using System.Text.Json.Serialization;
using ClassIsland.AISmartClass.Models;

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
    /// 本次调用是否开启深度思考（推理）。为 null 时使用 AIIsland 设置页的「深度思考」开关。
    /// 开启后模型会先推理再回答，更准但更慢、更耗 token。
    /// </summary>
    [JsonPropertyName("enableDeepThinking")]
    public bool? EnableDeepThinking { get; set; }

    /// <summary>
    /// 本次调用的思考强度（自动 / 低 / 中 / 高）。为 null 时使用 AIIsland 全局设置；
    /// 仅在开启深度思考时生效，不支持的模型会忽略该值。
    /// </summary>
    [JsonPropertyName("thinkingEffort")]
    public ThinkingEffort? ThinkingEffort { get; set; }

    /// <summary>
    /// 预留的单次回复最大 token 数。当前公开 API 实现尚未应用此值，
    /// 实际请求使用 AIIsland 全局设置。
    /// </summary>
    [JsonPropertyName("maxTokens")]
    public int? MaxTokens { get; set; }

    /// <summary>
    /// 本次调用是否允许模型联网搜索。为 null 时使用 AIIsland 设置页的「允许模型联网搜索」开关。
    /// 开启后模型会在需要实时信息时自主调用搜索工具；该次请求的输出上限会自动抬到
    /// 至少 800 token、超时至少 30 秒，耗时也会相应增加。
    /// </summary>
    [JsonPropertyName("enableWebSearch")]
    public bool? EnableWebSearch { get; set; }

    /// <summary>调用方对此次请求用途的简短描述，会显示在授权确认对话框中。</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>是否绕过缓存。为 true 时会先清空 AIIsland 的全局 AI 缓存。默认 false。</summary>
    [JsonPropertyName("bypassCache")]
    public bool BypassCache { get; set; }
}
