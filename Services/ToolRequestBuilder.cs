namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 把工具定义注入 Chat Completions 请求体。与 <see cref="ThinkingRequestBuilder"/> 同样是
/// 「body 建好后再补字段」的装饰器，避免把厂商差异散落到请求构造处。
/// <para>
/// 首轮只下发 <c>tools</c>，由模型自主决定是否调用。
/// 一旦对话里已经出现过工具调用（即后续轮次要回传 <c>role:"tool"</c> 消息），
/// 必须继续携带 <c>tools</c> 定义——多数严格实现会校验「有 tool 消息就必须有 tools」，
/// 缺了会直接 400；此时追加 <c>tool_choice:"none"</c> 禁止模型再次调用。
/// </para>
/// </summary>
internal static class ToolRequestBuilder
{
    /// <summary>联网搜索工具名。回传 tool 消息时需与之一致。</summary>
    public const string WebSearchToolName = "web_search";

    /// <summary>打开网页读取正文的工具名。</summary>
    public const string WebFetchToolName = "web_fetch";

    /// <summary>工具定义签名，纳入日志与诊断，便于区分「带工具」与「不带工具」的请求。</summary>
    public const string WebSearchSignature = "tools:v2";

    /// <summary>工具描述直接决定模型会不会主动联网，措辞需要同时给出正例与反例。</summary>
    private const string WebSearchDescription =
        "联网检索实时信息。适用于新闻、天气、比分、价格、最新版本、你不确定或可能已经过时的事实。" +
        "不要用于闲聊、创作、数学计算或常识问题。";

    private const string WebFetchDescription =
        "打开一个网页并读取正文。适用于：搜索结果的摘要不足以回答、需要页面里的具体内容、" +
        "或用户直接给出了链接。优先打开官方、新闻、百科类页面；一次对话最多使用 2 次。";

    public static void Apply(
        IDictionary<string, object?> body, bool enableTools, bool forceNoToolCall = false, bool enableFetch = true)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enableTools) return;

        body["tools"] = enableFetch
            ? new object[] { BuildWebSearchTool(), BuildWebFetchTool() }
            : new object[] { BuildWebSearchTool() };

        // 已经有工具执行结果时，禁止模型继续调用，强制它用现有资料作答
        if (forceNoToolCall)
            body["tool_choice"] = "none";
    }

    /// <summary>构造 web_search 的工具定义。返回新对象，调用方可安全持有。</summary>
    public static object BuildWebSearchTool() => new
    {
        type = "function",
        function = new
        {
            name = WebSearchToolName,
            description = WebSearchDescription,
            parameters = new
            {
                type = "object",
                properties = new
                {
                    query = new
                    {
                        type = "string",
                        description = "搜索关键词，简短精确，不超过 64 字"
                    }
                },
                required = new[] { "query" }
            }
        }
    };

    /// <summary>构造 web_fetch 的工具定义。</summary>
    public static object BuildWebFetchTool() => new
    {
        type = "function",
        function = new
        {
            name = WebFetchToolName,
            description = WebFetchDescription,
            parameters = new
            {
                type = "object",
                properties = new
                {
                    url = new
                    {
                        type = "string",
                        description = "要打开的完整 URL，必须以 http:// 或 https:// 开头"
                    }
                },
                required = new[] { "url" }
            }
        }
    };
}
