using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.AISmartClass.Models;

/// <summary>课表总结组件设置</summary>
public partial class ScheduleInsightSettings : ObservableObject
{
    [ObservableProperty]
    private bool _showScheduleInsight = true;

    [ObservableProperty]
    [property: JsonPropertyName("fontSize")]
    private double _fontSize = 14;
}
