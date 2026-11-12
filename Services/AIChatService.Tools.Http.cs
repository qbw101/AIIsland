using System.Net;
using System.Text;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 联网搜索的传输层：流式单轮、重试，以及「接口不支持 tools」时的退化。
/// 从 <c>AIChatService.Tools.cs</c> 拆出，避免单文件过长。
/// </summary>
public partial class AIChatService
{
    /// <summary>发送一轮流式请求，累积正文、思考与 tool_calls 分片。</summary>
    private async Task<AIChatResponse> SendStreamTurnAsync(
        IReadOnlyList<ChatMessage> messages,
        AiRequestSnapshot snapshot,
        bool enableTools,
        Action<AIChatStreamUpdate> emit,
        StringBuilder reasoning,
        CancellationToken ct,
        bool forceNoToolCall = false)
    {
        // 过滤器负责两件事：把逐字到达的 ✿FUNCTION✿ 工具调用扣住不显示，
        // 以及剥掉最终回答开头的 ✿RETURN✿ 标记。
        var filter = new TextToolCallStreamFilter();
        var turnReasoning = new StringBuilder();
        var accumulator = new ToolCallAccumulator();

        await foreach (var chunk in SendStreamRequestAsync(messages, snapshot, enableTools, ct, forceNoToolCall)
                           .ConfigureAwait(false))
        {
            turnReasoning.Append(chunk.Reasoning);
            foreach (var delta in chunk.ToolCallDeltas)
                accumulator.Append(delta);

            var visibleDelta = filter.Push(chunk.Content);

            if (visibleDelta.Length > 0 || chunk.Reasoning.Length > 0)
            {
                // 每轮只推送本轮的增量快照；跨轮累积由调用方的 reasoning 负责
                emit(new AIChatStreamUpdate(
                    visibleDelta,
                    chunk.Reasoning,
                    filter.Visible,
                    turnReasoning.ToString()));
            }
        }

        var raw = filter.Raw.Trim();
        var toolCalls = accumulator.Build();
        var textual = false;

        if (enableTools && toolCalls.Count == 0 && filter.IsToolCall)
        {
            toolCalls = TextToolCallParser.Parse(raw);
            textual = toolCalls.Count > 0;
            if (textual)
                Logger.Info($"模型以文本形式请求工具 {toolCalls[0].Name}（流式响应无结构化 tool_calls），已按文本协议处理");
        }

        // 文本工具调用轮次没有可展示的正文，但要把原始文本回传给下一轮
        var content = textual ? raw : filter.Visible.Trim();
        var turnThinking = turnReasoning.ToString().Trim();
        if (turnThinking.Length > 0) reasoning.Append(turnThinking);

        if (string.IsNullOrEmpty(content) && string.IsNullOrEmpty(turnThinking) && toolCalls.Count == 0)
            throw new InvalidDataException("AI 流式响应内容为空");

        return new AIChatResponse(content, turnThinking)
        {
            ToolCalls = toolCalls,
            TextualToolCalls = textual
        };
    }

    /// <summary>
    /// 发送非流式请求，并在接口拒绝 <c>tools</c> 时丢弃工具定义重发一次，
    /// 让功能退化为普通对话而不是整轮失败。
    /// <para>
    /// 只要返回 400 就尝试降级：各家对「不支持工具」「thinking 与 tools 不能同用」
    /// 这类错误的措辞完全不同（有的是 <c>tools is not supported</c>，有的是
    /// <c>thinking is not supported with tools</c>），只按关键词匹配很容易漏判。
    /// 400 不产生计费，多试一次的代价可以忽略。
    /// </para>
    /// </summary>
    private async Task<AIChatResponse> SendWithToolFallbackAsync(
        IReadOnlyList<ChatMessage> messages,
        AiRequestSnapshot snapshot,
        bool enableTools,
        CancellationToken ct,
        bool forceNoToolCall = false)
    {
        try
        {
            return await SendWithRetryAsync(messages, snapshot, enableTools, ct, forceNoToolCall)
                .ConfigureAwait(false);
        }
        catch (AIHttpResponseException ex) when (
            enableTools &&
            ex.StatusCode == HttpStatusCode.BadRequest &&
            // 已经开始回传 tool 结果的轮次不能丢 tools：那样反而变成「有 tool 消息却没有
            // tools 定义」，同样非法。只有首轮（还没有工具历史）才值得降级重试。
            !messages.Any(message => message.ToolCalls is { Count: > 0 }))
        {
            Logger.Info($"带工具的请求被拒绝（400），丢弃 tools 重试一次：{ex.ResponseBody}");
            return await SendWithRetryAsync(messages, snapshot, enableTools: false, ct).ConfigureAwait(false);
        }
    }

    /// <summary>简化版重试：只重试可重试错误，取消原样抛出。</summary>
    private async Task<AIChatResponse> SendWithRetryAsync(
        IReadOnlyList<ChatMessage> messages,
        AiRequestSnapshot snapshot,
        bool enableTools,
        CancellationToken ct,
        bool forceNoToolCall = false)
    {
        Exception? lastException = null;
        for (var attempt = 0; attempt <= snapshot.MaxRetries; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(1000, ct).ConfigureAwait(false);

            try
            {
                return await SendRequestAsync(messages, snapshot, enableTools, ct, forceNoToolCall)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                lastException = new TimeoutException(
                    snapshot.TimeoutSeconds > 0
                        ? $"本地等待 API 响应超过 {snapshot.TimeoutSeconds} 秒"
                        : "请求在未设置超时的情况下被中断",
                    ex);
            }
            catch (Exception ex)
            {
                lastException = ex;
                var failure = AIRequestFailureClassifier.Classify(ex, attempt + 1, 0);
                // 不可重试的错误原样抛出，让上层能识别具体状态码（例如「接口不支持 tools」的 400）
                if (!failure.IsRetryable) throw;
            }
        }

        throw new InvalidOperationException(
            $"AI 请求失败: {lastException?.Message ?? "未知错误"}", lastException);
    }
}
