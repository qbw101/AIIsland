using System.Text.Json.Serialization;

namespace ClassIsland.AISmartClass.PublicApi;

/// <summary>
/// 一次联网检索命中的来源。仅在 <see cref="AIIslandChatResult.UsedWebSearch"/> 为 true 时有值。
/// </summary>
public class AIIslandWebSource
{
    /// <summary>网页标题。</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    /// <summary>网页地址。</summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    /// <summary>检索结果里的摘要片段，可直接展示。</summary>
    [JsonPropertyName("snippet")]
    public string Snippet { get; set; } = "";

    /// <summary>发布时间（来源于搜索服务，格式不统一，可能为 null）。</summary>
    [JsonPropertyName("date")]
    public string? Date { get; set; }
}
