using Avalonia.Threading;
using ClassIsland.AISmartClass.Models.ExamMode;
using ClassIsland.Core.Abstractions.Services.NotificationProviders;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Notification;
using System.Timers;
using Timer = System.Timers.Timer;

namespace ClassIsland.AISmartClass.Services.ExamMode;

[NotificationProviderInfo(
    "A1B2C3D4-E5F6-7890-1234-567890ABCDEF",
    "AIIsland 考试模式",
    "\ue7ba",
    "根据 ClassIsland 考试课表触发考前提醒、结束提醒和听力自动播放。")]
public sealed class ExamModeScheduler : NotificationProviderBase, IDisposable
{
    private readonly Timer _timer;
    private DateTime _lastCheck = BusinessTime.Now;
    private string _lastClassSessionId = "";
    private LessonPhase _previousPhase = LessonPhase.NoPlan;
    private bool _phaseInitialized;
    private long _audioSequence;
    private readonly HashSet<string> _firedReminderKeys = new();
    private readonly HashSet<string> _firedStartKeys = new();
    private readonly HashSet<string> _firedRemainingKeys = new();
    private readonly HashSet<string> _firedAutoPlayKeys = new();
    private readonly HashSet<string> _firedEndKeys = new();
    private int _isChecking;
    private int _isDisposed;

    public ExamModeScheduler()
    {
        // 替换基类默认创建的 FluentIcon 为自定义字体图标（与设置页考试模式大项图标一致）
        IconElement = SettingsPageIconPatcher.CreateExamModeIcon();

        _timer = new Timer(1000) { AutoReset = true };
        _timer.Elapsed += OnTimerElapsed;
        _timer.Start();
        Logger.Info("[ExamModeScheduler] 已启动，考试调度跟随插件统一业务时钟");
    }

    private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (Volatile.Read(ref _isDisposed) != 0 ||
            Interlocked.Exchange(ref _isChecking, 1) != 0)
        {
            return;
        }

