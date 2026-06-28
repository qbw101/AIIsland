using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.AISmartClass.Attributes;
using ClassIsland.AISmartClass.Models;
using ClassIsland.AISmartClass.Services;

namespace ClassIsland.AISmartClass.Controls.HomeworkEstimate;

[ComponentInfo(
    "11000000-0000-0000-0000-000000000002",
    "AIIsland 作业量估算",
    "fluent(\ue12f)",
    "根据科目类型估算今日作业量"
)]
[AIIslandIcon("\ue004")]
public partial class HomeworkEstimate : ComponentBase<HomeworkEstimateSettings>
{
    public static readonly DirectProperty<HomeworkEstimate, string> EstimateProperty =
        AvaloniaProperty.RegisterDirect<HomeworkEstimate, string>(nameof(Estimate),
            o => o.Estimate, (o, v) => o.Estimate = v);
    private string _estimate = "等待分析...";
    public string Estimate { get => _estimate; set => SetAndRaise(EstimateProperty, ref _estimate, value); }

    public HomeworkEstimate()
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
            if (ps == null || ai == null) { Estimate = "服务未就绪"; return; }

            var subjects = ScheduleQueryHelper.GetTodaySubjectNames(ps);
            var result = await ai.EstimateHomeworkLoad(subjects);
            Estimate = result;
        }
        catch (Exception ex)
        {
            Logger.Info($"作业量估算失败: {ex.Message}");
            Estimate = "分析中...";
        }
    }
}
