using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared.Models.Profile;
using ClassIsland.AISmartClass.Models;
using ClassIsland.AISmartClass.Services;

namespace ClassIsland.AISmartClass.Controls.SmartClassPanel;

[ComponentInfo(
    "A1B2C3D4-E5F6-7890-ABCD-EF1234567890",
    "AIIsland 智能面板",
    "显示课表总结、倒计时和学习建议"
)]
public partial class SmartClassPanel : ComponentBase<Models.SmartClassPanelSettings>
{
    private readonly ILessonsService _lessons = null!;
    private readonly IProfileService _profileService = null!;
    private readonly AIChatService _ai = null!;

    // ===== 内部状态（ILessonsService 仅事件，通过事件追踪状态） =====

    private enum LessonState { BeforeClass, OnClass, Breaking, AfterSchool, Unknown }
    private LessonState _state = LessonState.Unknown;

    // ===== Avalonia DirectProperty 绑定属性 =====

    public static readonly DirectProperty<SmartClassPanel, string> TodaySummaryProperty =
        AvaloniaProperty.RegisterDirect<SmartClassPanel, string>(nameof(TodaySummary),
            o => o.TodaySummary, (o, v) => o.TodaySummary = v);

    private string _todaySummary = "加载中...";
    public string TodaySummary { get => _todaySummary; set => SetAndRaise(TodaySummaryProperty, ref _todaySummary, value); }

    public static readonly DirectProperty<SmartClassPanel, string> CurrentHintProperty =
        AvaloniaProperty.RegisterDirect<SmartClassPanel, string>(nameof(CurrentHint),
            o => o.CurrentHint, (o, v) => o.CurrentHint = v);

    private string _currentHint = "";
    public string CurrentHint { get => _currentHint; set => SetAndRaise(CurrentHintProperty, ref _currentHint, value); }

    public static readonly DirectProperty<SmartClassPanel, double> ProgressPercentProperty =
        AvaloniaProperty.RegisterDirect<SmartClassPanel, double>(nameof(ProgressPercent),
            o => o.ProgressPercent, (o, v) => o.ProgressPercent = v);

    private double _progressPercent = 0;
    public double ProgressPercent { get => _progressPercent; set => SetAndRaise(ProgressPercentProperty, ref _progressPercent, value); }

    public static readonly DirectProperty<SmartClassPanel, string> CountdownTextProperty =
        AvaloniaProperty.RegisterDirect<SmartClassPanel, string>(nameof(CountdownText),
            o => o.CountdownText, (o, v) => o.CountdownText = v);

    private string _countdownText = "";
    public string CountdownText { get => _countdownText; set => SetAndRaise(CountdownTextProperty, ref _countdownText, value); }

    public static readonly DirectProperty<SmartClassPanel, string> DifficultyStarsProperty =
        AvaloniaProperty.RegisterDirect<SmartClassPanel, string>(nameof(DifficultyStars),
            o => o.DifficultyStars, (o, v) => o.DifficultyStars = v);

    private string _difficultyStars = "⭐⭐⭐";
    public string DifficultyStars { get => _difficultyStars; set => SetAndRaise(DifficultyStarsProperty, ref _difficultyStars, value); }

    public static readonly DirectProperty<SmartClassPanel, string> PomodoroSuggestionProperty =
        AvaloniaProperty.RegisterDirect<SmartClassPanel, string>(nameof(PomodoroSuggestion),
            o => o.PomodoroSuggestion, (o, v) => o.PomodoroSuggestion = v);

    private string _pomodoroSuggestion = "25min";
    public string PomodoroSuggestion { get => _pomodoroSuggestion; set => SetAndRaise(PomodoroSuggestionProperty, ref _pomodoroSuggestion, value); }

    // ===== AI 状态指示器 =====

    public static readonly DirectProperty<SmartClassPanel, string> AIStatusTextProperty =
        AvaloniaProperty.RegisterDirect<SmartClassPanel, string>(nameof(AIStatusText),
            o => o.AIStatusText, (o, v) => o.AIStatusText = v);

    private string _aiStatusText = "";
    public string AIStatusText { get => _aiStatusText; set => SetAndRaise(AIStatusTextProperty, ref _aiStatusText, value); }

