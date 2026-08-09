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

    public static AIIslandChatResult Ok(string content, long durationMs) => new()
    {
        Content = content,
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
