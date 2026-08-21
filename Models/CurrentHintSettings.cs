using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.AISmartClass.Models;

/// <summary>当前课程AI提示组件设置</summary>
public partial class CurrentHintSettings : ObservableObject
{
    [ObservableProperty]
    [property: JsonPropertyName("showCurrentHint")]
    private bool _showCurrentHint = true;

    [ObservableProperty]
    [property: JsonPropertyName("fontSize")]
    private double _fontSize = 14;
}
