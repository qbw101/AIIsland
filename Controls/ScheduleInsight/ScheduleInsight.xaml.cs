using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.AISmartClass.Attributes;
using ClassIsland.AISmartClass.Models;
using ClassIsland.AISmartClass.Services;

namespace ClassIsland.AISmartClass.Controls.ScheduleInsight;

[ComponentInfo(
    "11000000-0000-0000-0000-000000000001",
    "AIIsland 课表总结",
    "fluent(\ue161)",
    "一句话解读今日课表"
)]
[AIIslandIcon("\ue005")]
public partial class ScheduleInsight : ComponentBase<ScheduleInsightSettings>
{
    public static readonly DirectProperty<ScheduleInsight, string> SummaryProperty =
        AvaloniaProperty.RegisterDirect<ScheduleInsight, string>(nameof(Summary),
            o => o.Summary, (o, v) => o.Summary = v);
    private string _summary = "加载中...";
    public string Summary { get => _summary; set => SetAndRaise(SummaryProperty, ref _summary, value); }

    public ScheduleInsight()
    {
        DataContext = this;
        InitializeComponent();
    }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnLoaded(RoutedEventArgs e)
    {
        DataContext = this;
        base.OnLoaded(e);
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var ps = Plugin.ProfileService;
            var ai = Plugin.GetAIService();
            if (ps == null || ai == null) { Summary = "服务未就绪"; return; }

            var subjects = ScheduleQueryHelper.GetTodaySubjectNames(ps);
            var result = await ai.SummarizeToday(subjects);
            Summary = string.IsNullOrEmpty(result) ? "今天没有课程安排~" : result;
        }
        catch (Exception ex)
        {
            Logger.Info($"课表总结加载失败: {ex.Message}");
            Summary = "课表分析中...";
        }
    }
}
