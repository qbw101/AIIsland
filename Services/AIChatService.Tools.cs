using System.Text;
using ClassIsland.AISmartClass.Models;
using ClassIsland.AISmartClass.Services.WebSearch;

namespace ClassIsland.AISmartClass.Services;

/// <summary>一次带联网能力的对话结果。</summary>
/// <param name="Content">最终正文。</param>
/// <param name="Reasoning">累积的思考过程。</param>
/// <param name="UsedWebSearch">本次是否真的执行过联网检索。</param>
/// <param name="Sources">检索命中的来源，供调用方展示引用列表。</param>
/// <param name="SearchQuery">实际使用的检索关键词，多个用「；」分隔；未联网时为空。</param>
internal sealed record AIChatToolResult(
    string Content,
    string Reasoning,
    bool UsedWebSearch,
    IReadOnlyList<WebSearchItem> Sources,
    string SearchQuery = "");

/// <summary>
/// 联网搜索的 function calling 实现。
/// <para>
/// 流程：第一轮带 <c>tools</c> 请求 → 模型若返回 <c>tool_calls</c> 就执行检索 →
/// 追加 assistant(tool_calls) 与每个调用各一条 tool(结果) 消息 → 最后一轮禁止调用、用资料作答。
/// 最多 <see cref="MaxToolRounds"/> 轮。模型可能一次返回多个工具调用，**必须逐个回传结果**：
/// 少回一条，严格实现（DeepSeek 等）会直接 400
/// <c>insufficient tool messages following tool_calls message</c>。
/// 实际检索次数由 <see cref="ToolExecutionBudget"/> 限制，同一关键词只搜一次。
/// </para>
/// <para>
/// 这条路径**不读写缓存**：联网结果具有时效性，且缓存结构不承载 tool_calls。
/// 未启用搜索或搜索不可用时自动退化为原有的单轮调用，行为与原实现完全一致。
/// </para>
/// </summary>
public partial class AIChatService
{
    /// <summary>
    /// 最多执行几轮工具调用。2 支持「搜索 → 打开网页 → 作答」的完整链路；
    /// 模型只想搜或只想抓时会更早结束。再多的轮次收益低且容易拖长时间。
    /// </summary>
    private const int MaxToolRounds = 2;

    /// <summary>
    /// 收尾轮最多补几次工具调用。已经下发 <c>tool_choice:"none"</c>，模型正常不会再调用；
    /// 个别兼容层会忽略该字段，给它一次补齐后重新作答的机会即可，避免死循环。
    /// </summary>
    private const int MaxFinalToolRounds = 1;

    /// <summary>启用联网时的最小输出上限：模型要先吐出 tool_call 的 JSON，200 会被挤空。</summary>
    private const int WebSearchMinMaxTokens = 800;

    /// <summary>启用联网时的最小超时：检索 + 两轮生成通常超过默认 10 秒。</summary>
    private const int WebSearchMinTimeoutSeconds = 30;

    private const int MaxQueryLength = 128;

    /// <summary>仅在启用联网时追加到 system prompt 末尾，指导模型怎么用搜索工具。</summary>
    private const string WebSearchSystemHint =
        "你可以调用 web_search 工具联网检索实时信息。" +
        "当问题涉及最新事件、具体数据、你不确定或可能已经过时的事实时，先搜索再回答；" +
        "纯闲聊、常识、创作类问题不要联网。引用资料时用 [1][2] 标注来源。";

    /// <summary>启用网页抓取时追加，说明 web_fetch 的使用时机。</summary>
    private const string WebFetchSystemHint =
        "你也可以调用 web_fetch 工具打开网页读取正文：" +
        "当搜索摘要不足以回答、或用户直接给出链接时，打开最相关的页面（优先官方、新闻与百科源）。";

    /// <summary>按设置拼装联网提示：关闭抓取时不提 web_fetch，避免模型调用不存在的工具。</summary>
    private string BuildWebSearchHint()
        => EnableWebFetch ? WebSearchSystemHint + WebFetchSystemHint : WebSearchSystemHint;

    /// <summary>
    /// 检索完成后的收尾指令。思考型模型在「还想再搜」时容易只输出思考过程、不给正文，
    /// 这一句明确要求直接作答。
    /// </summary>
    private const string FinalAnswerHint =
        "（系统提示）检索已完成。请立即基于以上资料给出最终回答，不要调用任何工具，也不要只输出思考过程。";

