using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.AISmartClass.Attributes;
using ClassIsland.AISmartClass.Models;
using ClassIsland.AISmartClass.Services;

namespace ClassIsland.AISmartClass.Controls.ClassCountdown;

[ComponentInfo(
    "11000000-0000-0000-0000-000000000003",
    "课时倒计时",
    "fluent(\ue121)",
    "显示当前课时剩余时间和进度条"
)]
[AIIslandIcon("\ue001")]
public partial class ClassCountdown : ComponentBase<ClassCountdownSettings>
{
    private enum LessonState { OnClass, Breaking, AfterSchool, Unknown }
    private LessonState _state = LessonState.Unknown;

    private CountdownTimeSource TimeSource => Settings?.TimeSource ?? CountdownTimeSource.ClassIslandTime;

    public static readonly DirectProperty<ClassCountdown, double> ProgressPercentProperty =
        AvaloniaProperty.RegisterDirect<ClassCountdown, double>(nameof(ProgressPercent),
            o => o.ProgressPercent, (o, v) => o.ProgressPercent = v);
    private double _progressPercent = 0;
    public double ProgressPercent { get => _progressPercent; set => SetAndRaise(ProgressPercentProperty, ref _progressPercent, value); }

    public static readonly DirectProperty<ClassCountdown, string> CountdownTextProperty =
        AvaloniaProperty.RegisterDirect<ClassCountdown, string>(nameof(CountdownText),
            o => o.CountdownText, (o, v) => o.CountdownText = v);
    private string _countdownText = "等待课程开始...";
    public string CountdownText { get => _countdownText; set => SetAndRaise(CountdownTextProperty, ref _countdownText, value); }

