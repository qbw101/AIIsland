using System;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace ClassIsland.AISmartClass.PublicApi;

/// <summary>
/// 授权模式。
/// </summary>
public enum AIIslandAuthMode
{
    /// <summary>每次调用都弹出确认对话框。</summary>
    [Description("每次确认")]
    PerCallConfirm = 0,

    /// <summary>已授权插件直接调用，不弹窗。</summary>
    [Description("直接授权")]
    Trusted = 1
}

/// <summary>
/// 单个插件对 AIIsland 的授权记录。
/// </summary>
public class AIIslandPluginAuthEntry
{
    /// <summary>调用方标识。当前由调用栈中的外部程序集名称推断。</summary>
    [JsonPropertyName("pluginId")]
    public string PluginId { get; set; } = "";

    /// <summary>调用方显示名称。当前通常与程序集名称相同。</summary>
    [JsonPropertyName("pluginName")]
    public string PluginName { get; set; } = "";

    /// <summary>当前授权模式。</summary>
    [JsonPropertyName("authMode")]
    public AIIslandAuthMode AuthMode { get; set; } = AIIslandAuthMode.PerCallConfirm;

    /// <summary>授权时间（切换为 Trusted 的时间）。</summary>
    [JsonPropertyName("authorizedAt")]
    public DateTime? AuthorizedAt { get; set; }

    /// <summary>累计调用次数。</summary>
    [JsonPropertyName("callCount")]
    public int CallCount { get; set; }

    /// <summary>最后调用时间。</summary>
    [JsonPropertyName("lastCalledAt")]
    public DateTime? LastCalledAt { get; set; }

    /// <summary>最后调用的方法名。</summary>
    [JsonPropertyName("lastMethod")]
    public string? LastMethod { get; set; }
}
