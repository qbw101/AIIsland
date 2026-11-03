using System.ComponentModel;
using System.Text.Json.Serialization;

namespace ClassIsland.AISmartClass.Models;

/// <summary>
/// 业务时间来源。插件内所有与课表、日期相关的判断都按此来源取“现在”。
/// </summary>
/// <remarks>
/// 该选项在插件全局设置中配置，不再由单个组件覆盖。
/// 枚举成员名称是持久化契约，重命名会破坏已有配置文件。
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BusinessTimeSource
{
    /// <summary>使用系统时间（Windows 本地时钟）</summary>
    [Description("系统时间")]
    SystemTime,

    /// <summary>使用 ClassIsland 时间（包含用户在 ClassIsland 中设置的时间偏移与校时）</summary>
    [Description("ClassIsland 时间")]
    ClassIslandTime
}

/// <summary>时间来源辅助方法。</summary>
public static class BusinessTimeSourceExtensions
{
    /// <summary>
    /// 归一化外部输入的枚举值。旧配置或手工编辑可能写入未定义数字，
    /// 不能让它进入运行时导致 EffectiveSource 变成未定义值。
    /// </summary>
    public static BusinessTimeSource Normalize(this BusinessTimeSource source)
        => Enum.IsDefined(typeof(BusinessTimeSource), source)
            ? source
            : BusinessTimeSource.ClassIslandTime;
}
