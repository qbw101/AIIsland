using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.AISmartClass.Models;

/// <summary>课时倒计时组件设置</summary>
public partial class ClassCountdownSettings : ObservableObject
{
    [ObservableProperty]
    private bool _showCountdown = true;

    [ObservableProperty]
    [property: JsonPropertyName("fontSize")]
    private double _fontSize = 14;
}
