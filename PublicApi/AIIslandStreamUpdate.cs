using System.Text.Json.Serialization;

namespace ClassIsland.AISmartClass.PublicApi;

/// <summary>
/// 流式输出的一次推送：本次增量 + 截至目前的完整快照。
/// 同一次推送里正文与思考过程通常只有一段有增量。
/// </summary>
public class AIIslandStreamUpdate
{
    /// <summary>本次新增的正文片段。</summary>
    [JsonPropertyName("contentDelta")]
    public string ContentDelta { get; init; } = "";

    /// <summary>本次新增的思考片段，仅在开启深度思考且厂商返回思考内容时有值。</summary>
    [JsonPropertyName("reasoningDelta")]
    public string ReasoningDelta { get; init; } = "";

    /// <summary>截至目前的完整正文。</summary>
    [JsonPropertyName("content")]
    public string Content { get; init; } = "";

    /// <summary>截至目前的完整思考过程。</summary>
    [JsonPropertyName("reasoning")]
    public string Reasoning { get; init; } = "";

    /// <summary>
    /// 阶段状态。<c>"searching"</c> 表示模型要求联网、正在执行检索；
    /// <c>"answering"</c> 表示检索完成、开始生成最终回答，调用方应清空此前已显示的内容
    /// （第一轮的过渡语会与最终答案重复）；空字符串表示普通输出。
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = "";

    /// <summary>
    /// 检索关键词。仅当 <see cref="Status"/> 为 <c>"searching"</c> 时可能有值，
    /// 内容是模型实际传给 <c>web_search</c> 的关键词（多个用「；」分隔，最多 3 个）。
    /// 可直接显示为「正在搜索：{SearchQuery}」；为空时退回通用的「正在搜索…」。
    /// </summary>
    [JsonPropertyName("searchQuery")]
    public string SearchQuery { get; init; } = "";
}