    /// <summary>正文为空时返回给调用方的说明，避免界面上出现「成功但什么都没有」。</summary>
    internal const string EmptyContentNotice =
        "（模型只返回了思考过程，没有给出正文。可以尝试关闭深度思考，或换一个模型重试。）";

    /// <summary>
    /// 联网搜索服务。为 null 表示该能力未初始化，此时所有联网入口都会退化成普通对话。
    /// 用属性而非构造函数注入：<see cref="WebSearchService"/> 是 internal，
    /// 不能出现在公开构造函数签名里。
    /// </summary>
    internal WebSearchService? WebSearch { get; set; }

    /// <summary>网页抓取服务（web_fetch 工具）。为 null 表示未初始化，此时该工具返回不可用说明。</summary>
    internal WebFetch.WebFetchService? WebFetch { get; set; }

    /// <summary>DeepSeek 官方抓取兜底；为 null 表示未初始化，本地失败时直接降级。</summary>
    internal WebFetch.DeepSeekFetchProvider? DeepSeekFetch { get; set; }

    /// <summary>当前是否具备联网能力（开关已开且至少配置了一个搜索提供者）。</summary>
    internal bool IsWebSearchAvailable => WebSearch?.IsAvailable == true;

    /// <summary>带联网能力的非流式对话。与流式入口共用工具循环。</summary>
    internal Task<AIChatToolResult> ChatWithWebSearchAsync(
        string systemPrompt,
        string userMessage,
        double temperature,
        CancellationToken ct,
        bool throwOnError,
        bool? enableDeepThinking = null,
        ThinkingEffort? thinkingEffort = null)
        => RunWebToolConversationAsync(systemPrompt, userMessage, null, temperature, ct, throwOnError,
            enableDeepThinking, thinkingEffort);

    /// <summary>带联网能力的流式对话。传输层负责增量输出，工具调度与非流式共用。</summary>
    internal Task<AIChatToolResult> ChatWithWebSearchStreamAsync(
        string systemPrompt,
        string userMessage,
        Action<AIChatStreamUpdate> onUpdate,
        double temperature,
        CancellationToken ct,
        bool throwOnError,
        bool? enableDeepThinking = null,
        ThinkingEffort? thinkingEffort = null)
    {
        ArgumentNullException.ThrowIfNull(onUpdate);
        return RunWebToolConversationAsync(systemPrompt, userMessage, onUpdate, temperature, ct, throwOnError,
            enableDeepThinking, thinkingEffort);
    }

