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

    private Timer? _customTimer;

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

        _lessons.OnBreakingTime += OnBreakingTimeHandler;
        _lessons.OnAfterSchool += OnAfterSchoolHandler;
        _lessons.OnClass += OnClassHandler;
        _lessons.PostMainTimerTicked += OnTimerTickHandler;  // 轮询兜底：主动检测课间状态

        _customTimer = new Timer(CheckCustomReminders, null,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    // ========================================
    //  触点 1：课前提醒（课间开始时，AI 根据上节+下节科目生成个性化提醒）
    // ========================================

    private async void OnBreakingTimeHandler(object? sender, EventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[AIIsland] OnBreakingTime 触发");

        // Settings 可能尚未被 ClassIsland 注入，防御性检查
        if (Settings == null)
        {
            System.Diagnostics.Debug.WriteLine("[AIIsland] OnBreakingTime: Settings 为 null，跳过");
            return;
        }
        if (!Settings.EnableBeforeClassReminder)
        {
            System.Diagnostics.Debug.WriteLine("[AIIsland] OnBreakingTime: EnableBeforeClassReminder=false，跳过");
            return;
        }

        try
        {
            var nextSubject = GetNextSubjectName();
            System.Diagnostics.Debug.WriteLine($"[AIIsland] OnBreakingTime: nextSubject={nextSubject ?? "(空)"}");

            if (string.IsNullOrEmpty(nextSubject))
            {
                await Task.Delay(500);
                nextSubject = GetNextSubjectName();
                System.Diagnostics.Debug.WriteLine($"[AIIsland] OnBreakingTime retry: nextSubject={nextSubject ?? "(空)"}");
            }
            if (string.IsNullOrEmpty(nextSubject)) return;

            // 用"科目+下节课起始时间"去重，确保每个课间独立触发
            var nextClassTime = GetNextClassStartTime();
            ResetDedupIfNeeded();
            var triggerKey = $"breaking_{nextSubject}_{nextClassTime.Hours:D2}{nextClassTime.Minutes:D2}";
            System.Diagnostics.Debug.WriteLine($"[AIIsland] OnBreakingTime triggerKey={triggerKey}");

            if (!_triggeredBeforeClassKeys.Add(triggerKey))
            {
                System.Diagnostics.Debug.WriteLine("[AIIsland] OnBreakingTime: 已触发过此课间，跳过");
                return;
            }

            var previousSubject = GetCurrentSubjectName();
            System.Diagnostics.Debug.WriteLine($"[AIIsland] OnBreakingTime: previous={previousSubject}, next={nextSubject}");

            var aiText = await _ai.GenerateBeforeClassReminder(previousSubject, nextSubject);
            System.Diagnostics.Debug.WriteLine($"[AIIsland] OnBreakingTime AI 返回: {aiText}");

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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"课前提醒生成失败: {ex.Message}");
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

    /// <summary>
    /// 主计时器每次 tick 后主动检测当前是否处于课间。
    /// 如果 OnBreakingTime 事件正常触发，这里会被去重跳过（不会重复触发）。
    /// 如果 OnBreakingTime 事件未触发（如 ClassIsland 时序问题），这里作为兜底补偿。
    /// </summary>
    private async void OnTimerTickHandler(object? sender, EventArgs e)
    {
        if (Settings == null || !Settings.EnableBeforeClassReminder) return;

        try
        {
            var activePlan = ScheduleQueryHelper.GetActivePlan(_profileService);
            if (activePlan == null) return;

            var now = TimeSpan.FromTicks(DateTime.Now.TimeOfDay.Ticks);

            // 检测当前是否处于课间：不在任何上课区间内，且在某两节课之间
            var currentClass = ScheduleQueryHelper.GetClassAtTime(activePlan, now);
            bool isInBreaking = (currentClass == null);

            if (isInBreaking && !_lastWasBreaking)
            {
                // 刚进入课间状态！尝试触发课前提醒
                System.Diagnostics.Debug.WriteLine("[AIIsland] TimerTick 检测到进入课间，尝试触发提醒");
                await TryTriggerBeforeClassReminder(activePlan, now);
            }
            _lastWasBreaking = isInBreaking;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AIIsland] TimerTick 检测失败: {ex.Message}");
        }
    }

    /// <summary>尝试触发课前提醒（供 OnBreakingTime 和 TimerTick 共用）</summary>
    private async Task TryTriggerBeforeClassReminder(ClassPlan activePlan, TimeSpan now)
    {
        var nextSubject = GetNextSubjectNameFromPlan(activePlan, now);
        if (string.IsNullOrEmpty(nextSubject)) return;

        var nextClassTime = GetNextClassStartTimeFromPlan(activePlan, now);
        ResetDedupIfNeeded();
        var triggerKey = $"breaking_{nextSubject}_{nextClassTime.Hours:D2}{nextClassTime.Minutes:D2}";

        System.Diagnostics.Debug.WriteLine($"[AIIsland] TryTrigger key={triggerKey}");
        if (!_triggeredBeforeClassKeys.Add(triggerKey)) return;

        var previousSubject = GetCurrentSubjectNameFromPlan(activePlan, now);
        System.Diagnostics.Debug.WriteLine($"[AIIsland] TryTrigger: prev={previousSubject}, next={nextSubject}");

        var aiText = await _ai.GenerateBeforeClassReminder(previousSubject, nextSubject);
        System.Diagnostics.Debug.WriteLine($"[AIIsland] TryTrigger AI: {aiText}");

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
            System.Diagnostics.Debug.WriteLine($"放学总结生成失败: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"换课提醒失败: {ex.Message}");
        }
    }

    // ========================================
    //  触点 4：定时提醒检查
    // ========================================

    private void CheckCustomReminders(object? state)
    {
        if (Settings == null || Settings.CustomReminders.Count == 0) return;

        var now = DateTime.Now;

        foreach (var reminder in Settings.CustomReminders.ToList())
        {
            if (!reminder.IsEnabled) continue;

            bool shouldTrigger = reminder.Type switch
            {
                ReminderType.FixedTime =>
                    reminder.FixedDateTime.HasValue &&
                    Math.Abs((reminder.FixedDateTime.Value - now).TotalSeconds) < 3 &&
                    reminder.LastTriggeredDate?.Date != now.Date,

                ReminderType.DailyRepeat =>
                    reminder.FixedDateTime.HasValue &&
                    Math.Abs((reminder.FixedDateTime.Value.TimeOfDay - now.TimeOfDay).TotalSeconds) < 3 &&
                    reminder.LastTriggeredDate?.Date != now.Date,

                ReminderType.SubjectLinked =>
                    CheckSubjectReminder(reminder, now),

                _ => false
            };

            if (shouldTrigger)
            {
                reminder.LastTriggeredDate = now;
                ShowNotification(new NotificationRequest
                {
                    MaskContent = NotificationContent.CreateTwoIconsMask(
                        reminder.Content,
                        "⏰",
                        "🔔",
                        false,
                        x => x.Duration = TimeSpan.FromSeconds(3))
                });
            }
        }
    }

    private bool CheckSubjectReminder(CustomReminder reminder, DateTime now)
    {
        if (string.IsNullOrEmpty(reminder.SubjectName)) return false;

        // 优先检查下节课，也检查当前课（防止时序偏差）
        var nextSubject = GetNextSubjectName();
        var currentSubject = GetCurrentSubjectName();

        bool match = nextSubject == reminder.SubjectName || currentSubject == reminder.SubjectName;
        if (!match) return false;

        return reminder.LastTriggeredDate?.Date != now.Date;
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

            // 如果正在上课，取下一节课
            var currentClass = ScheduleQueryHelper.GetClassAtTime(activePlan, now);
            if (currentClass != null)
            {
                var classes = activePlan.Classes.Where(c => c.IsEnabled).ToList();
                var idx = classes.IndexOf(currentClass);
                if (idx >= 0 && idx < classes.Count - 1)
                    return ScheduleQueryHelper.GetSubjectName(_profileService, classes[idx + 1].SubjectId);
                return ""; // 最后一节课
            }

            // 如果在课间，取即将开始的下一节课
            var nextClass = ScheduleQueryHelper.GetNextClass(activePlan, now);
            if (nextClass != null)
                return ScheduleQueryHelper.GetSubjectName(_profileService, nextClass.SubjectId);
        }
        catch { }
        return "";
    }

    /// <summary>获取下节课的起始时间，用于去重 key 区分不同课间</summary>
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

    // ========================================
    //  直接接收 plan/now 参数的辅助方法（避免重复查询 GetActivePlan）
    // ========================================

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
}
