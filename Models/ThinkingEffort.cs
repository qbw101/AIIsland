using System.ComponentModel;
using System.Text.Json.Serialization;

namespace ClassIsland.AISmartClass.Models;

/// <summary>
/// 思考强度。数值越大，模型在回答前愿意花的推理越长，速度越慢、token 消耗越高。
/// <see cref="Auto"/> 表示不下发强度参数，交给模型自己决定。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ThinkingEffort
{
    /// <summary>跟随模型默认强度（不下发参数）。</summary>
    [Description("自动（跟随模型）")]
    Auto = 0,

    /// <summary>低：更快、更省，适合提醒类短句。</summary>
    [Description("低 — 更快")]
    Low = 1,

    /// <summary>中：速度与质量平衡。</summary>
    [Description("中 — 平衡")]
    Medium = 2,

    /// <summary>高：更深入，适合复杂问题。</summary>
    [Description("高 — 更深入")]
    High = 3
}

/// <summary>思考强度对应的请求值。</summary>
public static class ThinkingEffortValues
{
    /// <summary>转成 OpenAI 兼容接口的 <c>reasoning_effort</c> 取值；Auto 返回 null。</summary>
    public static string? ToRequestValue(this ThinkingEffort effort) => effort switch
    {
        ThinkingEffort.Low => "low",
        ThinkingEffort.Medium => "medium",
        ThinkingEffort.High => "high",
        _ => null
    };

    /// <summary>
    /// 开启深度思考时为思考过程预留的输出 token。很多接口把思考算进 max_tokens，
    /// 不预留会把正文挤空（表现为「回复为空」直接降级）。
    /// </summary>
    public static int GetThinkingReserveTokens(this ThinkingEffort effort) => effort switch
    {
        ThinkingEffort.Low => 512,
        ThinkingEffort.Medium => 1024,
        ThinkingEffort.High => 2048,
        _ => 1024
    };
}
