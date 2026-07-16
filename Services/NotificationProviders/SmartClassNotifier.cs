using System.Diagnostics;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Abstractions.Services.NotificationProviders;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Notification;
using ClassIsland.Shared.Models.Profile;
using ClassIsland.AISmartClass.Models;

namespace ClassIsland.AISmartClass.Services.NotificationProviders;

[NotificationProviderInfo(
    "8F3A2B1C-9D4E-4A5F-B6C7-1E2F3A4B5C6D",
    "AIIsland 智能提醒",
    "课间开始时根据上节+下节科目生成个性化提醒；支持放学总结和自定义定时提醒"
)]
public class SmartClassNotifier : NotificationProviderBase<SmartClassNotifierSettings>
{
    private readonly ILessonsService _lessons;
    private readonly IProfileService _profileService;
    private readonly AIChatService _ai;

    private readonly Timer _customTimer;
    private int _customReminderChecking;

    /// <summary>已触发的课前提醒 key 集合，每个课间独立去重</summary>
    private readonly HashSet<string> _triggeredBeforeClassKeys = new();
    private DateTime _dedupResetDate = DateTime.MinValue;

    public SmartClassNotifier(IProfileService profileService, ILessonsService lessonsService, AIChatService aiService)
    {
        _profileService = profileService;
        _lessons = lessonsService;
        _ai = aiService;

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

        // 订阅托盘菜单手动触发事件
        AIRegenerationService.TriggerBeforeClassReminderRequested += OnManualBeforeClassReminder;
        AIRegenerationService.TriggerAfterSchoolSummaryRequested += OnManualAfterSchoolSummary;
    }

    private void OnManualBeforeClassReminder()
    {
        Logger.Info("[TrayMenu] 手动触发课前提醒");
        _ = ManualBeforeClassReminderAsync();
    }

    private async Task ManualBeforeClassReminderAsync()
    {
        try
        {
            var nextSubject = GetNextSubjectName();
            if (string.IsNullOrEmpty(nextSubject))
            {
                nextSubject = "下一节课";
                Logger.Info("[TrayMenu] 当前无下一节课，使用通用内容触发课前提醒");
            }
            var previousSubject = GetCurrentSubjectName();
            if (string.IsNullOrWhiteSpace(previousSubject))
            {
                previousSubject = "今日课程";
            }
            var aiText = await _ai.GenerateBeforeClassReminder(previousSubject, nextSubject);
            ShowBeforeClassNotification(nextSubject, aiText);
        }
        catch (Exception ex)
        {
            Logger.Error($"[TrayMenu] 手动课前提醒失败: {ex.Message}");
        }
    }

    private void OnManualAfterSchoolSummary()
    {
        Logger.Info("[TrayMenu] 手动触发放学总结");
        _ = ManualAfterSchoolSummaryAsync();
    }

    private async Task ManualAfterSchoolSummaryAsync()
    {
        try
        {
            var todayClasses = GetTodayClassNames();
            if (todayClasses.Count == 0)
            {
                Logger.Info("[TrayMenu] 手动放学总结：今日无课程");
                return;
            }
            var aiText = await _ai.GenerateDailySummary(todayClasses);
            ShowNotification(new NotificationRequest
            {
                MaskContent = NotificationContent.CreateTwoIconsMask(
                    "今日学习总结",
                    "📋",
                    "✅",
                    Settings?.EnableTTS ?? false,
                    x =>
                    {
                        x.Duration = TimeSpan.FromSeconds(Settings?.MaskDurationSeconds ?? 3);
                        x.SpeechContent = "放学啦";
                    }),
                OverlayContent = NotificationContent.CreateSimpleTextContent(
                    aiText,
                    x =>
                    {
                        x.Duration = TimeSpan.FromSeconds((Settings?.OverlayDurationSeconds ?? 5) + 2);
                        x.IsSpeechEnabled = Settings?.EnableTTS ?? false;
                        x.SpeechContent = aiText;
                    })
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"[TrayMenu] 手动放学总结失败: {ex.Message}");
        }
    }

    // ========================================
    //  触点 1：课前提醒（课间开始时，AI 根据上节+下节科目生成个性化提醒）
    // ========================================

    private async void OnBreakingTimeHandler(object? sender, EventArgs e)
    {
        Logger.Info("OnBreakingTime 触发");

        if (Settings == null)
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

            var aiText = await _ai.GenerateBeforeClassReminder(previousSubject, nextSubject);
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
            _dedupResetDate = today;
        }
    }

