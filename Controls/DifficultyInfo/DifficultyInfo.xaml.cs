using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.AISmartClass.Attributes;
using ClassIsland.AISmartClass.Models;
using ClassIsland.AISmartClass.Services;

namespace ClassIsland.AISmartClass.Controls.DifficultyInfo;

[ComponentInfo(
    "11000000-0000-0000-0000-000000000005",
    "难度与番茄钟",
    "fluent(\ue1ce)",
    "显示今日课程难度等级与专注建议"
)]
[AIIslandIcon("\ue002")]
public partial class DifficultyInfo : ComponentBase<DifficultyInfoSettings>
{
    public static readonly DirectProperty<DifficultyInfo, string> DifficultyLabelProperty =
        AvaloniaProperty.RegisterDirect<DifficultyInfo, string>(nameof(DifficultyLabel),
            o => o.DifficultyLabel, (o, v) => o.DifficultyLabel = v);
    private string _difficultyLabel = "3 / 5";
    public string DifficultyLabel { get => _difficultyLabel; set => SetAndRaise(DifficultyLabelProperty, ref _difficultyLabel, value); }

    public static readonly DirectProperty<DifficultyInfo, string> PomodoroSuggestionProperty =
        AvaloniaProperty.RegisterDirect<DifficultyInfo, string>(nameof(PomodoroSuggestion),
            o => o.PomodoroSuggestion, (o, v) => o.PomodoroSuggestion = v);
    private string _pomodoroSuggestion = "25 分钟";
    public string PomodoroSuggestion { get => _pomodoroSuggestion; set => SetAndRaise(PomodoroSuggestionProperty, ref _pomodoroSuggestion, value); }

    public DifficultyInfo()
    {
        DataContext = this;
        InitializeComponent();
    }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnLoaded(RoutedEventArgs e)
    {
        DataContext = this;
        base.OnLoaded(e);
        ComponentBindingRefresh.Refresh(this);
        UpdateInfo();
    }

    private async void UpdateInfo()
    {
        try
        {
            var ai = Plugin.GetAIService();
            if (ai == null) return;

            // 等待 ProfileService / 课表就绪，避免启动早期误判为空课表
            var subjects = await ScheduleQueryHelper.GetTodaySubjectNamesWhenReadyAsync(() => Plugin.ProfileService);
            var diff = ai.EstimateDifficulty(subjects);
            DifficultyLabel = $"{Math.Clamp(diff, 1, 5)} / 5";
            PomodoroSuggestion = subjects.Count switch { > 5 => "25 分钟", >= 4 => "30 分钟", _ => "45 分钟" };
        }
        catch (Exception ex) { Logger.Info($"难度信息失败: {ex.Message}"); }
    }
}
