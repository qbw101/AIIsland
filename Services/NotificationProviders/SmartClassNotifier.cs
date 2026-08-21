using System.Diagnostics;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Abstractions.Services.NotificationProviders;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Notification;
using ClassIsland.Shared.Enums;
using ClassIsland.Shared.Models.Profile;
using ClassIsland.AISmartClass.Models;

namespace ClassIsland.AISmartClass.Services.NotificationProviders;

public enum ThoughtfulScene
{
    BeforeSchool,
    DailyBriefing,
    BreakStart,
    AfterSchool
}

[NotificationProviderInfo(
    "8F3A2B1C-9D4E-4A5F-B6C7-1E2F3A4B5C6D",
    "AIIsland 贴心提醒",
    "智能每日简报汇总天气、课程、自定义提醒、节假日和新闻；课间及放学保留原有贴心提醒"
)]
public class SmartClassNotifier : NotificationProviderBase<SmartClassNotifierSettings>
{
    private readonly ILessonsService _lessons;
    private readonly IProfileService _profileService;
    private readonly AIChatService _ai;
    private readonly WindowsSystemContextService _systemContext;
    private readonly LocationService _locationService;
    private readonly DailyBriefingDataService _dailyBriefingData = new();

    private readonly Timer _customTimer;
    private readonly Timer _musicTimer;
    private int _customReminderChecking;
    private int _musicChecking;
    private string? _lastMusicKey;

    /// <summary>已触发的课前提醒 key 集合，每个课间独立去重</summary>
    private readonly HashSet<string> _triggeredBeforeClassKeys = new();
    private readonly HashSet<string> _triggeredBeforeSchoolKeys = new();
    private DateTime _dedupResetDate = DateTime.MinValue;

