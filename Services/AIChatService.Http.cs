using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// AIChatService HTTP 请求实现：非流式与流式（SSE）请求。
/// </summary>
public partial class AIChatService
{
    /// <summary>单轮文本/图片请求的便捷重载（不涉及工具）。</summary>
    private Task<AIChatResponse> SendRequestAsync(
        string system, string user, VisionImage? image, AiRequestSnapshot snapshot, CancellationToken ct)
        => SendRequestAsync(BuildSimpleMessages(system, user, image), snapshot, enableTools: false, ct);

    /// <summary>
    /// 发送一次非流式请求。<paramref name="enableTools"/> 为 true 时解析响应里的
    /// <c>tool_calls</c>，交由上层执行工具后回传。
    /// </summary>
    private async Task<AIChatResponse> SendRequestAsync(
        IReadOnlyList<ChatMessage> messages, AiRequestSnapshot snapshot, bool enableTools,
        CancellationToken ct, bool forceNoToolCall = false)
    {
        var jsonBody = JsonSerializer.Serialize(
            BuildChatBody(messages, snapshot, stream: false, enableTools, forceNoToolCall));
        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, snapshot.Endpoint) { Content = content };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {snapshot.ApiKey}");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // 0 表示不设置超时：CancelAfter(0) 会立刻取消，必须跳过
        if (snapshot.TimeoutSeconds > 0)
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(snapshot.TimeoutSeconds));

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowApiResponseExceptionAsync(response, timeoutCts.Token).ConfigureAwait(false);
        }

        var responseJson = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var message = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message");

            var text = ReadMessageText(message, "content");
            var toolCalls = ReadToolCalls(message);
            var textual = false;

            // 兼容把工具调用写进正文的模型（Qwen 老模板的 ✿FUNCTION✿ 协议）。
            // 只在允许工具的轮次里解析，普通对话不会误伤正文。
            if (enableTools && toolCalls.Count == 0)
            {
                var parsed = TextToolCallParser.Parse(text);
                if (parsed.Count > 0)
                {
                    toolCalls = parsed;
                    textual = true;
                    Logger.Info($"模型以文本形式请求工具 {parsed[0].Name}（未返回结构化 tool_calls），已按文本协议处理");
                }
            }

            return new AIChatResponse(text, ReadMessageText(message, "reasoning_content"))
            {
                ToolCalls = toolCalls,
                TextualToolCalls = textual
            };
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new AIResponseFormatException(
                "API 返回内容不符合 OpenAI Chat Completions JSON 格式",
                TruncateResponseBody(responseJson),
                ex);
        }
    }

    /// <summary>
    /// 读取 message 里的文本字段，兼容字符串与分段数组（多模态返回），
    /// 以及 content 为 null（只有 reasoning_content）的思考型模型。
    /// </summary>
    private static string ReadMessageText(JsonElement message, string propertyName, bool trim = true)
    {
        if (!message.TryGetProperty(propertyName, out var value)) return "";

        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                var text = value.GetString() ?? "";
                // 流式分片不能裁剪：分片之间往往靠空格/换行拼接，裁掉会把单词粘在一起
                return trim ? text.Trim() : text;

            case JsonValueKind.Array:
                var parts = value.EnumerateArray().Select(part => part.ValueKind switch
                {
                    JsonValueKind.String => part.GetString(),
                    JsonValueKind.Object when part.TryGetProperty("text", out var text2) &&
                                               text2.ValueKind == JsonValueKind.String
                        => text2.GetString(),
                    _ => null
                });
                var joined = string.Concat(parts);
                return trim ? joined.Trim() : joined;

            default:
                return "";
        }
    }

    /// <summary>
    /// 读取非流式响应里的 <c>message.tool_calls</c>。结构不符合预期时返回空集合，
    /// 让本轮退化成普通文本回答，而不是整次调用失败。
    /// </summary>
    internal static IReadOnlyList<ChatToolCall> ReadToolCalls(JsonElement message)
    {
        if (!message.TryGetProperty("tool_calls", out var toolCalls) ||
            toolCalls.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ChatToolCall>();
        }

        var result = new List<ChatToolCall>();
        var fallbackIndex = 0;
        foreach (var item in toolCalls.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                fallbackIndex++;
                continue;
            }

            var id = ReadOptionalString(item, "id");
            var name = "";
            var arguments = "";

            if (item.TryGetProperty("function", out var function) &&
                function.ValueKind == JsonValueKind.Object)
            {
                name = ReadOptionalString(function, "name");
                arguments = ReadOptionalString(function, "arguments");
            }

            result.Add(new ChatToolCall(
                string.IsNullOrEmpty(id) ? $"call_{fallbackIndex}" : id,
                name,
                string.IsNullOrEmpty(arguments) ? "{}" : arguments));
            fallbackIndex++;
        }

        return result;
    }

    /// <summary>
    /// 读取流式响应的 <c>delta.tool_calls</c> 分片。arguments 会被拆成多段，
    /// 由 <see cref="ToolCallAccumulator"/> 在调用侧按 index 聚合。
    /// </summary>
    internal static IReadOnlyList<ChatToolCallDelta> ReadToolCallDeltas(JsonElement delta)
    {
        if (!delta.TryGetProperty("tool_calls", out var toolCalls) ||
            toolCalls.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ChatToolCallDelta>();
        }

        var result = new List<ChatToolCallDelta>();
        foreach (var item in toolCalls.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            var index = 0;
            if (item.TryGetProperty("index", out var indexElement) &&
                indexElement.ValueKind == JsonValueKind.Number)
            {
                index = indexElement.GetInt32();
            }

            var id = ReadOptionalString(item, "id");
            string? name = null;
            string? arguments = null;

            if (item.TryGetProperty("function", out var function) &&
                function.ValueKind == JsonValueKind.Object)
            {
                if (function.TryGetProperty("name", out var nameElement) &&
                    nameElement.ValueKind == JsonValueKind.String)
                {
                    name = nameElement.GetString();
                }

                if (function.TryGetProperty("arguments", out var argumentsElement) &&
                    argumentsElement.ValueKind == JsonValueKind.String)
                {
                    arguments = argumentsElement.GetString();
                }
            }

            result.Add(new ChatToolCallDelta(
                index,
                string.IsNullOrEmpty(id) ? null : id,
                string.IsNullOrEmpty(name) ? null : name,
                string.IsNullOrEmpty(arguments) ? null : arguments));
        }

        return result;
    }

    private static string ReadOptionalString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    /// <summary>把「system + user（可带图）」包装成消息列表，供不涉及工具的调用点复用。</summary>
    private static IReadOnlyList<ChatMessage> BuildSimpleMessages(
        string system, string user, VisionImage? image)
    {
        return new[]
        {
            ChatMessage.System(system),
            image == null ? ChatMessage.User(user) : ChatMessage.UserWithImage(user, image)
        };
    }

    /// <summary>
    /// 发送流式请求并逐 token 返回。兼容 OpenAI Chat Completions SSE 格式；
    /// 传入 <paramref name="image"/> 时为带图片的多模态流式请求。
    /// </summary>
    /// <summary>单轮流式请求的便捷重载（不涉及工具）。</summary>
    private IAsyncEnumerable<AIChatStreamChunk> SendStreamRequestAsync(
        string system, string user, VisionImage? image, AiRequestSnapshot snapshot, CancellationToken ct)
        => SendStreamRequestAsync(BuildSimpleMessages(system, user, image), snapshot, enableTools: false, ct);

    private async IAsyncEnumerable<AIChatStreamChunk> SendStreamRequestAsync(
        IReadOnlyList<ChatMessage> messages, AiRequestSnapshot snapshot, bool enableTools,
        [EnumeratorCancellation] CancellationToken ct, bool forceNoToolCall = false)
    {
        var jsonBody = JsonSerializer.Serialize(
            BuildChatBody(messages, snapshot, stream: true, enableTools, forceNoToolCall));
        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, snapshot.Endpoint) { Content = content };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {snapshot.ApiKey}");
        request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // 0 表示不设置超时：CancelAfter(0) 会立刻取消，必须跳过
        if (snapshot.TimeoutSeconds > 0)
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(snapshot.TimeoutSeconds));
        var requestToken = timeoutCts.Token;

        using var response = await _http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            requestToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowApiResponseExceptionAsync(response, requestToken).ConfigureAwait(false);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(requestToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var completed = false;
        while (true)
        {
            var line = await reader.ReadLineAsync(requestToken).ConfigureAwait(false);
            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            var data = line["data:".Length..].Trim();
            if (data == "[DONE]")
            {
                completed = true;
                break;
            }

            var contentChunk = "";
            var reasoningChunk = "";
            IReadOnlyList<ChatToolCallDelta> toolDeltas = Array.Empty<ChatToolCallDelta>();
            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;
                if (root.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0)
                {
                    var choice = choices[0];
                    if (choice.TryGetProperty("finish_reason", out var finishReason) &&
                        finishReason.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrEmpty(finishReason.GetString()))
                    {
                        completed = true;
                    }

                    if (choice.TryGetProperty("delta", out var delta))
                    {
                        contentChunk = ReadMessageText(delta, "content", trim: false);
                        reasoningChunk = ReadMessageText(delta, "reasoning_content", trim: false);
                        toolDeltas = ReadToolCallDeltas(delta);
                    }
                }
            }
            catch (JsonException)
            {
                // SSE 数据行不是合法 JSON（如某些厂商的心跳），忽略。
                continue;
            }

            if (contentChunk.Length > 0 || reasoningChunk.Length > 0 || toolDeltas.Count > 0)
            {
                yield return new AIChatStreamChunk(contentChunk, reasoningChunk)
                {
                    ToolCallDeltas = toolDeltas
                };
            }
        }

        if (!completed)
            throw new IOException("AI 流式响应在完成标记前中断");
    }

    private static async Task ThrowApiResponseExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string? responseBody = null;
        try
        {
            responseBody = TruncateResponseBody(
                await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            responseBody = $"[读取 API 错误响应体失败: {ex.GetType().Name}: {ex.Message}]";
        }

        var requestId = TryGetHeader(response.Headers, "x-request-id") ??
                        TryGetHeader(response.Headers, "request-id") ??
                        TryGetHeader(response.Headers, "x-trace-id") ??
                        TryGetHeader(response.Headers, "trace-id");
        throw new AIHttpResponseException(
            response.StatusCode,
            response.ReasonPhrase,
            requestId,
            responseBody);
    }

    private static string TruncateResponseBody(string responseBody)
    {
        const int maxResponseBodyLength = 16 * 1024;
        return responseBody.Length > maxResponseBodyLength
            ? responseBody[..maxResponseBodyLength] + "\n...[响应体已截断]"
            : responseBody;
    }

    private static string? TryGetHeader(HttpResponseHeaders headers, string name)
    {
        return headers.TryGetValues(name, out var values)
            ? string.Join(", ", values)
            : null;
    }

    /// <summary>
    /// 构造 Chat Completions 请求体。对 DeepSeek 系列（含阿里百炼渠道托管的 DeepSeek）额外注入 thinking=disabled。
    /// DeepSeek（deepseek-flash / deepseek-v4.1-flash 等）默认开启思考模式，会先把思维链写入
    /// reasoning_content，若 max_tokens 较小，content 可能被挤空，导致流式解析不到
    /// 任何正文而触发「AI 流式响应内容为空」降级。本插件只需要简短文本，禁用思考模式
    /// 可获得稳定、更快的 content 输出。
    /// <para>
    /// 消息体由 <see cref="ChatMessage"/> 列表构造：单轮场景是 system + user（可带图），
    /// 工具调用场景会额外追加 assistant(tool_calls) 与 tool(结果) 两条消息。
    /// </para>
    /// </summary>
    private Dictionary<string, object?> BuildChatBody(
        IReadOnlyList<ChatMessage> messages, AiRequestSnapshot snapshot, bool stream,
        bool enableTools = false, bool forceNoToolCall = false)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = snapshot.Model,
            ["temperature"] = snapshot.Temperature,
            ["stream"] = stream,
            ["messages"] = messages.Select(message => message.ToPayload()).ToArray()
        };

        // 0 表示不限制输出长度：不下发 max_tokens，由模型/接口的默认上限决定
        if (snapshot.MaxTokens > 0)
            body["max_tokens"] = snapshot.MaxTokens;

        // 按接口族下发深度思考与思考强度（各厂商字段不同，乱塞会被 400）
        ThinkingRequestBuilder.Apply(
            body, snapshot.Endpoint, snapshot.Model,
            snapshot.EnableDeepThinking, snapshot.ThinkingEffort);

        // 工具定义：中间轮允许模型继续调用（搜索 → 抓取），收尾轮由调用方
        // 传 forceNoToolCall 强制作答。消息里已有 tool 结果时必须继续携带 tools 定义，
        // 否则严格实现会判「有 tool 消息却没有 tools」直接 400。
        // web_fetch 可以在设置里单独关掉，关闭时只下发 web_search。
        ToolRequestBuilder.Apply(body, enableTools, forceNoToolCall, enableFetch: EnableWebFetch);

        return body;
    }
}
