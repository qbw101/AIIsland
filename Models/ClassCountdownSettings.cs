using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.AISmartClass.Models;

/// <summary>
/// 课时倒计时组件设置。
/// 时间来源已迁移到插件全局设置（<see cref="AISettings.TimeSource"/>），
/// 组件不再保存或覆盖时间来源；旧配置中的 <c>timeSource</c> 字段在反序列化时被忽略。
/// </summary>
public partial class ClassCountdownSettings : ObservableObject
{
    [ObservableProperty]
    [property: JsonPropertyName("showCountdown")]
    private bool _showCountdown = true;

    [ObservableProperty]
    [property: JsonPropertyName("fontSize")]
    private double _fontSize = 14;
}
