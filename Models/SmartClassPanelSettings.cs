using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.AISmartClass.Models;

/// <summary>
/// 主界面面板组件的显示选项。
/// 在组件设置中由用户控制显示哪些模块。
/// </summary>
public partial class SmartClassPanelSettings : ObservableObject
{
    [ObservableProperty]
    [property: JsonPropertyName("showTodayOverview")]
    private bool _showTodayOverview = true;
    // 是否显示今日课表一句话总结

    [ObservableProperty]
    [property: JsonPropertyName("showHomeworkEstimate")]
    private bool _showHomeworkEstimate = true;
    // 是否显示今日作业量估算

    [ObservableProperty]
    [property: JsonPropertyName("showCountdown")]
    private bool _showCountdown = true;
    // 是否显示当前课时倒计时 + 进度条

    [ObservableProperty]
    [property: JsonPropertyName("showCurrentHint")]
    private bool _showCurrentHint = true;
    // 是否显示当前课程 AI 提示

    [ObservableProperty]
    [property: JsonPropertyName("showDifficulty")]
    private bool _showDifficulty = true;
    // 是否显示难度星数预估

    [ObservableProperty]
    [property: JsonPropertyName("showPomodoro")]
    private bool _showPomodoro = true;
    // 是否显示番茄钟建议

    [ObservableProperty]
    [property: JsonPropertyName("panelMaxHeight")]
    private int _panelMaxHeight = 220;
    // 面板最大高度（像素），防止占用主界面过多空间
}
