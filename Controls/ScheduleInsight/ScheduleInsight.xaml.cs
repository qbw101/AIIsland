using System.Text;
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

    private CancellationTokenSource? _loadCts;
    private long _loadGeneration;

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
        AIRegenerationService.RegenerateSummaryRequested += OnRegenerateRequested;
        StartLoad();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        AIRegenerationService.RegenerateSummaryRequested -= OnRegenerateRequested;
        Interlocked.Increment(ref _loadGeneration);
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
        base.OnUnloaded(e);
    }

    private void OnRegenerateRequested()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Summary = "生成中...";
            StartLoad();
        });
    }

    private void StartLoad()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var generation = Interlocked.Increment(ref _loadGeneration);
        _ = LoadAsync(generation, _loadCts.Token);
    }

    private async Task LoadAsync(long generation, CancellationToken ct)
    {
        try
        {
            var ai = Plugin.GetAIService();
            if (ai == null) { Summary = "服务未就绪，稍后重试"; return; }

            var schedule = await ScheduleQueryHelper.GetTodaySubjectsWhenReadyAsync(
                () => Plugin.ProfileService, ct: ct);
            if (generation != Interlocked.Read(ref _loadGeneration)) return;

            if (schedule.Readiness == ProfileReadiness.ReadyWithoutCourses)
            {
                Summary = "今天没有课程安排~";
                return;
            }

            if (schedule.Readiness != ProfileReadiness.Ready)
            {
                Summary = "课表加载中，请稍后刷新";
                return;
            }

            Summary = "生成中...";
            await ai.SummarizeTodayStream(schedule.Subjects, snapshot =>
            {
                if (generation != Interlocked.Read(ref _loadGeneration)) return;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (generation == Interlocked.Read(ref _loadGeneration))
                        Summary = snapshot;
                });
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Logger.Info($"课表总结加载失败: {ex.Message}");
            if (generation == Interlocked.Read(ref _loadGeneration))
                Summary = "课表分析失败，请稍后刷新";
        }
    }
}
