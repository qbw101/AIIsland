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
    "AIIsland 贴心提示",
    "fluent(\uea80)",
    "上课时生成当前课程提示，下课时拼接天气/新闻/生日等贴心关怀"
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
    private DateOnly _observedDate = BusinessTime.Today;
    private bool _lessonsSubscribed;
    private CancellationTokenSource? _refreshCts;
    private long _refreshGeneration;
    private bool _preferNextClass;
    private LessonPhase _lastPhase;
    private string _lastIdentity = "";
    private bool _phaseInitialized;

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
        ComponentBindingRefresh.Refresh(this);
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
        {
            ls.PostMainTimerTicked -= OnTimerTicked;
        }
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
        // 状态变化改为在计时器回调中按业务时钟推导：
        // 宿主 OnClass/OnBreakingTime/OnAfterSchool 在时间偏移时按真实时钟触发，会刷新错误科目。
        ls.PostMainTimerTicked += OnTimerTicked;
        _lessonsSubscribed = true;
        SeedPhaseFromBusinessClock();
    }

    /// <summary>首次订阅时用当前业务阶段建立基线，避免启动瞬间误判课间。</summary>
    private void SeedPhaseFromBusinessClock()
    {
        try
        {
            var snapshot = LessonSnapshotService.Capture();
            _lastPhase = snapshot.Phase;
            _lastIdentity = LessonIdentity.ForHint(snapshot);
            _phaseInitialized = true;
            _preferNextClass = snapshot.IsBreaking;
        }
        catch (Exception ex)
        {
            Logger.Info($"[CurrentHint] 初始化课时阶段失败: {ex.Message}");
        }
    }

    private void OnTimerTicked(object? sender, EventArgs e)
    {
        var today = BusinessTime.Today;
        var snapshot = LessonSnapshotService.Capture();
        var phase = snapshot.Phase;
        var identity = LessonIdentity.ForHint(snapshot);

        var dateChanged = _observedDate != today;
        if (dateChanged) _observedDate = today;

        if (!_phaseInitialized)
        {
            _phaseInitialized = true;
            _lastPhase = phase;
            _lastIdentity = identity;
            _preferNextClass = snapshot.IsBreaking;
            if (!dateChanged) return;
        }
        else
        {
            var phaseChanged = phase != _lastPhase;
            // 同一阶段内课程可能变化（切换时间来源、临时换课），必须按内容身份再比较一次。
            var identityChanged = !string.Equals(_lastIdentity, identity, StringComparison.Ordinal);
            if (!dateChanged && !phaseChanged && !identityChanged) return;

            _lastPhase = phase;
            _lastIdentity = identity;
        }

        _preferNextClass = snapshot.IsBreaking;
        _loaded = false;
        StartRefresh(force: true);
    }

    private void StartRefresh(bool force)
    {
        TrySubscribeLessonsService();
        if (!force && _loaded && _loadedDate == BusinessTime.Today) return;

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
                context = ScheduleQueryHelper.GetLearningHintContext(
                    ps, schedule.Subjects, _preferNextClass);
            }

            if (_loaded && _loadedDate == BusinessTime.Today &&
                string.Equals(_loadedSubject, context.CacheKey, StringComparison.Ordinal))
                return;

            Logger.Info($"[CurrentHint] 当前状态: '{context.Scene}', 学习重点: '{context.Focus}'");
            Hint = "生成中...";

            // 第一段：课程提示（上课/下课都先生成）
            var courseHint = await ai.GenerateLearningHintStream(context.Scene, context.Focus, snapshot =>
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

            // 第二段：非上课时，拼接贴心提示（链式生成：把第一段结果作为第二段输入）
            if (!string.Equals(context.Scene, "正在上课", StringComparison.Ordinal))
            {
                var notifier = Plugin.SmartClassNotifierInstance;
                if (notifier != null)
                {
                    var changes = await notifier.GetThoughtfulHintChangesAsync(ct);
                    if (generation != Interlocked.Read(ref _refreshGeneration)) return;

                    if (changes.Count > 0)
                    {
                        var thoughtful = await ai.GenerateThoughtfulHintAsync(
                            context.Scene, courseHint, changes, snapshot =>
                            {
                                if (generation != Interlocked.Read(ref _refreshGeneration)) return;
                                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                {
                                    if (generation == Interlocked.Read(ref _refreshGeneration))
                                        Hint = $"{courseHint}｜{snapshot}";
                                });
                            }, ct);

                        if (generation != Interlocked.Read(ref _refreshGeneration)) return;
                        if (!string.IsNullOrWhiteSpace(thoughtful))
                            Hint = $"{courseHint}｜{thoughtful}";
                    }
                }
            }

            _loaded = !string.IsNullOrWhiteSpace(courseHint) && !ai.IsFallbackResult(courseHint);
            if (_loaded)
            {
                _loadedSubject = context.CacheKey;
                _loadedDate = BusinessTime.Today;
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
