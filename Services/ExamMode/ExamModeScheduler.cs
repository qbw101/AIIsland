using Avalonia.Threading;
using ClassIsland.AISmartClass.Models.ExamMode;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Abstractions.Services.NotificationProviders;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Notification;
using ClassIsland.Shared.Enums;
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
    private readonly ILessonsService _lessons;
    private readonly Timer _timer;
    private DateTime _lastCheck = DateTime.Now;
    private string _lastClassSessionId = "";
    private TimeState _previousState;
    private long _audioSequence;
    private readonly HashSet<string> _firedReminderKeys = new();
    private readonly HashSet<string> _firedStartKeys = new();
    private readonly HashSet<string> _firedRemainingKeys = new();
    private readonly HashSet<string> _firedAutoPlayKeys = new();
    private readonly HashSet<string> _firedEndKeys = new();
    private int _isChecking;
    private int _isDisposed;

    public ExamModeScheduler(ILessonsService lessons)
    {
        _lessons = lessons;
        _previousState = lessons.CurrentState;
        _timer = new Timer(1000) { AutoReset = true };
        _timer.Elapsed += OnTimerElapsed;
        _timer.Start();
        Logger.Info("[ExamModeScheduler] 已启动，考试时间全部同步 ClassIsland");
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
            var now = DateTime.Now;
            var settings = Plugin.GetAISettings()?.ExamMode;
            var server = ExamModeServer.GetOrCreate();

            if (settings is not { EnableScheduler: true } || !server.IsRunning)
            {
                _previousState = _lessons.CurrentState;
                _lastCheck = now;
                return;
            }

            CheckPreExamReminders(settings, now, server);
            CheckExamStart(settings, now, server);
            CheckRemainingTimeReminders(settings, now, server);
            CheckAudioAutoPlay(settings, now, server);
            CheckExamEnd(settings, now, server);
            _previousState = _lessons.CurrentState;
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

    private void CheckPreExamReminders(ExamModeSettings settings, DateTime now, ExamModeServer server)
    {
        if (!settings.ReminderEnabled || _lessons.CurrentState == TimeState.OnClass)
        {
            return;
        }

        var nextItem = _lessons.NextClassTimeLayoutItem;
        var start = now.Date + nextItem.StartTime;
        var subjectName = NormalizeSubjectName(_lessons.NextClassSubject?.Name, "下一场考试");
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

    private void CheckExamStart(ExamModeSettings settings, DateTime now, ExamModeServer server)
    {
        if (!settings.StartNotificationEnabled || _previousState == TimeState.OnClass ||
            _lessons.CurrentState != TimeState.OnClass)
        {
            return;
        }

        var item = _lessons.CurrentTimeLayoutItem;
        var subjectName = NormalizeSubjectName(_lessons.CurrentSubject?.Name, "本场考试");
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

    private void CheckRemainingTimeReminders(ExamModeSettings settings, DateTime now, ExamModeServer server)
    {
        if (!settings.RemainingNotificationEnabled || _lessons.CurrentState != TimeState.OnClass)
        {
            return;
        }

        var item = _lessons.CurrentTimeLayoutItem;
        var subjectName = NormalizeSubjectName(_lessons.CurrentSubject?.Name, "本场考试");
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

    private void CheckAudioAutoPlay(ExamModeSettings settings, DateTime now, ExamModeServer server)
    {
        if (!settings.AutoPlayEnabled || string.IsNullOrWhiteSpace(settings.AudioPath) ||
            !File.Exists(settings.AudioPath))
        {
            return;
        }

        var offset = Math.Clamp(settings.AutoPlayOffsetMinutes, -60, 60);
        var isClassTime = _lessons.CurrentState == TimeState.OnClass;
        if ((offset < 0 && isClassTime) || (offset >= 0 && !isClassTime))
        {
            return;
        }

        var item = isClassTime ? _lessons.CurrentTimeLayoutItem : _lessons.NextClassTimeLayoutItem;
        var subjectName = isClassTime
            ? NormalizeSubjectName(_lessons.CurrentSubject?.Name, "当前考试")
            : NormalizeSubjectName(_lessons.NextClassSubject?.Name, "下一场考试");
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
            ExpiresAtUnixSeconds = DateTimeOffset.Now.AddMinutes(2).ToUnixTimeSeconds()
        });
        Logger.Info($"[ExamModeScheduler] 听力自动播放命令已下发: {sessionId}, seq={sequence}");
    }

    private void CheckExamEnd(ExamModeSettings settings, DateTime now, ExamModeServer server)
    {
        var state = _lessons.CurrentState;
        if (_previousState != TimeState.OnClass || state == TimeState.OnClass)
        {
            if (state == TimeState.OnClass)
            {
                var item = _lessons.CurrentTimeLayoutItem;
                var name = NormalizeSubjectName(_lessons.CurrentSubject?.Name, "当前考试");
                _lastClassSessionId = BuildSessionId(now.Date + item.StartTime, name);
            }
            return;
        }

        if (!settings.EndNotificationEnabled)
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
