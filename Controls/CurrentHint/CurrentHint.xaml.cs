using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.AISmartClass.Attributes;
using ClassIsland.AISmartClass.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.AISmartClass.Models;

namespace ClassIsland.AISmartClass.Controls.CurrentHint;

[ComponentInfo(
    "11000000-0000-0000-0000-000000000004",
    "AIIsland 课程提示",
    "fluent(\uea80)",
    "上课时自动生成当前课程学习提示"
)]
[AIIslandIcon("\ue003")]
public partial class CurrentHint : ComponentBase<CurrentHintSettings>
{
    public static readonly DirectProperty<CurrentHint, string> HintProperty =
        AvaloniaProperty.RegisterDirect<CurrentHint, string>(nameof(Hint),
            o => o.Hint, (o, v) => o.Hint = v);
    private string _hint = "等待课程开始...";
    public string Hint { get => _hint; set => SetAndRaise(HintProperty, ref _hint, value); }

    private bool _loaded;
    private string _loadedSubject = "";
    private DateOnly _loadedDate;
    private bool _lessonsSubscribed;
    private CancellationTokenSource? _refreshCts;
    private long _refreshGeneration;

    public CurrentHint()
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
        AIRegenerationService.RegenerateHintRequested += OnRegenerateRequested;
        StartRefresh(force: false);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        AIRegenerationService.RegenerateHintRequested -= OnRegenerateRequested;
        Interlocked.Increment(ref _refreshGeneration);
        var ls = Plugin.LessonsService;
        if (_lessonsSubscribed && ls != null)
            ls.OnClass -= OnClassHandler;
        _lessonsSubscribed = false;
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = null;
        base.OnUnloaded(e);
    }

    private void OnRegenerateRequested()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _loaded = false;
            StartRefresh(force: true);
        });
    }

    private void TrySubscribeLessonsService()
    {
        if (_lessonsSubscribed) return;
        var ls = Plugin.LessonsService;
        if (ls == null) return;
        ls.OnClass += OnClassHandler;
        _lessonsSubscribed = true;
    }

    private void OnClassHandler(object? sender, EventArgs e)
    {
        _loaded = false;
        StartRefresh(force: true);
    }

    private void StartRefresh(bool force)
    {
        TrySubscribeLessonsService();
        if (!force && _loaded && _loadedDate == DateOnly.FromDateTime(DateTime.Now)) return;

        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = new CancellationTokenSource();
        var generation = Interlocked.Increment(ref _refreshGeneration);
        _ = RefreshAsync(generation, _refreshCts.Token);
    }

    private async Task RefreshAsync(long generation, CancellationToken ct)
    {
        try
        {
            var ai = Plugin.GetAIService();
            if (ai == null)
            {
                Hint = "AI 服务加载中，请稍后刷新";
                return;
            }

            var schedule = await ScheduleQueryHelper.GetTodaySubjectsWhenReadyAsync(
                () => Plugin.ProfileService, ct: ct);
            TrySubscribeLessonsService();
            if (generation != Interlocked.Read(ref _refreshGeneration)) return;

            if (schedule.Readiness is not (ProfileReadiness.Ready or ProfileReadiness.ReadyWithoutCourses))
            {
                Hint = "课表加载中，请稍后刷新";
                return;
            }

            var ps = Plugin.ProfileService;
            LearningHintContext context;
            if (schedule.Readiness == ProfileReadiness.ReadyWithoutCourses || ps == null)
            {
                context = new LearningHintContext("今天没有课程", "自主学习", "no-courses");
            }
            else
            {
                context = ScheduleQueryHelper.GetLearningHintContext(ps, schedule.Subjects);
            }

            if (_loaded && _loadedDate == DateOnly.FromDateTime(DateTime.Now) &&
                string.Equals(_loadedSubject, context.CacheKey, StringComparison.Ordinal))
                return;

            Logger.Info($"[CurrentHint] 当前状态: '{context.Scene}', 学习重点: '{context.Focus}'");
            Hint = "生成中...";

            var result = await ai.GenerateLearningHintStream(context.Scene, context.Focus, snapshot =>
            {
                if (generation != Interlocked.Read(ref _refreshGeneration)) return;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (generation == Interlocked.Read(ref _refreshGeneration) &&
                        !string.IsNullOrWhiteSpace(snapshot))
                    {
                        Hint = snapshot;
                    }
                });
            }, ct);

            if (generation != Interlocked.Read(ref _refreshGeneration)) return;
            _loaded = !string.IsNullOrWhiteSpace(result) && !ai.IsFallbackResult(result);
            if (_loaded)
            {
                _loadedSubject = context.CacheKey;
                _loadedDate = DateOnly.FromDateTime(DateTime.Now);
            }
            else if (string.IsNullOrWhiteSpace(Hint))
            {
                Hint = "课程提示暂不可用，请稍后刷新";
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Logger.Info($"课程提示失败: {ex.Message}");
            if (generation == Interlocked.Read(ref _refreshGeneration))
                Hint = "课程提示生成失败，请稍后刷新";
        }
    }

}
