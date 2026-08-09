using System.Text.Json.Serialization;

namespace ClassIsland.AISmartClass.PublicApi;

/// <summary>
/// AI 对话的可选参数。
/// </summary>
public class AIIslandChatOptions
{
    /// <summary>温度值（0-2），越高越随机。为 null 时使用 AIIsland 全局设置。</summary>
    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    /// <summary>
    /// 预留的单次回复最大 token 数。当前公开 API 实现尚未应用此值，
    /// 实际请求使用 AIIsland 全局设置。
    /// </summary>
    [JsonPropertyName("maxTokens")]
    public int? MaxTokens { get; set; }

    /// <summary>调用方对此次请求用途的简短描述，会显示在授权确认对话框中。</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>是否绕过缓存。为 true 时会先清空 AIIsland 的全局 AI 缓存。默认 false。</summary>
    [JsonPropertyName("bypassCache")]
    public bool BypassCache { get; set; }
}