        try
        {
            var snapshot = LessonSnapshotService.Capture();
            var now = snapshot.Now;

            if (BusinessClockContinuity.ShouldResetBaseline(_lastCheck, now))
            {
                // 时间来源切换、偏移调整或系统休眠恢复：以当前时间重建基线，不补发陈旧提醒。
                _lastCheck = now;
                _previousPhase = snapshot.Phase;
                Logger.Info($"[ExamModeScheduler] 检测到业务时间跳变，已重建调度基线: {now:HH:mm:ss}");
                return;
            }

            var settings = Plugin.GetAISettings()?.ExamMode;
            var server = ExamModeServer.GetOrCreate();

            if (settings is not { EnableScheduler: true } || !server.IsRunning)
            {
                _previousPhase = snapshot.Phase;
                _lastCheck = now;
                return;
            }

            // 首次观察只建立阶段基线，避免启动时补发开考/结束提醒。
            var firstObservation = !_phaseInitialized;
            _phaseInitialized = true;

            CheckPreExamReminders(settings, snapshot, server);
            if (!firstObservation)
            {
                CheckExamStart(settings, snapshot, server);
                CheckExamEnd(settings, snapshot, server);
            }
            CheckRemainingTimeReminders(settings, snapshot, server);
            CheckAudioAutoPlay(settings, snapshot, server);
            _previousPhase = snapshot.Phase;
            _lastCheck = now;
            PruneOldKeys(now);
        }
        catch (Exception ex)
        {
            Logger.Error($"[ExamModeScheduler] 调度检查异常: {ex}");
        }
        finally
        {
            Volatile.Write(ref _isChecking, 0);
        }
    }

    private void CheckPreExamReminders(ExamModeSettings settings, LessonSnapshot snapshot, ExamModeServer server)
    {
        if (!settings.ReminderEnabled || snapshot.IsOnClass)
        {
            return;
        }

        var nextItem = snapshot.NextLayout;
        if (nextItem == null)
        {
            return;
        }

        var now = snapshot.Now;
        var start = now.Date + nextItem.StartTime;
        var subjectName = NormalizeSubjectName(snapshot.NextSubjectName, "下一场考试");
        var sessionId = BuildSessionId(start, subjectName);
        var crossed = settings.ReminderMinutes
            .Where(m => m is > 0 and <= 120)
            .Distinct()
            .Select(minute => new { Minute = minute, Trigger = start.AddMinutes(-minute) })
            .Where(item => item.Trigger > _lastCheck && item.Trigger <= now)
            .OrderBy(item => item.Minute)
            .FirstOrDefault(item => !_firedReminderKeys.Contains($"{sessionId}:pre:{item.Minute}"));

        if (crossed == null)
        {
            return;
        }

        var key = $"{sessionId}:pre:{crossed.Minute}";
        _firedReminderKeys.Add(key);
        var title = $"距 {subjectName} 开始还有 {crossed.Minute} 分钟";
        var body = $"{subjectName} 将于 {start:HH:mm} 开始，请做好准备。";
        ShowNativeNotification(title, body, true);
        server.AddReminderHistory("pre", title, body, now);
        Logger.Info($"[ExamModeScheduler] 考前提醒已触发: {key}");
    }

    private void CheckExamStart(ExamModeSettings settings, LessonSnapshot snapshot, ExamModeServer server)
    {
        if (!settings.StartNotificationEnabled || _previousPhase == LessonPhase.OnClass || !snapshot.IsOnClass)
        {
            return;
        }

        var item = snapshot.CurrentLayout;
        if (item == null)
        {
            return;
        }

        var now = snapshot.Now;
        var subjectName = NormalizeSubjectName(snapshot.CurrentSubjectName, "本场考试");
        var start = now.Date + item.StartTime;
        var end = now.Date + item.EndTime;
        var sessionId = BuildSessionId(start, subjectName);
        _lastClassSessionId = sessionId;
        if (!_firedStartKeys.Add(sessionId))
        {
            return;
        }

        var title = $"{subjectName}考试开始";
        var body = $"考试时间 {start:HH:mm}–{end:HH:mm}，请开始答题。";
        ShowNativeNotification(title, body, true);
        server.AddReminderHistory("start", title, body, now);
        Logger.Info($"[ExamModeScheduler] 开考提醒已触发: {sessionId}");
    }

    private void CheckRemainingTimeReminders(ExamModeSettings settings, LessonSnapshot snapshot, ExamModeServer server)
    {
        if (!settings.RemainingNotificationEnabled || !snapshot.IsOnClass)
        {
            return;
        }

        var item = snapshot.CurrentLayout;
        if (item == null)
        {
            return;
        }

        var now = snapshot.Now;
        var subjectName = NormalizeSubjectName(snapshot.CurrentSubjectName, "本场考试");
        var start = now.Date + item.StartTime;
        var end = now.Date + item.EndTime;
        var sessionId = BuildSessionId(start, subjectName);
        _lastClassSessionId = sessionId;
        var crossed = settings.RemainingMinutes
            .Where(m => m is > 0 and <= 120)
            .Distinct()
            .Select(minute => new { Minute = minute, Trigger = end.AddMinutes(-minute) })
            .Where(entry => entry.Trigger > _lastCheck && entry.Trigger <= now)
            .OrderBy(entry => entry.Minute)
            .FirstOrDefault(entry => !_firedRemainingKeys.Contains($"{sessionId}:remaining:{entry.Minute}"));

        if (crossed == null)
        {
            return;
        }

        var key = $"{sessionId}:remaining:{crossed.Minute}";
        _firedRemainingKeys.Add(key);
        var title = $"{subjectName}考试剩余 {crossed.Minute} 分钟";
        var body = "请合理安排答题和检查时间。";
        ShowNativeNotification(title, body, true);
        server.AddReminderHistory("remaining", title, body, now);
        Logger.Info($"[ExamModeScheduler] 剩余时间提醒已触发: {key}");
    }

    private void CheckAudioAutoPlay(ExamModeSettings settings, LessonSnapshot snapshot, ExamModeServer server)
    {
        if (!settings.AutoPlayEnabled || string.IsNullOrWhiteSpace(settings.AudioPath) ||
            !File.Exists(settings.AudioPath))
        {
            return;
        }

        var offset = Math.Clamp(settings.AutoPlayOffsetMinutes, -60, 60);
        // 负偏移表示开考前（尚未进入考试），非负偏移表示开考后（必须在考试中）。
        if (offset < 0 && snapshot.IsOnClass)
        {
            return;
        }

        if (offset >= 0 && !snapshot.IsOnClass)
        {
            return;
        }

        var item = snapshot.IsOnClass ? snapshot.CurrentLayout : snapshot.NextLayout;
        if (item == null)
        {
            return;
        }

        var now = snapshot.Now;
        var subjectName = snapshot.IsOnClass
            ? NormalizeSubjectName(snapshot.CurrentSubjectName, "当前考试")
            : NormalizeSubjectName(snapshot.NextSubjectName, "下一场考试");
        var start = now.Date + item.StartTime;
        var sessionId = BuildSessionId(start, subjectName);
        if (server.GetAudioSessionChoice(sessionId) != true)
        {
            return;
        }

        var trigger = start.AddMinutes(offset);

        if (trigger <= _lastCheck || trigger > now || !_firedAutoPlayKeys.Add(sessionId))
        {
            return;
        }

        var sequence = Interlocked.Increment(ref _audioSequence);
        server.SetAudioCommand(new ExamAudioCommand
        {
            Sequence = sequence,
            Action = "play",
            Url = "/api/audio/file",
            Volume = Math.Clamp(settings.AudioVolume, 0, 1),
            SessionId = sessionId,
            ExamName = subjectName,
            // 通信命令有效期必须使用真实时间，不能跟随业务时间偏移。
            ExpiresAtUnixSeconds = DateTimeOffset.Now.AddMinutes(2).ToUnixTimeSeconds()
        });
        Logger.Info($"[ExamModeScheduler] 听力自动播放命令已下发: {sessionId}, seq={sequence}");
    }

    private void CheckExamEnd(ExamModeSettings settings, LessonSnapshot snapshot, ExamModeServer server)
    {
        var now = snapshot.Now;
        if (snapshot.IsOnClass)
        {
            if (snapshot.CurrentLayout != null)
            {
                var name = NormalizeSubjectName(snapshot.CurrentSubjectName, "当前考试");
                _lastClassSessionId = BuildSessionId(now.Date + snapshot.CurrentLayout.StartTime, name);
            }

            return;
        }

        if (_previousPhase != LessonPhase.OnClass || !settings.EndNotificationEnabled)
        {
            return;
        }

        var sessionId = string.IsNullOrWhiteSpace(_lastClassSessionId)
            ? $"{now:yyyyMMdd-HHmm}:ended"
            : _lastClassSessionId;
        if (!_firedEndKeys.Add(sessionId))
        {
            return;
        }

        var subjectName = sessionId.Split(':').LastOrDefault() ?? "本场考试";
        var title = $"{subjectName}考试结束";
        var body = "请停止答题并检查姓名、班级和答题卡。";
        ShowNativeNotification(title, body, true);
        server.AddReminderHistory("end", title, body, now);
        Logger.Info($"[ExamModeScheduler] 考试结束原生提醒已触发: {sessionId}");
    }

    private void ShowNativeNotification(string title, string body, bool forceSound = false)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var request = new NotificationRequest
                {
                    MaskContent = NotificationContent.CreateTwoIconsMask(
                        title,
                        "fluent(\ue7ba)",
                        hasRightIcon: false,
                        factory: content =>
                        {
                            content.Duration = TimeSpan.FromSeconds(3);
                            content.IsSpeechEnabled = true;
                            content.SpeechContent = title;
                        }),
                    OverlayContent = NotificationContent.CreateSimpleTextContent(body, content =>
                    {
                        content.Duration = TimeSpan.FromSeconds(6);
                        content.IsSpeechEnabled = true;
                        content.SpeechContent = body;
                    })
                };

                if (forceSound)
                {
                    request.RequestNotificationSettings.IsSettingsEnabled = true;
                    request.RequestNotificationSettings.IsNotificationEnabled = true;
                    request.RequestNotificationSettings.IsNotificationEffectEnabled = true;
                    request.RequestNotificationSettings.IsNotificationSoundEnabled = true;
                    request.RequestNotificationSettings.IsSpeechEnabled = true;
                }

                ShowNotification(request);
            }
            catch (Exception ex)
            {
                Logger.Error($"[ExamModeScheduler] 显示 ClassIsland 原生提醒失败: {ex}");
            }
        });
    }

    private static string NormalizeSubjectName(string? name, string fallback)
    {
        return string.IsNullOrWhiteSpace(name) || name == "???" ? fallback : name.Trim();
    }

    private static string BuildSessionId(DateTime start, string subjectName)
    {
        return $"{start:yyyyMMdd-HHmm}:{subjectName}";
    }

    private void PruneOldKeys(DateTime now)
    {
        var prefix = now.AddDays(-1).ToString("yyyyMMdd");
        _firedReminderKeys.RemoveWhere(key => string.CompareOrdinal(key, prefix) < 0);
        _firedStartKeys.RemoveWhere(key => string.CompareOrdinal(key, prefix) < 0);
        _firedRemainingKeys.RemoveWhere(key => string.CompareOrdinal(key, prefix) < 0);
        _firedAutoPlayKeys.RemoveWhere(key => string.CompareOrdinal(key, prefix) < 0);
        _firedEndKeys.RemoveWhere(key => string.CompareOrdinal(key, prefix) < 0);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _timer.Stop();
        _timer.Elapsed -= OnTimerElapsed;
        _timer.Dispose();
    }
}
