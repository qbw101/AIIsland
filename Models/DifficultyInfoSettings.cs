using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.AISmartClass.Models;

/// <summary>难度/番茄钟信息组件设置</summary>
public partial class DifficultyInfoSettings : ObservableObject
{
    [ObservableProperty]
    [property: JsonPropertyName("showDifficulty")]
    private bool _showDifficulty = true;

    [ObservableProperty]
    [property: JsonPropertyName("showPomodoro")]
    private bool _showPomodoro = true;

    [ObservableProperty]
    [property: JsonPropertyName("fontSize")]
    private double _fontSize = 14;
}