    public ClassCountdown()
    {
        DataContext = this;
        InitializeComponent();
    }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnLoaded(RoutedEventArgs e)
    {
        DataContext = this;
        base.OnLoaded(e);
        var ls = Plugin.LessonsService;
        if (ls != null)
        {
            ls.PostMainTimerTicked += OnTimerTick;
            ls.OnClass += (_, _) => _state = LessonState.OnClass;
            ls.OnBreakingTime += (_, _) => _state = LessonState.Breaking;
            ls.OnAfterSchool += (_, _) => { _state = LessonState.AfterSchool; ProgressPercent = 100; CountdownText = "放学啦"; };
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        var ls = Plugin.LessonsService;
        if (ls != null) ls.PostMainTimerTicked -= OnTimerTick;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        switch (_state)
        {
            case LessonState.AfterSchool:
                ProgressPercent = 100; CountdownText = "放学啦"; break;
            case LessonState.OnClass: UpdateClassCountdown(); break;
            case LessonState.Breaking: UpdateBreakCountdown(); break;
            case LessonState.Unknown: TryInitFromTime(); break;
        }
    }

    private void UpdateClassCountdown()
    {
        if (TimeSource == CountdownTimeSource.ClassIslandTime)
        {
            UpdateClassCountdownFromLessonsService();
            return;
        }

        var ps = Plugin.ProfileService;
        if (ps == null) return;
        var layout = ScheduleQueryHelper.GetCurrentLayoutItem(ps);
        if (layout == null) return;

        var now = TimeSpan.FromTicks(DateTime.Now.TimeOfDay.Ticks);
        var total = (layout.EndTime - layout.StartTime).TotalSeconds;
        if (total <= 0) return;
        var elapsed = (now - layout.StartTime).TotalSeconds;
        ProgressPercent = Math.Clamp(elapsed / total * 100, 0, 100);
        var remaining = (layout.EndTime - now).TotalSeconds;
        if (remaining > 0) { var m = (int)(remaining / 60); var s = (int)(remaining % 60); CountdownText = $"距下课还有 {m:D2}:{s:D2}"; }
        else { CountdownText = "即将下课"; ProgressPercent = 100; }
    }

    private void UpdateClassCountdownFromLessonsService()
    {
        var ls = Plugin.LessonsService;
        if (ls == null) return;

        var layout = ls.CurrentTimeLayoutItem;
        if (layout == null) return;

        var remaining = ls.OnBreakingTimeLeftTime;
        var total = (layout.EndTime - layout.StartTime).TotalSeconds;
        if (total > 0)
        {
            var remainingSeconds = remaining.TotalSeconds;
            ProgressPercent = Math.Clamp((total - remainingSeconds) / total * 100, 0, 100);
        }

        if (remaining > TimeSpan.Zero)
        {
            var m = (int)(remaining.TotalMinutes);
            var s = remaining.Seconds;
            CountdownText = $"距下课还有 {m:D2}:{s:D2}";
        }
        else
        {
            CountdownText = "即将下课";
            ProgressPercent = 100;
        }
    }

    private void UpdateBreakCountdown()
    {
        if (TimeSource == CountdownTimeSource.ClassIslandTime)
        {
            UpdateBreakCountdownFromLessonsService();
            return;
        }

        var ps = Plugin.ProfileService;
        if (ps == null) return;
        var layout = ScheduleQueryHelper.GetCurrentLayoutItem(ps);
        if (layout == null) return;

        var now = TimeSpan.FromTicks(DateTime.Now.TimeOfDay.Ticks);
        if (now > layout.EndTime) { _state = LessonState.OnClass; return; }
        var remaining = (layout.EndTime - now).TotalSeconds;
        var m = (int)(remaining / 60); var s = (int)(remaining % 60);
        CountdownText = $"课间剩余 {m:D2}:{s:D2}"; ProgressPercent = 100;
    }

    private void UpdateBreakCountdownFromLessonsService()
    {
        var ls = Plugin.LessonsService;
        if (ls == null) return;

        var layout = ls.CurrentTimeLayoutItem;
        if (layout == null) return;

        var remaining = ls.OnClassLeftTime;
        if (remaining <= TimeSpan.Zero)
        {
            _state = LessonState.OnClass;
            return;
        }

        var m = (int)(remaining.TotalMinutes);
        var s = remaining.Seconds;
        CountdownText = $"课间剩余 {m:D2}:{s:D2}";
        ProgressPercent = 100;
    }

    private void TryInitFromTime()
    {
        try
        {
            if (TimeSource == CountdownTimeSource.ClassIslandTime)
            {
                TryInitFromLessonsService();
                return;
            }

            var ps = Plugin.ProfileService;
            if (ps?.Profile == null) return;
            var plan = ps.Profile.ClassPlans.Values.FirstOrDefault(p => p.IsActivated && p.IsEnabled);
            if (plan == null) return;

            var now = TimeSpan.FromTicks(DateTime.Now.TimeOfDay.Ticks);
            var cls = ScheduleQueryHelper.GetClassAtTime(plan, now);
            if (cls != null) { _state = LessonState.OnClass; UpdateClassCountdown(); }
            else
            {
                var ends = plan.Classes.Where(c => c.IsEnabled).Select(c => c.CurrentTimeLayoutItem?.EndTime).Where(t => t.HasValue).Select(t => t!.Value).ToList();
                if (ends.Count > 0 && now > ends.Last()) { _state = LessonState.AfterSchool; ProgressPercent = 100; CountdownText = "放学啦"; }
                else if (ends.Any(et => now > et)) { _state = LessonState.Breaking; UpdateBreakCountdown(); }
            }
        }
        catch { }
    }

    private void TryInitFromLessonsService()
    {
        var ls = Plugin.LessonsService;
        if (ls == null) return;

        var remainingClass = ls.OnBreakingTimeLeftTime;
        var remainingBreak = ls.OnClassLeftTime;

        if (remainingClass > TimeSpan.Zero)
        {
            _state = LessonState.OnClass;
            UpdateClassCountdownFromLessonsService();
        }
        else if (remainingBreak > TimeSpan.Zero)
        {
            _state = LessonState.Breaking;
            UpdateBreakCountdownFromLessonsService();
        }
        else if (ls.CurrentTimeLayoutItem == null)
        {
            _state = LessonState.AfterSchool;
            ProgressPercent = 100;
            CountdownText = "放学啦";
        }
    }
}
