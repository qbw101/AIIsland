using System.ComponentModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.AISmartClass.Models;

/// <summary>课时倒计时时间来源</summary>
public enum CountdownTimeSource
{
    /// <summary>使用系统时间（Windows 本地时钟）</summary>
    [Description("系统时间")]
    SystemTime,

    /// <summary>使用 ClassIsland 内部时间（包含用户设置的时间偏移）</summary>
    [Description("ClassIsland 时间（带偏移）")]
    ClassIslandTime
}

/// <summary>课时倒计时组件设置</summary>
public partial class ClassCountdownSettings : ObservableObject
{
    [ObservableProperty]
    [property: JsonPropertyName("showCountdown")]
    private bool _showCountdown = true;

    [ObservableProperty]
    [property: JsonPropertyName("fontSize")]
    private double _fontSize = 14;

    [ObservableProperty]
    [property: JsonPropertyName("timeSource")]
    private CountdownTimeSource _timeSource = CountdownTimeSource.ClassIslandTime;
}
