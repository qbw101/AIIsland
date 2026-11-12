using System.Text.Json.Serialization;

namespace ClassIsland.AISmartClass.PublicApi;

/// <summary>
/// AI 对话返回结果。
/// </summary>
public class AIIslandChatResult
{
    /// <summary>AI 生成的文本内容。失败时为空字符串或错误信息。</summary>
    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    /// <summary>
    /// 模型的思考过程（<c>reasoning_content</c>）。
    /// 只有开启深度思考且厂商返回思考内容时才非空；未开启或厂商不返回时为空字符串。
    /// </summary>
    [JsonPropertyName("reasoning")]
    public string Reasoning { get; set; } = "";

    /// <summary>调用是否成功。</summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>失败时的错误信息。成功时为 null。</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>是否使用了本地降级内容（AI 不可用时）。</summary>
    [JsonPropertyName("isFallback")]
    public bool IsFallback { get; set; }

    /// <summary>本次调用的耗时（毫秒）。</summary>
    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }

    /// <summary>本次调用是否真的执行了联网检索（模型可能判断不需要联网，此时为 false）。</summary>
    [JsonPropertyName("usedWebSearch")]
    public bool UsedWebSearch { get; set; }

    /// <summary>
    /// 本次联网实际使用的检索关键词，多个用「；」分隔；未联网时为 null。
    /// 可直接用于「已按 xxx 搜索」这类说明。
    /// </summary>
    [JsonPropertyName("searchQuery")]
    public string? SearchQuery { get; set; }

    /// <summary>检索命中的来源列表；未联网或没有结果时为 null。</summary>
    [JsonPropertyName("webSources")]
    public IReadOnlyList<AIIslandWebSource>? WebSources { get; set; }

    public static AIIslandChatResult Ok(string content, long durationMs) => new()
    {
        Content = content,
        Success = true,
        DurationMs = durationMs
    };

    public static AIIslandChatResult Ok(string content, string reasoning, long durationMs) => new()
    {
        Content = content,
        Reasoning = reasoning,
        Success = true,
        DurationMs = durationMs
    };

    public static AIIslandChatResult Fail(string error, long durationMs = 0) => new()
    {
        Success = false,
        Error = error,
        DurationMs = durationMs
    };
}