    private async Task<AIChatToolResult> RunWebToolConversationAsync(
        string systemPrompt,
        string userMessage,
        Action<AIChatStreamUpdate>? onUpdate,
        double temperature,
        CancellationToken ct,
        bool throwOnError,
        bool? enableDeepThinking,
        ThinkingEffort? thinkingEffort)
    {
        var search = WebSearch;
        var snapshot = CreateToolSnapshot(temperature, enableDeepThinking, thinkingEffort);
        var isStreaming = onUpdate != null;
        if (search is null || !search.IsAvailable || string.IsNullOrWhiteSpace(snapshot.ApiKey))
        {
            if (!isStreaming)
                return await DegradeAsync(systemPrompt, userMessage, temperature, ct, throwOnError,
                    enableDeepThinking, thinkingEffort).ConfigureAwait(false);

            var plain = await ChatStreamResponseAsync(systemPrompt, userMessage, onUpdate!, temperature, ct,
                enableDeepThinking, thinkingEffort).ConfigureAwait(false);
            return new AIChatToolResult(plain.Content, plain.Reasoning, false, Array.Empty<WebSearchItem>());
        }

        void Emit(AIChatStreamUpdate update)
        {
            if (onUpdate == null) return;
            try { onUpdate(update); }
            catch (Exception ex) { throw new StreamCallbackException(ex.Message, ex); }
        }

        var messages = new List<ChatMessage>
        {
            ChatMessage.System(systemPrompt + "\n\n" + BuildWebSearchHint()),
            ChatMessage.User(userMessage)
        };
        var sources = new List<WebSearchItem>();
        var reasoning = new StringBuilder();
        var usedWebSearch = false;
        var queries = new List<string>();
        var budget = new ToolExecutionBudget();

        async Task<AIChatResponse> SendTurnAsync(bool enableTools, bool forceNoToolCall = false)
        {
            EnsureToolCallsAnswered(messages);
            if (isStreaming)
                return await SendStreamTurnAsync(messages, snapshot, enableTools, Emit, reasoning, ct,
                    forceNoToolCall).ConfigureAwait(false);

            var turn = await SendWithToolFallbackAsync(messages, snapshot, enableTools, ct, forceNoToolCall)
                .ConfigureAwait(false);
            if (turn.HasReasoning) reasoning.Append(turn.Reasoning);
            return turn;
        }

        async Task ExecuteTurnAsync(AIChatResponse turn)
        {
            ct.ThrowIfCancellationRequested();
            Emit(BuildSearchingUpdate(turn.ToolCalls, userMessage));
            var executions = await ExecuteToolCallsAsync(turn.ToolCalls, userMessage, search, budget, ct)
                .ConfigureAwait(false);
            AppendToolTurn(messages, turn, executions);
            sources.AddRange(executions.SelectMany(execution => execution.Items));
            usedWebSearch |= executions.Any(execution => execution.Kind is ToolKind.Search or ToolKind.Fetch);
            CollectQueries(queries, executions);
            Emit(new AIChatStreamUpdate("", "", "", "") { Status = "answering" });
        }

        Task<AIChatToolResult> CompleteAsync(AIChatResponse turn)
            => BuildResultAsync(turn.Content, reasoning, usedWebSearch, sources, snapshot, isStreaming,
                systemPrompt, userMessage, string.Join("；", queries));

        try
        {
            for (var round = 0; round < MaxToolRounds; round++)
            {
                var turn = await SendTurnAsync(enableTools: true).ConfigureAwait(false);
                if (turn.ToolCalls.Count == 0) return await CompleteAsync(turn).ConfigureAwait(false);
                await ExecuteTurnAsync(turn).ConfigureAwait(false);
            }

            AppendFinalAnswerHint(messages);
            var hasStructuredToolHistory = HasStructuredToolHistory(messages);
            for (var attempt = 0; ; attempt++)
            {
                var final = await SendTurnAsync(hasStructuredToolHistory, forceNoToolCall: true).ConfigureAwait(false);
                if (final.ToolCalls.Count == 0 || attempt >= MaxFinalToolRounds)
                    return await CompleteAsync(final).ConfigureAwait(false);

                await ExecuteTurnAsync(final).ConfigureAwait(false);
                AppendFinalAnswerHint(messages);
            }
        }
        catch (Exception ex) when (!throwOnError && ex is not OperationCanceledException && ex is not StreamCallbackException)
        {
            Logger.Error(ex, "联网对话执行失败，已降级");
            var fallback = _fallback.GetErrorPhrase(AIRequestFailureClassifier.Classify(ex, 1, 0));
            Emit(new AIChatStreamUpdate(fallback, "", fallback, ""));
            return new AIChatToolResult(fallback, reasoning.ToString(), usedWebSearch, sources, string.Join("；", queries));
        }
    }

    /// <summary>
    /// 追加收尾指令。文本协议路径的最后一条已经是带指令的检索结果 user 消息，
    /// 不再重复追加，避免出现连续两条 user 消息。
    /// </summary>
    private static void AppendFinalAnswerHint(List<ChatMessage> messages)
    {
        if (messages.Count > 0 && messages[^1].Role == "user") return;
        messages.Add(ChatMessage.User(FinalAnswerHint));
    }

    /// <summary>构造联网轮次的请求快照：抬高输出上限与超时，且关闭缓存。</summary>
    private AiRequestSnapshot CreateToolSnapshot(
        double temperature,
        bool? enableDeepThinking,
        ThinkingEffort? thinkingEffort)
    {
        lock (_settingsLock)
        {
            var thinkingEnabled = enableDeepThinking ?? EnableDeepThinking;
            var effort = thinkingEffort ?? ThinkingEffort;

            var maxTokens = ThinkingRequestBuilder.GetEffectiveMaxTokens(MaxTokens, thinkingEnabled, effort);
            // 0 表示不限制，保持 0；否则至少留出 tool_call 与最终回答的空间
            if (maxTokens > 0 && maxTokens < WebSearchMinMaxTokens)
                maxTokens = WebSearchMinMaxTokens;

            var timeout = TimeoutSeconds <= 0
                ? 0
                : Math.Max(TimeoutSeconds, WebSearchMinTimeoutSeconds);

            return new AiRequestSnapshot(
                Endpoint,
                ApiKey,
                Model,
                maxTokens,
                timeout,
                Math.Max(0, CacheMinutes),
                Math.Clamp(MaxRetries, 0, 5),
                EnableCache: false,
                temperature >= 0 ? temperature : GetTemperature(),
                thinkingEnabled,
                effort);
        }
    }

}