    public static readonly DirectProperty<SmartClassPanel, bool> IsAIThinkingProperty =
        AvaloniaProperty.RegisterDirect<SmartClassPanel, bool>(nameof(IsAIThinking),
            o => o.IsAIThinking, (o, v) => o.IsAIThinking = v);

    private bool _isAIThinking = false;
    public bool IsAIThinking { get => _isAIThinking; set => SetAndRaise(IsAIThinkingProperty, ref _isAIThinking, value); }

    public static readonly DirectProperty<SmartClassPanel, bool> ShowExtrasProperty =
        AvaloniaProperty.RegisterDirect<SmartClassPanel, bool>(nameof(ShowExtras),
            o => o.ShowExtras, (o, v) => o.ShowExtras = v);

    private bool _showExtras = true;
    public bool ShowExtras { get => _showExtras; set => SetAndRaise(ShowExtrasProperty, ref _showExtras, value); }

    // ===== 作业量估算 =====

    public static readonly DirectProperty<SmartClassPanel, string> HomeworkEstimateProperty =
        AvaloniaProperty.RegisterDirect<SmartClassPanel, string>(nameof(HomeworkEstimate),
            o => o.HomeworkEstimate, (o, v) => o.HomeworkEstimate = v);

    private string _homeworkEstimate = "";
    public string HomeworkEstimate { get => _homeworkEstimate; set => SetAndRaise(HomeworkEstimateProperty, ref _homeworkEstimate, value); }

    private bool _todayOverviewLoaded = false;
    private bool _currentHintLoaded = false;
    private CancellationTokenSource? _loadingCts;

    // ===== 构造函数 =====

    public SmartClassPanel() => InitializeComponent();

    public SmartClassPanel(IProfileService profileService, ILessonsService lessons, AIChatService ai) : this()
    {
        _profileService = profileService;
        _lessons = lessons;
        _ai = ai;
    }

    // ===== 生命周期 =====

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _lessons.PostMainTimerTicked += OnTimerTick;
        _lessons.OnClass += OnOnClass;
        _lessons.OnBreakingTime += OnOnBreakingTime;
        _lessons.OnAfterSchool += OnOnAfterSchool;
        _lessons.CurrentTimeStateChanged += OnTimeStateChanged;

        _ = InitializeAsync();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        _lessons.PostMainTimerTicked -= OnTimerTick;
        _lessons.OnClass -= OnOnClass;
        _lessons.OnBreakingTime -= OnOnBreakingTime;
        _lessons.OnAfterSchool -= OnOnAfterSchool;
        _lessons.CurrentTimeStateChanged -= OnTimeStateChanged;

