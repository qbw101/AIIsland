using System.Text.Json.Serialization;

namespace ClassIsland.AISmartClass.Models;

/// <summary>
/// 模型对图片输入（视觉）能力的判定结果。
/// 只描述「能不能读图片」，不评价识别质量。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VisionSupport
{
    /// <summary>还没有检测结论，最多只能按模型名称猜测。</summary>
    Unknown = 0,

    /// <summary>可以接收图片输入。</summary>
    Supported = 1,

    /// <summary>只能处理文本，拒绝图片输入。</summary>
    Unsupported = 2
}

/// <summary>
/// <see cref="VisionSupport"/> 在配置文件里的字符串形式。
/// 用字符串而不是数字，便于用户直接查看配置，也给未来取值留出空间。
/// </summary>
public static class VisionSupportValues
{
    public const string Unknown = "unknown";
    public const string Supported = "supported";
    public const string Unsupported = "unsupported";

    public static string ToStorageValue(VisionSupport value) => value switch
    {
        VisionSupport.Supported => Supported,
        VisionSupport.Unsupported => Unsupported,
        _ => Unknown
    };

    public static VisionSupport FromStorageValue(string? value)
    {
        return (value ?? "").Trim().ToLowerInvariant() switch
        {
            Supported => VisionSupport.Supported,
            Unsupported => VisionSupport.Unsupported,
            _ => VisionSupport.Unknown
        };
    }
}