    // ========================================
    //  轮询兜底：PostMainTimerTicked 主动检测课间状态
    // ========================================

    private bool _lastWasBreaking = false;

    private async void OnTimerTickHandler(object? sender, EventArgs e)
    {
        if (Settings == null || !Settings.EnableBeforeClassReminder) return;

        try
        {
            var activePlan = ScheduleQueryHelper.GetActivePlan(_profileService);
            if (activePlan == null) return;

            var now = TimeSpan.FromTicks(DateTime.Now.TimeOfDay.Ticks);
            var currentClass = ScheduleQueryHelper.GetClassAtTime(activePlan, now);
            var currentBreak = ScheduleQueryHelper.GetCurrentBreak(activePlan, now);
            bool isInBreaking = currentClass == null && currentBreak != null;

            if (isInBreaking && !_lastWasBreaking)
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

        var aiText = await _ai.GenerateBeforeClassReminder(previousSubject, nextSubject);
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
                Settings.EnableTTS,
                x =>
                {
                    x.Duration = TimeSpan.FromSeconds(Settings.MaskDurationSeconds);
                    x.SpeechContent = $"{nextSubject}课要开始了";
                }),
            OverlayContent = NotificationContent.CreateSimpleTextContent(
                aiText,
                x =>
                {
                    x.Duration = TimeSpan.FromSeconds(Settings.OverlayDurationSeconds);
                    x.IsSpeechEnabled = Settings.EnableTTS;
                    x.SpeechContent = aiText;
                })
        });
    }

    // ========================================
    //  触点 2：放学总结
    // ========================================

    private async void OnAfterSchoolHandler(object? sender, EventArgs e)
    {
        if (Settings == null || !Settings.EnableAfterSchoolSummary) return;

        try
        {
            var todayClasses = GetTodayClassNames();
            if (todayClasses.Count == 0) return;

            var aiText = await _ai.GenerateDailySummary(todayClasses);

            ShowNotification(new NotificationRequest
            {
                MaskContent = NotificationContent.CreateTwoIconsMask(
                    "今日学习总结",
                    "📋",
                    "✅",
                    Settings.EnableTTS,
                    x =>
                    {
                        x.Duration = TimeSpan.FromSeconds(Settings.MaskDurationSeconds);
                        x.SpeechContent = "放学啦";
                    }),
                OverlayContent = NotificationContent.CreateSimpleTextContent(
                    aiText,
                    x =>
                    {
                        x.Duration = TimeSpan.FromSeconds(Settings.OverlayDurationSeconds + 2);
                        x.IsSpeechEnabled = Settings.EnableTTS;
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
        if (Settings == null || !Settings.EnableClassChangeAlert) return;

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
        if (Settings == null || Settings.CustomReminders.Count == 0) return;

        Dispatcher.UIThread.Post(CheckCustomRemindersCore, DispatcherPriority.Background);
    }

    private void CheckCustomRemindersCore()
    {
        // 防止 UI 线程上并发重入（极端场景：前一轮耗时超过 1 秒）
        if (Interlocked.Exchange(ref _customReminderChecking, 1) == 1) return;

        try
        {
            if (Settings == null || Settings.CustomReminders.Count == 0) return;

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
                Settings.EnableTTS,
                x =>
                {
                    x.Duration = TimeSpan.FromSeconds(Settings.MaskDurationSeconds);
                    x.SpeechContent = content;
                }),
            OverlayContent = NotificationContent.CreateSimpleTextContent(
                content,
                x =>
                {
                    x.Duration = TimeSpan.FromSeconds(Settings.OverlayDurationSeconds);
                    x.IsSpeechEnabled = Settings.EnableTTS;
                    x.SpeechContent = content;
                })
        });
    }

    // ========================================
    //  辅助方法
    // ========================================

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