        _loadingCts?.Cancel();
        _loadingCts = null;
    }

    // ===== 事件处理 =====

    private void OnOnClass(object? sender, EventArgs e)
    {
        _state = LessonState.OnClass;
        ShowExtras = true;
        _currentHintLoaded = false;
        SetAIStatus(true, "AI 思考中...");
        _ = RefreshHintAsync().ContinueWith(_ =>
        {
            SetAIStatus(false, "AI 就绪");
            _ = ClearAIStatusAfterDelay(3000);
        }, TaskScheduler.Default);
    }

    private void OnOnBreakingTime(object? sender, EventArgs e)
    {
        _state = LessonState.Breaking;
        ShowExtras = true;
    }

    private void OnOnAfterSchool(object? sender, EventArgs e)
    {
        _state = LessonState.AfterSchool;
        ShowExtras = false;
        ProgressPercent = 100;
        CountdownText = "放学啦 🎉";
    }

    private void OnTimeStateChanged(object? sender, EventArgs e) { }

    // ===== 异步初始化 =====

    private async Task InitializeAsync()
    {
        _loadingCts = new CancellationTokenSource();
        var ct = _loadingCts.Token;
        try
        {
            SetAIStatus(true, "AI 分析课表中...");

            await Task.WhenAll(LoadTodaySummary(ct), RefreshHintAsync(ct), LoadHomeworkEstimate(ct));
            UpdateDifficultyAndPomodoro();

            SetAIStatus(false, "AI 就绪");
            _ = ClearAIStatusAfterDelay(3000);
        }
        catch (OperationCanceledException) { SetAIStatus(false, ""); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SmartClassPanel 初始化失败: {ex.Message}");
            SetAIStatus(false, "AI 离线");
        }
    }

    /// <summary>设置 AI 状态指示器</summary>
    private void SetAIStatus(bool thinking, string text)
    {
        IsAIThinking = thinking;
        AIStatusText = text;
    }

    /// <summary>延迟清除 AI 状态文本</summary>
    private async Task ClearAIStatusAfterDelay(int ms)
    {
        await Task.Delay(ms);
        AIStatusText = "";
    }

    private async Task LoadTodaySummary(CancellationToken ct)
    {
        if (!Settings.ShowTodayOverview || _todayOverviewLoaded) return;
        var subjects = GetTodaySubjectNames();
        var result = await _ai.SummarizeToday(subjects, ct);
        TodaySummary = result;
        // 只有真正拿到 AI 结果（非降级句子）才标记为已加载，允许失败后重试
        _todayOverviewLoaded = !IsFallbackResult(result);
    }

    private async Task LoadHomeworkEstimate(CancellationToken ct)
    {
        if (!Settings.ShowHomeworkEstimate) return;
        var subjects = GetTodaySubjectNames();
        var result = await _ai.EstimateHomeworkLoad(subjects, ct);
        HomeworkEstimate = result;
    }

    private async Task RefreshHintAsync(CancellationToken ct = default)
    {
        if (!Settings.ShowCurrentHint || _currentHintLoaded) return;
        var current = GetCurrentSubjectName();
        var userMsg = string.IsNullOrEmpty(current) ? "请给一句15字以内的学习提示。" : $"当前课程：{current}\n请给一句15字以内的学习提示。";
        var result = await _ai.ChatAsync("你是一个学习助手，给高中生当前课程的简短提示。", userMsg, ct: ct);
        CurrentHint = result;
        _currentHintLoaded = !IsFallbackResult(result);
    }

    /// <summary>判断结果是否为降级句子（AI 不可用时的回退文本）</summary>
    private static bool IsFallbackResult(string result)
    {
        if (string.IsNullOrEmpty(result)) return true;
        // 降级句子特征：包含特定关键词
        return result.Contains("请在设置中配置") || result.Contains("AI 暂时不可用");
    }

    private void UpdateDifficultyAndPomodoro()
    {
        var subjects = GetTodaySubjectNames();
        var diff = _ai.EstimateDifficulty(subjects);
        DifficultyStars = new string('⭐', Math.Clamp(diff, 1, 5));
        PomodoroSuggestion = subjects.Count switch { > 5 => "建议 25min", >= 4 => "建议 30min", _ => "建议 45min" };
    }

    // ===== 实时事件 =====

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (!Settings.ShowCountdown) return;
        switch (_state)
        {
            case LessonState.AfterSchool:
                ProgressPercent = 100;
                CountdownText = "放学啦 🎉";
                break;
            case LessonState.OnClass:
                UpdateClassCountdown();
                break;
            case LessonState.Breaking:
                UpdateBreakCountdown();
                break;
            case LessonState.BeforeClass:
                // 课前等待阶段，无需更新
                break;
            case LessonState.Unknown:
                // 未知状态时尝试根据当前时间推断
                TryInitStateFromCurrentTime();
                break;
        }
    }

    /// <summary>上课中：显示剩余时间 + 进度条</summary>
    private void UpdateClassCountdown()
    {
        var layout = Services.ScheduleQueryHelper.GetCurrentLayoutItem(_profileService);
        if (layout == null) { return; }

        var now = TimeSpan.FromTicks(DateTime.Now.TimeOfDay.Ticks);
        var start = layout.StartTime;
        var end = layout.EndTime;
        var total = (end - start).TotalSeconds;
        var elapsed = (now - start).TotalSeconds;

        if (total <= 0) return;

        ProgressPercent = Math.Clamp(elapsed / total * 100, 0, 100);

        var remaining = (end - now).TotalSeconds;
        if (remaining > 0)
        {
            var minutes = (int)(remaining / 60);
            var seconds = (int)(remaining % 60);
            CountdownText = $"距下课还有 {minutes:D2}:{seconds:D2}";
        }
        else
        {
            CountdownText = "即将下课";
            ProgressPercent = 100;
        }
    }

    /// <summary>课间：显示下节课倒计时</summary>
    private void UpdateBreakCountdown()
    {
        var layout = Services.ScheduleQueryHelper.GetCurrentLayoutItem(_profileService);
        if (layout == null) { return; }

        var now = TimeSpan.FromTicks(DateTime.Now.TimeOfDay.Ticks);
        var end = layout.EndTime;

        // 如果当前课间已经结束（时间已过），回到上课状态等待
        if (now > end)
        {
            _state = LessonState.OnClass;
            return;
        }

        var remaining = (end - now).TotalSeconds;
        var minutes = (int)(remaining / 60);
        var seconds = (int)(remaining % 60);
        CountdownText = $"课间剩余 {minutes:D2}:{seconds:D2}";
        ProgressPercent = 100; // 课间阶段进度条满
    }

    /// <summary>
    /// Unknown 状态下，根据当前时间推断当前所处的课段。
    /// 用于组件加载后尚未收到事件时的快速初始化。
    /// </summary>
    private void TryInitStateFromCurrentTime()
    {
        try
        {
            var profile = _profileService.Profile;
            if (profile == null) return;

            var activePlan = profile.ClassPlans.Values.FirstOrDefault(p => p.IsActivated && p.IsEnabled);
            if (activePlan == null) return;

            var now = TimeSpan.FromTicks(DateTime.Now.TimeOfDay.Ticks);
            var classInfo = Services.ScheduleQueryHelper.GetClassAtTime(activePlan, now);

            if (classInfo != null)
            {
                _state = LessonState.OnClass;
                UpdateClassCountdown();
            }
            else
            {
                // 检查是否在任何课间
                var allLayouts = activePlan.Classes
                    .Where(c => c.IsEnabled)
                    .Select(c => c.CurrentTimeLayoutItem)
                    .Where(tl => tl != null)
                    .ToList();

                var inBreak = allLayouts.Any(tl => now > tl.EndTime);
                if (inBreak)
                {
                    _state = LessonState.Breaking;
                    UpdateBreakCountdown();
                }
                else if (allLayouts.Count > 0 && now > allLayouts.Last()!.EndTime)
                {
                    _state = LessonState.AfterSchool;
                    ProgressPercent = 100;
                    CountdownText = "放学啦 🎉";
                }
            }
        }
        catch { /* 保持 Unknown，等待事件更新 */ }
    }

    // ===== 辅助方法 =====

    private List<string> GetTodaySubjectNames()
    {
        try
        {
            var profile = _profileService.Profile;
            if (profile == null) return new List<string>();

            var activePlan = profile.ClassPlans.Values.FirstOrDefault(p => p.IsActivated && p.IsEnabled);
            if (activePlan == null) return new List<string>();

            var subjectNames = new List<string>();
            foreach (var cls in activePlan.Classes.Where(c => c.IsEnabled))
            {
                if (profile.Subjects.TryGetValue(cls.SubjectId, out var subject) && !string.IsNullOrEmpty(subject.Name))
                    subjectNames.Add(subject.Name);
            }
            return subjectNames.Distinct().ToList();
        }
        catch { return new List<string>(); }
    }

    private string GetCurrentSubjectName()
    {
        try
        {
            var profile = _profileService.Profile;
            if (profile == null) return "";
            var activePlan = profile.ClassPlans.Values.FirstOrDefault(p => p.IsActivated && p.IsEnabled);
            if (activePlan == null) return "";

            var now = TimeSpan.FromTicks(DateTime.Now.TimeOfDay.Ticks);
            var currentClass = Services.ScheduleQueryHelper.GetClassAtTime(activePlan, now);

            if (currentClass != null)
                return Services.ScheduleQueryHelper.GetSubjectName(_profileService, currentClass.SubjectId);

            // 如果不在上课区间，取下一节课名称
            var nextClass = Services.ScheduleQueryHelper.GetNextClass(activePlan, now);
            if (nextClass != null)
                return Services.ScheduleQueryHelper.GetSubjectName(_profileService, nextClass.SubjectId);
        }
        catch { }
        return "";
    }
}
