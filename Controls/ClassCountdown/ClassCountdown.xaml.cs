using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.AISmartClass.Attributes;
using ClassIsland.AISmartClass.Models;
using ClassIsland.AISmartClass.Services;

namespace ClassIsland.AISmartClass.Controls.ClassCountdown;

[ComponentInfo(
    "11000000-0000-0000-0000-000000000003",
    "课时倒计时",
    "fluent(\ue121)",
    "显示当前课时剩余时间"
)]
[AIIslandIcon("\ue001")]
public partial class ClassCountdown : ComponentBase<ClassCountdownSettings>
{
    public static readonly DirectProperty<ClassCountdown, string> CountdownTextProperty =
        AvaloniaProperty.RegisterDirect<ClassCountdown, string>(nameof(CountdownText),
            o => o.CountdownText, (o, v) => o.CountdownText = v);
    private string _countdownText = "等待课程开始...";
    public string CountdownText { get => _countdownText; set => SetAndRaise(CountdownTextProperty, ref _countdownText, value); }

    private const int MaxSubscribeRetries = 60;
    private static readonly TimeSpan SubscribeRetryInterval = TimeSpan.FromSeconds(1);

    private bool _isLoaded;
    private bool _settingsSubscribed;
    private ILessonsService? _subscribedLessons;
    private DispatcherTimer? _subscribeRetryTimer;
    private int _subscribeRetryCount;

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
        ComponentBindingRefresh.Refresh(this);
        _isLoaded = true;
        SubscribeSettings();
        TrySubscribeTimer();
        if (_subscribedLessons == null) StartSubscribeRetry();
        UpdateFromBusinessClock();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        _isLoaded = false;
        base.OnUnloaded(e);
        StopSubscribeRetry();
        UnsubscribeTimer();
        UnsubscribeSettings();
    }

    // ===== 计时器订阅（宿主服务可能晚于组件就绪） =====

    private void TrySubscribeTimer()
    {
        if (_subscribedLessons != null) return;
        var lessons = Plugin.LessonsService;
        if (lessons == null) return;

        lessons.PostMainTimerTicked += OnTimerTick;
        _subscribedLessons = lessons;
        StopSubscribeRetry();
    }

    private void UnsubscribeTimer()
    {
        // 解绑时使用订阅当时的服务实例，避免宿主重建服务后从错误实例解绑。
        var lessons = _subscribedLessons;
        _subscribedLessons = null;
        if (lessons != null) lessons.PostMainTimerTicked -= OnTimerTick;
    }

    private void StartSubscribeRetry()
    {
        if (_subscribeRetryTimer != null) return;
        _subscribeRetryCount = 0;
        _subscribeRetryTimer = new DispatcherTimer { Interval = SubscribeRetryInterval };
        _subscribeRetryTimer.Tick += OnSubscribeRetryTick;
        _subscribeRetryTimer.Start();
    }

    private void StopSubscribeRetry()
    {
        if (_subscribeRetryTimer == null) return;
        _subscribeRetryTimer.Stop();
        _subscribeRetryTimer.Tick -= OnSubscribeRetryTick;
        _subscribeRetryTimer = null;
    }

    private void OnSubscribeRetryTick(object? sender, EventArgs e)
    {
        if (!_isLoaded)
        {
            StopSubscribeRetry();
            return;
        }

        TrySubscribeTimer();
        if (_subscribedLessons != null || ++_subscribeRetryCount >= MaxSubscribeRetries)
        {
            StopSubscribeRetry();
            if (_subscribedLessons == null)
                Logger.Info("[ClassCountdown] 宿主计时器长时间不可用，倒计时将在下次加载时重试");
        }
    }

    // ===== 设置变更 =====

    private void SubscribeSettings()
    {
        if (_settingsSubscribed) return;
        Plugin.AISettingsChanged += OnSettingsChanged;
        _settingsSubscribed = true;
    }

    private void UnsubscribeSettings()
    {
        if (!_settingsSubscribed) return;
        Plugin.AISettingsChanged -= OnSettingsChanged;
        _settingsSubscribed = false;
    }

    private void OnSettingsChanged(AISettings settings)
    {
        // 切换时间来源后立即刷新；组件已卸载时不再触碰 UI。
        Dispatcher.UIThread.Post(() =>
        {
            if (_isLoaded) UpdateFromBusinessClock();
        });
    }

    // ===== 渲染 =====

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (!_isLoaded) return;
        UpdateFromBusinessClock();
    }

    /// <summary>
    /// 每次回调都用同一个业务时钟重新判断阶段和剩余时间。
    /// 不保存宿主事件锁定的状态，因此切换时钟、时间回拨、跨天和启动未就绪都能自行恢复。
    /// </summary>
    private void UpdateFromBusinessClock()
    {
        try
        {
            var now = BusinessTime.Now;
            var resolution = ScheduleQueryHelper.ResolvePlanForDate(Plugin.ProfileService, now);
            Apply(LessonClock.Evaluate(resolution.Plan, now, resolution.HostAuthoritative));
        }
        catch (Exception ex)
        {
            Logger.Info($"[ClassCountdown] 刷新倒计时失败: {ex.Message}");
        }
    }

    private void Apply(LessonTimeState state)
    {
        switch (state.Phase)
        {
            case LessonPhase.OnClass:
                // 只显示纯数字倒计时，标签已说明含义，避免左侧文字过长挤压版面。
                CountdownText = state.Remaining > TimeSpan.Zero
                    ? LessonClock.FormatCountdown(state.Remaining)
                    : "即将下课";
                break;

            case LessonPhase.Breaking:
                CountdownText = LessonClock.FormatCountdown(state.Remaining);
                break;

            case LessonPhase.AfterSchool:
                CountdownText = "放学啦";
                break;

            case LessonPhase.NoCourses:
                CountdownText = "今天没有课程";
                break;

            case LessonPhase.BeforeFirstClass:
            case LessonPhase.NoPlan:
            case LessonPhase.LayoutNotReady:
            default:
                CountdownText = "等待课程开始...";
                break;
        }
    }
}
