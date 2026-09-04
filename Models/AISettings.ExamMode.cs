using ClassIsland.AISmartClass.Models.ExamMode;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace ClassIsland.AISmartClass.Models;

public partial class AISettings
{
    [ObservableProperty]
    [property: JsonPropertyName("examMode")]
    private ExamModeSettings _examMode = new();
}
