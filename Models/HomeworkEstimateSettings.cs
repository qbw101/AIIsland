using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.AISmartClass.Models;

/// <summary>作业量估算组件设置</summary>
public partial class HomeworkEstimateSettings : ObservableObject
{
    [ObservableProperty]
    private bool _showHomeworkEstimate = true;

    [ObservableProperty]
    [property: JsonPropertyName("fontSize")]
    private double _fontSize = 14;
}