    public SmartClassNotifier(IProfileService profileService, ILessonsService lessonsService, AIChatService aiService)
    {
        _profileService = profileService;
        _lessons = lessonsService;
        _ai = aiService;
        _systemContext = new WindowsSystemContextService();
        _locationService = new LocationService(
            () => Settings?.ClassIslandInstallDirectory ?? "");

        // 将核心服务暴露为全局静态引用，供独立组件使用
        Plugin.ProfileService = profileService;
        Plugin.LessonsService = lessonsService;

        // 替换基类默认创建的 FluentIcon 为自定义字体图标（bell_badge_gearshape）
        IconElement = SettingsPageIconPatcher.CreateNotifierIcon();

        _lessons.OnBreakingTime += OnBreakingTimeHandler;
        _lessons.OnAfterSchool += OnAfterSchoolHandler;
        _lessons.OnClass += OnClassHandler;
        _lessons.PostMainTimerTicked += OnTimerTickHandler;  // 轮询兜底：主动检测课间状态

        // 自定义提醒需要独立轮询：固定时间/每日重复/科目课前 N 分钟都依赖当前时钟。
        _customTimer = new Timer(CheckCustomReminders, null,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        _musicTimer = new Timer(CheckMusicReminder, null,
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

        // 订阅托盘菜单手动触发事件
        AIRegenerationService.TriggerBeforeSchoolReminderRequested += OnManualBeforeSchoolReminder;
        AIRegenerationService.TriggerBeforeClassReminderRequested += OnManualBeforeClassReminder;
        AIRegenerationService.TriggerAfterSchoolSummaryRequested += OnManualAfterSchoolSummary;

        // 暴露单例给自动化模块，复用通知通道
        Plugin.SmartClassNotifierInstance = this;
    }

    private async void OnManualBeforeClassReminder()
    {
        Logger.Info("[TrayMenu] 手动触发课前提醒");
        try
        {
            await ManualBeforeClassReminderAsync();
        }
        catch (Exception ex)
        {
            Logger.Error($"[TrayMenu] 手动课前提醒失败: {ex.Message}");
        }
    }

    private async void OnManualBeforeSchoolReminder()
    {
        Logger.Info("[TrayMenu] 手动触发智能每日简报");
        try
        {
            await ManualBeforeSchoolReminderAsync();
        }
        catch (Exception ex)
        {
            Logger.Error($"[TrayMenu] 手动智能每日简报失败: {ex.Message}");
        }
    }

    public async Task<string> ManualBeforeSchoolReminderAsync(
        bool bypassCache = true,
        CancellationToken ct = default)
    {
        if (bypassCache) _ai.ClearCache();
        ct.ThrowIfCancellationRequested();

        var context = await BuildThoughtfulContextAsync(ThoughtfulScene.DailyBriefing, ct);
        var aiText = await _ai.GenerateDailyBriefing(
            GetTodayClassSchedule(), ct, context: context);
        ct.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(() => ShowThoughtfulNotification(
            "智能每日简报", aiText, "🗓️", "今日简报"));
        return aiText;
    }

    public async Task<string> ManualBeforeClassReminderAsync(
        bool bypassCache = true,
        CancellationToken ct = default)
    {
        if (bypassCache)
        {
            _ai.ClearCache();
        }

        var nextSubject = GetNextSubjectName();
        if (string.IsNullOrEmpty(nextSubject))
        {
            nextSubject = "下一节课";
            Logger.Info("[Automation] 当前无下一节课，使用通用内容触发课前提醒");
        }
        var previousSubject = GetCurrentSubjectName();
        if (string.IsNullOrWhiteSpace(previousSubject))
        {
            previousSubject = "今日课程";
        }
        var context = await BuildThoughtfulContextAsync(ThoughtfulScene.BreakStart, ct);
        var aiText = await _ai.GenerateBeforeClassReminder(previousSubject, nextSubject, ct, context: context);
        ct.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(() => ShowBeforeClassNotification(nextSubject, aiText));
        return aiText;
    }

    private async void OnManualAfterSchoolSummary()
    {
        Logger.Info("[TrayMenu] 手动触发放学总结");
        try
        {
            await ManualAfterSchoolSummaryAsync();
        }
        catch (Exception ex)
        {
            Logger.Error($"[TrayMenu] 手动放学总结失败: {ex.Message}");
        }
    }

    public async Task<string> ManualAfterSchoolSummaryAsync(
        bool bypassCache = true,
        CancellationToken ct = default)
    {
        if (bypassCache)
        {
            _ai.ClearCache();
        }

        ct.ThrowIfCancellationRequested();
        var todayClasses = GetTodayClassNames();
        if (todayClasses.Count == 0)
        {
            Logger.Info("[Automation] 手动放学总结：今日无课程");
            await ShowAutomationNotificationAsync(
                "今日学习总结",
                "今天没有课程安排。",
                true,
                ct);
            return "今天没有课程安排。";
        }
        var context = await BuildThoughtfulContextAsync(ThoughtfulScene.AfterSchool, ct);
        var aiText = await _ai.GenerateDailySummary(todayClasses, ct, context: context);
        ct.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(() => ShowNotification(new NotificationRequest
        {
            MaskContent = NotificationContent.CreateTwoIconsMask(
                "今日学习总结",
                "📋",
                "✅",
                true,
                x =>
                {
                    x.Duration = TimeSpan.FromSeconds(Settings?.MaskDurationSeconds ?? 3);
                    x.SpeechContent = "放学啦";
                }),
            OverlayContent = CreateReminderBodyContent(
                aiText,
                Settings?.RollingSpeed ?? 7,
                x =>
                {
                    x.Duration = TimeSpan.FromSeconds((Settings?.OverlayDurationSeconds ?? 5) + 2);
                    x.IsSpeechEnabled = true;
                    x.SpeechContent = aiText;
                })
        }));
        return aiText;
    }

    // ========================================
    //  触点 1：课前提醒（课间开始时，AI 根据上节+下节科目生成个性化提醒）
    // ========================================

    private async void OnBreakingTimeHandler(object? sender, EventArgs e)
    {
        Logger.Info("OnBreakingTime 触发");

        if (Settings == null || !Settings.EnableThoughtfulReminder)
        {
            Logger.Info("OnBreakingTime: Settings 为 null，跳过");
            return;
        }
        if (!Settings.EnableBeforeClassReminder)
        {
            Logger.Info("OnBreakingTime: EnableBeforeClassReminder=false，跳过");
            return;
        }

        try
        {
            var nextSubject = GetNextSubjectName();
            Logger.Info($"OnBreakingTime: nextSubject={nextSubject ?? "(空)"}");

            if (string.IsNullOrEmpty(nextSubject))
            {
                await Task.Delay(500);
                nextSubject = GetNextSubjectName();
                Logger.Info($"OnBreakingTime retry: nextSubject={nextSubject ?? "(空)"}");
            }
            if (string.IsNullOrEmpty(nextSubject)) return;

            var nextClassTime = GetNextClassStartTime();
            ResetDedupIfNeeded();
            var triggerKey = $"breaking_{nextSubject}_{nextClassTime.Hours:D2}{nextClassTime.Minutes:D2}";
            Logger.Info($"OnBreakingTime triggerKey={triggerKey}");

            if (!_triggeredBeforeClassKeys.Add(triggerKey))
            {
                Logger.Info("OnBreakingTime: 已触发过此课间，跳过");
                return;
            }

            var previousSubject = GetCurrentSubjectName();
            Logger.Info($"OnBreakingTime: previous={previousSubject}, next={nextSubject}");

            var context = await BuildThoughtfulContextAsync(ThoughtfulScene.BreakStart);
            var aiText = await _ai.GenerateBeforeClassReminder(previousSubject, nextSubject, context: context);
            Logger.Info($"OnBreakingTime AI 返回: {aiText}");

            ShowBeforeClassNotification(nextSubject, aiText);
        }
        catch (Exception ex)
        {
            Logger.Error($"课前提醒生成失败: {ex.Message}");
        }
    }

    /// <summary>每日零点重置去重集合</summary>
    private void ResetDedupIfNeeded()
    {
        var today = DateTime.Now.Date;
        if (_dedupResetDate != today)
        {
            _triggeredBeforeClassKeys.Clear();
            _triggeredBeforeSchoolKeys.Clear();
            _dedupResetDate = today;
        }
    }

    // ========================================
    //  轮询兜底：PostMainTimerTicked 主动检测课间状态
    // ========================================

    private bool _lastWasBreaking = false;

    private async Task TryTriggerBeforeSchoolReminder(ClassPlan activePlan, TimeSpan now)
    {
        var firstClass = ThoughtfulReminderTiming.GetFirstClass(activePlan);
        var layout = firstClass?.CurrentTimeLayoutItem;
        if (firstClass == null || layout == null ||
            !ThoughtfulReminderTiming.IsDueBeforeSchool(now, layout.StartTime))
            return;

        ResetDedupIfNeeded();
        var key = $"before-school:{DateTime.Now:yyyyMMdd}:{layout.StartTime:hh\\mm}";
        if (!_triggeredBeforeSchoolKeys.Add(key)) return;

        try
        {
            var context = await BuildThoughtfulContextAsync(ThoughtfulScene.DailyBriefing);
            var aiText = await _ai.GenerateDailyBriefing(GetTodayClassSchedule(), context: context);
            await Dispatcher.UIThread.InvokeAsync(() => ShowThoughtfulNotification(
                "智能每日简报", aiText, "🗓️", "今日简报"));
        }
        catch (Exception ex)
        {
            Logger.Error($"智能每日简报生成失败: {ex.Message}");
        }
    }

    private void ShowThoughtfulNotification(string title, string body, string icon, string speech)
    {
        if (Settings == null) return;

        ShowNotification(new NotificationRequest
        {
            MaskContent = NotificationContent.CreateTwoIconsMask(
                title,
                icon,
                "🏫",
                true,
                x =>
                {
                    x.Duration = TimeSpan.FromSeconds(Settings.MaskDurationSeconds);
                    x.SpeechContent = speech;
                }),
            OverlayContent = CreateReminderBodyContent(
                body,
                Settings.RollingSpeed,
                x =>
                {
                    x.Duration = TimeSpan.FromSeconds(Settings.OverlayDurationSeconds);
                    x.IsSpeechEnabled = true;
                    x.SpeechContent = body;
                })
        });
    }

    /// <summary>长提醒/多段落提醒使用 ClassIsland 滚动文本模板，避免正文超出通知区域。</summary>
    private static NotificationContent CreateReminderBodyContent(
        string body,
        int rollingSpeed,
        Action<NotificationContent> configure)
    {
        var displayBody = NormalizeNotificationBody(body);

        if (displayBody.Length <= 45)
            return NotificationContent.CreateSimpleTextContent(displayBody, configure);

        // 合并后的正文超过 45 字时启用单行滚动模板。滚动速度可调（字/秒），
        // 值越小滚动越慢、正文停留越久，可缓解长句子语音播报不完。
        var speed = Math.Clamp(rollingSpeed, 1, 30);
        var rollingDuration = TimeSpan.FromSeconds(Math.Clamp(
            8 + displayBody.Length / (double)speed, 12, 120));
        return NotificationContent.CreateRollingTextContent(
            displayBody,
            rollingDuration,
            0,
            content =>
            {
                configure(content);
                content.Duration = rollingDuration;
            });
    }

    /// <summary>
    /// 将 AI 的多段落响应规范化为通知模板可用的格式。
    /// ClassIsland 的滚动模板是单行布局，因此用中文分号合并所有非空段落。
    /// </summary>
    private static string NormalizeNotificationBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        return lines.Length switch
        {
            0 => "",
            1 => lines[0],
            _ => string.Join("；", lines.Select(line => line.TrimEnd('。', '；'))) + "。"
        };
    }

    private async void OnTimerTickHandler(object? sender, EventArgs e)
    {
        if (Settings == null || !Settings.EnableThoughtfulReminder) return;
        EnsureWindowsContextAvailability();

        try
        {
            var activePlan = ScheduleQueryHelper.GetActivePlan(_profileService);
            if (activePlan == null) return;

            var now = TimeSpan.FromTicks(DateTime.Now.TimeOfDay.Ticks);
            if (Settings.EnableBeforeSchoolReminder && _lessons.CurrentState != TimeState.OnClass)
                await TryTriggerBeforeSchoolReminder(activePlan, now);

            var currentClass = ScheduleQueryHelper.GetClassAtTime(activePlan, now);
            var currentBreak = ScheduleQueryHelper.GetCurrentBreak(activePlan, now);
            bool isInBreaking = currentClass == null && currentBreak != null;

            if (Settings.EnableBeforeClassReminder && isInBreaking && !_lastWasBreaking)
            {
                Logger.Info("TimerTick 检测到进入课间，尝试触发提醒");
                await TryTriggerBeforeClassReminder(activePlan, now);
            }
            _lastWasBreaking = isInBreaking;
        }
        catch (Exception ex)
        {
            Logger.Error($"TimerTick 检测失败: {ex.Message}");
        }
    }

    private async Task TryTriggerBeforeClassReminder(ClassPlan activePlan, TimeSpan now)
    {
        var nextSubject = GetNextSubjectNameFromPlan(activePlan, now);
        if (string.IsNullOrEmpty(nextSubject)) return;

        var nextClassTime = GetNextClassStartTimeFromPlan(activePlan, now);
        ResetDedupIfNeeded();
        var triggerKey = $"breaking_{nextSubject}_{nextClassTime.Hours:D2}{nextClassTime.Minutes:D2}";

        Logger.Info($"TryTrigger key={triggerKey}");
        if (!_triggeredBeforeClassKeys.Add(triggerKey)) return;

        var previousSubject = GetCurrentSubjectNameFromPlan(activePlan, now);
        Logger.Info($"TryTrigger: prev={previousSubject}, next={nextSubject}");

        var context = await BuildThoughtfulContextAsync(ThoughtfulScene.BreakStart);
        var aiText = await _ai.GenerateBeforeClassReminder(previousSubject, nextSubject, context: context);
        Logger.Info($"TryTrigger AI: {aiText}");

        ShowBeforeClassNotification(nextSubject, aiText);
    }

    private void ShowBeforeClassNotification(string nextSubject, string aiText)
    {
        if (Settings == null) return;

        ShowNotification(new NotificationRequest
        {
            MaskContent = NotificationContent.CreateTwoIconsMask(
                nextSubject,
                "🔔",
                "🏫",
                true,
                x =>
                {
                    x.Duration = TimeSpan.FromSeconds(Settings.MaskDurationSeconds);
                    x.SpeechContent = $"{nextSubject}课要开始了";
                }),
            OverlayContent = CreateReminderBodyContent(
                aiText,
                Settings.RollingSpeed,
                x =>
                {
                    x.Duration = TimeSpan.FromSeconds(Settings.OverlayDurationSeconds);
                    x.IsSpeechEnabled = true;
                    x.SpeechContent = aiText;
                })
        });
    }

    /// <summary>供自动化模块调用的公开通知入口（复用本提供方已验证的通知通道）。</summary>
    public void ShowAutomationNotification(string title, string body, bool enableTts = true)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ShowAutomationNotification(title, body, enableTts));
            return;
        }

        // 语音播报跟随 ClassIsland 全局设置，插件层不再单独开关；enableTts 仅表示调用方是否请求语音。
        var isSpeechEnabled = enableTts;
        var maskDuration = Settings?.MaskDurationSeconds ?? 3;
        var overlayDuration = Settings?.OverlayDurationSeconds ?? 5;

        ShowNotification(new NotificationRequest
        {
            MaskContent = NotificationContent.CreateTwoIconsMask(
                title,
                "⚡",
                "🏫",
                isSpeechEnabled,
                x =>
                {
                    x.Duration = TimeSpan.FromSeconds(maskDuration);
                    x.SpeechContent = title;
                }),
            OverlayContent = CreateReminderBodyContent(
                body,
                Settings?.RollingSpeed ?? 7,
                x =>
                {
                    x.Duration = TimeSpan.FromSeconds(overlayDuration);
                    x.IsSpeechEnabled = isSpeechEnabled;
                    x.SpeechContent = body;
                })
        });
    }

    /// <summary>等待通知在 UI 线程完成投递，供 ClassIsland 自动化动作准确传播取消和异常。</summary>
    public async Task ShowAutomationNotificationAsync(
        string title,
        string body,
        bool enableTts = true,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(() =>
            ShowAutomationNotification(title, body, enableTts));
        ct.ThrowIfCancellationRequested();
    }

    // ========================================
    //  触点 2：放学总结
    // ========================================

    private async void OnAfterSchoolHandler(object? sender, EventArgs e)
    {
        if (Settings == null || !Settings.EnableThoughtfulReminder || !Settings.EnableAfterSchoolSummary) return;

        try
        {
            var todayClasses = GetTodayClassNames();
            if (todayClasses.Count == 0) return;

            var context = await BuildThoughtfulContextAsync(ThoughtfulScene.AfterSchool);
            var aiText = await _ai.GenerateDailySummary(todayClasses, context: context);

            ShowNotification(new NotificationRequest
            {
                MaskContent = NotificationContent.CreateTwoIconsMask(
                    "今日学习总结",
                    "📋",
                    "✅",
                    true,
                    x =>
                    {
                        x.Duration = TimeSpan.FromSeconds(Settings.MaskDurationSeconds);
                        x.SpeechContent = "放学啦";
                    }),
                OverlayContent = CreateReminderBodyContent(
                    aiText,
                    Settings.RollingSpeed,
                    x =>
                    {
                        x.Duration = TimeSpan.FromSeconds(Settings.OverlayDurationSeconds + 2);
                        x.IsSpeechEnabled = true;
                        x.SpeechContent = aiText;
                    })
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"放学总结生成失败: {ex.Message}");
        }
    }

    // ========================================
    //  触点 3：换课提醒
    // ========================================

    private void OnClassHandler(object? sender, EventArgs e)
    {
        if (Settings == null || !Settings.EnableThoughtfulReminder || !Settings.EnableClassChangeAlert) return;

        try
        {
            var profile = _profileService.Profile;
            if (profile == null) return;

            var activePlan = profile.ClassPlans.Values.FirstOrDefault(p => p.IsActivated && p.IsEnabled);
            if (activePlan == null) return;

            var currentClass = activePlan.Classes.FirstOrDefault(c => c.IsEnabled && c.IsChangedClass);
            if (currentClass != null)
            {
                ShowNotification(new NotificationRequest
                {
                    MaskContent = NotificationContent.CreateTwoIconsMask(
                        "注意换课",
                        "🔄",
                        "⚠️",
                        false,
                        x => x.Duration = TimeSpan.FromSeconds(2))
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"换课提醒失败: {ex.Message}");
        }
    }

    // ========================================
    //  触点 4：自定义提醒
    // ========================================

    /// <summary>
    /// Timer 回调（ThreadPool 线程）。
    /// 仅做轻量级前置检查，然后将核心逻辑 Post 到 UI 线程执行。
    /// ShowNotification 必须在 UI 线程上调用，否则通知窗口无法创建。
    /// </summary>
    private void CheckCustomReminders(object? state)
    {
        // 轻量前置检查，避免无意义地 Post 到 UI 线程
        if (Settings == null || !Settings.EnableThoughtfulReminder || !Settings.EnableCustomReminder || Settings.CustomReminders.Count == 0) return;

        Dispatcher.UIThread.Post(CheckCustomRemindersCore, DispatcherPriority.Background);
    }

    private void CheckMusicReminder(object? state)
    {
        if (Settings == null || !Settings.EnableThoughtfulReminder || !Settings.EnableMusicReminder)
            return;
        EnsureWindowsContextAvailability();
        if (!Settings.EnableMusicReminder) return;
        if (_lessons.CurrentState == TimeState.OnClass) return;
        if (Interlocked.Exchange(ref _musicChecking, 1) == 1) return;
        _ = CheckMusicReminderAsync();
    }

    private async Task CheckMusicReminderAsync()
    {
        try
        {
            var track = await _systemContext.GetCurrentMusicAsync();
            if (track == null)
            {
                _lastMusicKey = null;
                return;
            }
            var key = $"{track.Title}\u001f{track.Artist}\u001f{track.Album}";
            if (string.Equals(_lastMusicKey, key, StringComparison.Ordinal)) return;
            _lastMusicKey = key;
            var insight = await _ai.GenerateMusicInsight(track.Title, track.Artist, track.Album);
            await Dispatcher.UIThread.InvokeAsync(() => ShowThoughtfulNotification(
                "播放岛", insight, "🎵", $"正在播放 {track.Title}"));
        }
        catch (Exception ex)
        {
            Logger.Error($"音乐贴心提醒失败: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _musicChecking, 0);
        }
    }

    private void CheckCustomRemindersCore()
    {
        // 防止 UI 线程上并发重入（极端场景：前一轮耗时超过 1 秒）
        if (Interlocked.Exchange(ref _customReminderChecking, 1) == 1) return;

        try
        {
            if (Settings == null || !Settings.EnableThoughtfulReminder || !Settings.EnableCustomReminder || Settings.CustomReminders.Count == 0) return;

            var now = DateTime.Now;

            // 快照遍历，避免在遍历期间 Settings.CustomReminders 被外部并发修改
            foreach (var reminder in Settings.CustomReminders.ToList())
            {
                if (!reminder.IsEnabled) continue;

                var triggerKey = GetDueCustomReminderKey(reminder, now);
                if (triggerKey == null) continue;
                if (string.Equals(reminder.LastTriggeredKey, triggerKey, StringComparison.Ordinal)) continue;

                reminder.LastTriggeredDate = now;
                reminder.LastTriggeredKey = triggerKey;

                if (reminder.Type == ReminderType.FixedTime)
                {
                    // 固定时间提醒是一次性任务，触发后自动停用，避免第二天同一时间误触发。
                    reminder.IsEnabled = false;
                }

                ShowCustomReminder(reminder);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"自定义提醒检查失败: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _customReminderChecking, 0);
        }
    }

    private string? GetDueCustomReminderKey(CustomReminder reminder, DateTime now)
    {
        return reminder.Type switch
        {
            ReminderType.FixedTime => GetFixedTimeReminderKey(reminder, now),
            ReminderType.DailyRepeat => GetDailyRepeatReminderKey(reminder, now),
            ReminderType.SubjectLinked => GetSubjectLinkedReminderKey(reminder, now),
            _ => null
        };
    }

    private static string? GetFixedTimeReminderKey(CustomReminder reminder, DateTime now)
    {
        if (!reminder.FixedDateTime.HasValue) return null;

        var due = reminder.FixedDateTime.Value;
        if (now < due || now > due.AddSeconds(90)) return null;

        return $"fixed:{reminder.Id}:{due:yyyyMMddHHmm}";
    }

    private static string? GetDailyRepeatReminderKey(CustomReminder reminder, DateTime now)
    {
        if (!reminder.FixedDateTime.HasValue) return null;

        var due = now.Date + reminder.FixedDateTime.Value.TimeOfDay;
        if (now < due || now > due.AddSeconds(90)) return null;

        return $"daily:{reminder.Id}:{now:yyyyMMdd}";
    }

    private string? GetSubjectLinkedReminderKey(CustomReminder reminder, DateTime now)
    {
        var targetSubject = NormalizeSubjectName(reminder.SubjectName);
        if (string.IsNullOrEmpty(targetSubject)) return null;

        var activePlan = ScheduleQueryHelper.GetActivePlan(_profileService);
        if (activePlan == null) return null;

        var nowTime = TimeSpan.FromTicks(now.TimeOfDay.Ticks);
        var minutesBefore = Math.Clamp(reminder.MinutesBefore, 0, 120);

        foreach (var cls in activePlan.Classes.Where(c => c.IsEnabled))
        {
            var layout = cls.CurrentTimeLayoutItem;
            if (layout == null) continue;

            var subject = NormalizeSubjectName(ScheduleQueryHelper.GetSubjectName(_profileService, cls.SubjectId));
            if (!string.Equals(subject, targetSubject, StringComparison.OrdinalIgnoreCase)) continue;

            var remindAt = layout.StartTime - TimeSpan.FromMinutes(minutesBefore);
            if (remindAt < TimeSpan.Zero) remindAt = TimeSpan.Zero;

            var windowEnd = remindAt.Add(TimeSpan.FromSeconds(90));
            if (nowTime >= remindAt && nowTime <= windowEnd)
                return $"subject:{reminder.Id}:{now:yyyyMMdd}:{layout.StartTime:hh\\mm}";
        }

        return null;
    }

    private void ShowCustomReminder(CustomReminder reminder)
    {
        if (Settings == null) return;

        var title = reminder.Type switch
        {
            ReminderType.SubjectLinked => $"{NormalizeSubjectName(reminder.SubjectName)}课提醒",
            ReminderType.DailyRepeat => "每日提醒",
            _ => "自定义提醒"
        };
        var content = string.IsNullOrWhiteSpace(reminder.Content) ? "该处理这件事了" : reminder.Content.Trim();

        ShowNotification(new NotificationRequest
        {
            MaskContent = NotificationContent.CreateTwoIconsMask(
                title,
                "⏰",
                "🔔",
                true,
                x =>
                {
                    x.Duration = TimeSpan.FromSeconds(Settings.MaskDurationSeconds);
                    x.SpeechContent = content;
                }),
            OverlayContent = CreateReminderBodyContent(
                content,
                Settings.RollingSpeed,
                x =>
                {
                    x.Duration = TimeSpan.FromSeconds(Settings.OverlayDurationSeconds);
                    x.IsSpeechEnabled = true;
                    x.SpeechContent = content;
                })
        });
    }

    // ========================================
    //  辅助方法
    // ========================================

    private static async Task<string> GetDutyReminderWithRetryAsync(
        CancellationToken ct,
        IReadOnlySet<string> allowedPluginIds)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var reminder = DailyBriefingDataService.GetDutyReminder(allowedPluginIds);
            if (!string.IsNullOrWhiteSpace(reminder)) return reminder;

            if (attempt < maxAttempts)
            {
                Logger.Info($"[SmartClassNotifier] 值日提醒暂为空，第 {attempt} 次重试");
                await Task.Delay(TimeSpan.FromMilliseconds(300), ct);
            }
        }

        return "";
    }

    public async Task<string> BuildThoughtfulContextAsync(ThoughtfulScene scene, CancellationToken ct = default)
    {
        if (Settings == null) return "";
        EnsureWindowsContextAvailability();

        var now = DateTime.Now;
        var lines = new List<string>
        {
            $"当前日期时间：{now:yyyy-MM-dd HH:mm}（{GetChineseDayOfWeek(now.DayOfWeek)}，{GetTimePeriod(now.Hour)}）",
            $"内容场景：{scene switch { ThoughtfulScene.DailyBriefing => "智能每日简报", ThoughtfulScene.BeforeSchool => "智能每日简报（兼容触发）", ThoughtfulScene.BreakStart => "课间开始", _ => "最后一节课结束" }}"
        };

        if (scene == ThoughtfulScene.DailyBriefing)
        {
            if (Settings.EnableDailyBriefingHoliday)
            {
                var holiday = DailyBriefingDataService.GetHolidayDescription(now.Date);
                if (!string.IsNullOrWhiteSpace(holiday)) lines.Add($"节假日信息：{holiday}");
            }
            
            // 生日祝福
            var birthdayGreeting = PluginIntegrationService.IsAuthorized(
                Settings, PluginIntegrationService.BirthdayIslandId)
                ? DailyBriefingDataService.GetBirthdayGreeting()
                : "";
            if (!string.IsNullOrWhiteSpace(birthdayGreeting))
            {
                lines.Add($"生日信息：{birthdayGreeting}");
            }

            var dailyDutyIds = PluginIntegrationService.GetAuthorizedDutyPluginIds(Settings);
            if (dailyDutyIds.Count > 0)
            {
                var dutyReminder = await GetDutyReminderWithRetryAsync(ct, dailyDutyIds);
                if (!string.IsNullOrWhiteSpace(dutyReminder))
                    lines.Add($"值日提醒：{dutyReminder}");
            }
            
            var reminders = GetTodayCustomReminderTexts(now);
            if (reminders.Count > 0) lines.Add($"今日自定义提醒：{string.Join("；", reminders)}");
            try
            {
                if (Settings.EnableDailyBriefingNews)
                {
                    var news = await _dailyBriefingData.GetNewsAsync(Settings.ClassIslandInstallDirectory, Settings.RssFeedUrls, ct);
                    if (news.Count > 0) lines.Add($"今日新闻：{string.Join("；", news)}");
                }
            }
            catch (Exception ex)
            {
                Logger.Info($"获取每日新闻失败: {ex.Message}");
            }
        }

        try
        {
            var needWeather = Settings.EnableWeatherReminder ||
                              Settings.EnableTemperatureReminder ||
                              Settings.EnableWeatherAlertReminder;
            var location = needWeather ? await _locationService.GetLocationAsync(ct) : null;
            if (location != null)
                lines.Add($"当前位置：{location.Address}（{location.Latitude:F4}, {location.Longitude:F4}）");

            var weatherTask = needWeather
                ? _systemContext.GetCurrentWeatherAsync(location, ct)
                : Task.FromResult<WindowsSystemContextService.WeatherSnapshot?>(null);
            var musicTask = Settings.EnableMusicReminder
                ? _systemContext.GetCurrentMusicAsync(ct)
                : Task.FromResult<WindowsSystemContextService.MusicTrack?>(null);
            await Task.WhenAll(weatherTask, musicTask);

            var weather = await weatherTask;
            if (weather != null)
            {
                var current = new List<string>();
                if (Settings.EnableWeatherReminder)
                    current.Add(WindowsSystemContextService.DescribeWeatherCode(weather.WeatherCode));
                if (Settings.EnableTemperatureReminder)
                {
                    var temperature = $"{weather.TemperatureC:0.#}°C";
                    if (weather.ApparentTemperatureC is double apparentTemperature)
                        temperature += $"，体感 {apparentTemperature:0.#}°C";
                    current.Add(temperature);
                }
                if (current.Count > 0) lines.Add($"当前天气：{string.Join("，", current)}");

                if (Settings.EnableWeatherAlertReminder && weather.Alerts.Count > 0)
                {
                    var alertTexts = weather.Alerts
                        .Select(a => string.IsNullOrWhiteSpace(a.Level)
                            ? a.Title
                            : $"{a.Title}（{a.Level}）")
                        .ToList();
                    lines.Add($"天气预警：{string.Join("；", alertTexts)}");
                }

                if (scene == ThoughtfulScene.AfterSchool && weather.Tomorrow != null)
                {
                    var tomorrow = weather.Tomorrow;
                    var forecast = new List<string>();
                    if (Settings.EnableWeatherReminder)
                        forecast.Add(WindowsSystemContextService.DescribeDailyWeather(tomorrow));
                    if (Settings.EnableTemperatureReminder)
                    {
                        forecast.Add($"{tomorrow.MinimumTemperatureC:0.#}～{tomorrow.MaximumTemperatureC:0.#}°C");
                        if (tomorrow.MinimumApparentTemperatureC is double minimumApparent &&
                            tomorrow.MaximumApparentTemperatureC is double maximumApparent)
                        {
                            forecast.Add($"体感 {minimumApparent:0.#}～{maximumApparent:0.#}°C");
                        }
                    }
                    if (forecast.Count > 0) lines.Add($"明日天气：{string.Join("，", forecast)}");
                }
            }

            // 放学总结场景：添加值日生提醒。
            // DutyIsland 可能在 ClassIsland 启动后晚加载，放学事件有机会先于其服务注册；
            // 因此在放学总结上下文构建期间做短暂重试，避免首次读取过早导致值日提醒丢失。
            if (scene == ThoughtfulScene.AfterSchool)
            {
                Logger.Info("[SmartClassNotifier] 放学总结场景，准备获取值日提醒");
                var dutyIds = PluginIntegrationService.GetAuthorizedDutyPluginIds(Settings);
                var dutyReminder = dutyIds.Count == 0
                    ? ""
                    : await GetDutyReminderWithRetryAsync(ct, dutyIds);
                Logger.Info($"[SmartClassNotifier] 值日提醒结果: '{dutyReminder}'");
                if (!string.IsNullOrWhiteSpace(dutyReminder))
                {
                    lines.Add($"值日提醒：{dutyReminder}");
                    Logger.Info("[SmartClassNotifier] 已添加值日提醒到上下文");
                }
                else
                {
                    Logger.Info("[SmartClassNotifier] 值日提醒为空，未添加");
                }
            }

            var music = await musicTask;
            if (music != null)
            {
                var artist = string.IsNullOrWhiteSpace(music.Artist) ? "未知歌手" : music.Artist;
                lines.Add($"当前媒体：正在播放《{music.Title}》— {artist}");
            }
        }
        catch (Exception ex)
        {
            Logger.Info($"构建贴心提醒上下文失败: {ex.Message}");
        }

        var context = string.Join("\n", lines);
        Logger.Info($"贴心提醒提示词上下文: {context.Replace("\n", " | ")}");
        return context;
    }

    private static string GetTimePeriod(int hour) => hour switch
    {
        >= 5 and < 8 => "清晨",
        >= 8 and < 12 => "上午",
        >= 12 and < 14 => "中午",
        >= 14 and < 18 => "下午",
        >= 18 and < 22 => "晚上",
        _ => "深夜"
    };

    private static string GetChineseDayOfWeek(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        _ => "周日"
    };

    private void EnsureWindowsContextAvailability()
    {
        if (Settings == null || WindowsSystemContextService.IsWindowsSystemContextSupported) return;
        Settings.EnableWeatherReminder = false;
        Settings.EnableTemperatureReminder = false;
        Settings.EnableWeatherAlertReminder = false;
        Settings.EnableMusicReminder = false;
    }

    private List<string> GetTodayClassNames()
    {
        try
        {
            var profile = _profileService.Profile;
            if (profile == null) return new List<string>();

            var activePlan = profile.ClassPlans.Values.FirstOrDefault(p => p.IsActivated && p.IsEnabled);
            if (activePlan == null) return new List<string>();

            var names = new List<string>();
            foreach (var cls in activePlan.Classes.Where(c => c.IsEnabled))
            {
                if (profile.Subjects.TryGetValue(cls.SubjectId, out var subject) && !string.IsNullOrEmpty(subject.Name))
                    names.Add(subject.Name);
            }
            return names.Distinct().ToList();
        }
        catch { return new List<string>(); }
    }

    public List<string> GetTodayBriefingClasses() => GetTodayClassSchedule();

    private List<string> GetTodayClassSchedule()
    {
        try
        {
            var plan = ScheduleQueryHelper.GetActivePlan(_profileService);
            if (plan == null) return new List<string>();
            return plan.Classes.Where(c => c.IsEnabled)
                .OrderBy(c => c.CurrentTimeLayoutItem?.StartTime ?? TimeSpan.MaxValue)
                .Select((c, i) =>
                {
                    var subject = ScheduleQueryHelper.GetSubjectName(_profileService, c.SubjectId);
                    var time = c.CurrentTimeLayoutItem;
                    return time == null ? $"第{i + 1}节：{subject}" : $"第{i + 1}节 {time.StartTime:hh\\:mm}-{time.EndTime:hh\\:mm}：{subject}";
                })
                .Where(x => !x.EndsWith("："))
                .ToList();
        }
        catch { return GetTodayClassNames(); }
    }

    private List<string> GetTodayCustomReminderTexts(DateTime now)
    {
        if (Settings?.EnableCustomReminder != true) return new List<string>();
        return Settings.CustomReminders.Where(r => r.IsEnabled &&
                (r.Type == ReminderType.DailyRepeat ||
                 (r.Type == ReminderType.FixedTime && r.FixedDateTime?.Date == now.Date) ||
                 r.Type == ReminderType.SubjectLinked))
            .Select(r => r.Content?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .Take(8)
            .ToList();
    }

    private string GetCurrentSubjectName()
    {
        try
        {
            var activePlan = ScheduleQueryHelper.GetActivePlan(_profileService);
            if (activePlan == null) return "";

            var now = TimeSpan.FromTicks(DateTime.Now.TimeOfDay.Ticks);
            var currentClass = ScheduleQueryHelper.GetClassAtTime(activePlan, now);
            if (currentClass != null)
                return ScheduleQueryHelper.GetSubjectName(_profileService, currentClass.SubjectId);
        }
        catch { }
        return "";
    }

    private string GetNextSubjectName()
    {
        try
        {
            var activePlan = ScheduleQueryHelper.GetActivePlan(_profileService);
            if (activePlan == null) return "";

            var now = TimeSpan.FromTicks(DateTime.Now.TimeOfDay.Ticks);
            return GetNextSubjectNameFromPlan(activePlan, now);
        }
        catch { }
        return "";
    }

    private TimeSpan GetNextClassStartTime()
    {
        try
        {
            var activePlan = ScheduleQueryHelper.GetActivePlan(_profileService);
            if (activePlan == null) return TimeSpan.Zero;

            var now = TimeSpan.FromTicks(DateTime.Now.TimeOfDay.Ticks);
            return GetNextClassStartTimeFromPlan(activePlan, now);
        }
        catch { }
        return TimeSpan.Zero;
    }

    private string GetNextSubjectNameFromPlan(ClassPlan plan, TimeSpan now)
    {
        try
        {
            var currentClass = ScheduleQueryHelper.GetClassAtTime(plan, now);
            if (currentClass != null)
            {
                var classes = plan.Classes.Where(c => c.IsEnabled).ToList();
                var idx = classes.IndexOf(currentClass);
                if (idx >= 0 && idx < classes.Count - 1)
                    return ScheduleQueryHelper.GetSubjectName(_profileService, classes[idx + 1].SubjectId);
                return "";
            }

            var nextClass = ScheduleQueryHelper.GetNextClass(plan, now);
            if (nextClass != null)
                return ScheduleQueryHelper.GetSubjectName(_profileService, nextClass.SubjectId);
        }
        catch { }
        return "";
    }

    private TimeSpan GetNextClassStartTimeFromPlan(ClassPlan plan, TimeSpan now)
    {
        try
        {
            var nextClass = ScheduleQueryHelper.GetNextClass(plan, now);
            if (nextClass?.CurrentTimeLayoutItem != null)
                return nextClass.CurrentTimeLayoutItem.StartTime;
        }
        catch { }
        return TimeSpan.Zero;
    }

    private string GetCurrentSubjectNameFromPlan(ClassPlan plan, TimeSpan now)
    {
        try
        {
            var currentClass = ScheduleQueryHelper.GetClassAtTime(plan, now);
            if (currentClass != null)
                return ScheduleQueryHelper.GetSubjectName(_profileService, currentClass.SubjectId);
        }
        catch { }
        return "";
    }

    private static string NormalizeSubjectName(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? ""
            : value.Trim().TrimEnd('课');
    }
}
