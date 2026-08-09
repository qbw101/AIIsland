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

    private CancellationTokenSource? _loadCts;
    private long _loadGeneration;
    private DateOnly _observedDate = DateOnly.FromDateTime(DateTime.Now);
    private bool _lessonsSubscribed;

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
        TrySubscribeLessonsService();
        StartLoad();

        // 订阅托盘菜单手动重新生成事件
        AIRegenerationService.RegenerateHomeworkEstimateRequested += OnRegenerateRequested;
    }

    private void OnRegenerateRequested()
    {
        Logger.Info("[TrayMenu] 手动重新生成作业量估算");
        Plugin.GetAIService()?.ClearCache();
        StartLoad();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        AIRegenerationService.RegenerateHomeworkEstimateRequested -= OnRegenerateRequested;
        var lessons = Plugin.LessonsService;
        if (_lessonsSubscribed && lessons != null)
            lessons.PostMainTimerTicked -= OnTimerTicked;
        _lessonsSubscribed = false;
        Interlocked.Increment(ref _loadGeneration);
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
    }

    private void TrySubscribeLessonsService()
    {
        if (_lessonsSubscribed || Plugin.LessonsService == null) return;
        Plugin.LessonsService.PostMainTimerTicked += OnTimerTicked;
        _lessonsSubscribed = true;
    }

    private void OnTimerTicked(object? sender, EventArgs e)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_observedDate == today) return;

        _observedDate = today;
        Estimate = "加载今日课表...";
        StartLoad();
    }

    private void StartLoad()
    {
        TrySubscribeLessonsService();
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
            if (ai == null) { Estimate = "服务未就绪"; return; }

            // 等待 ProfileService / 课表就绪，避免启动早期误判为空课表
            var subjects = await ScheduleQueryHelper.GetTodaySubjectNamesWhenReadyAsync(
                () => Plugin.ProfileService, ct: ct);
            TrySubscribeLessonsService();
            if (generation != Interlocked.Read(ref _loadGeneration)) return;
            Estimate = "生成中...";
            await ai.EstimateHomeworkLoadStream(subjects, snapshot =>
            {
                if (generation != Interlocked.Read(ref _loadGeneration)) return;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (generation == Interlocked.Read(ref _loadGeneration))
                        Estimate = snapshot;
                });
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Logger.Info($"作业量估算失败: {ex.Message}");
            if (generation == Interlocked.Read(ref _loadGeneration))
                Estimate = "分析中...";
        }
    }
}
