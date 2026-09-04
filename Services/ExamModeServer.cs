using System.Diagnostics;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ClassIsland.AISmartClass.Models.ExamMode;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared.Enums;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 考试模式 HTTP 服务器。在本地端口上提供 REST API 和仪表盘 HTML，
/// 浏览器打开后可显示考试倒计时、当前科目等信息。
/// </summary>
public class ExamModeServer : IDisposable
{
    private static readonly object InstanceLock = new();
    private static ExamModeServer? _instance;
    private const int MaxPort = 9895;
    private HttpListener? _listener;
    private readonly int _startPort;
    private int _port;
    private CancellationTokenSource? _cts;
    private readonly object _audioCommandLock = new();
    private readonly object _audioSessionLock = new();
    private readonly object _reminderHistoryLock = new();
    private readonly List<ExamReminderHistoryEntry> _reminderHistory = new();
    private readonly Dictionary<string, bool> _audioSessionChoices = new();
    private readonly object _timeCalibrationLock = new();
    private static readonly string[] TimeCalibrationServers =
    {
        "ntp.aliyun.com",
        "time.windows.com"
    };
    private ExamAudioCommand? _audioCommand;
    private TimeCalibrationResult _timeCalibration = TimeCalibrationResult.Pending;

    public bool IsRunning { get; private set; }
    public string Url => $"http://localhost:{_port}";
    public bool Enabled { get; set; } = true;

    public static ExamModeServer GetOrCreate(int port = 9876)
    {
        lock (InstanceLock)
        {
            return _instance ??= new ExamModeServer(port);
        }
    }

    public ExamModeServer(int port = 9876)
    {
        _startPort = port;
        _port = port;
    }

    public void SetAudioCommand(ExamAudioCommand command)
    {
        lock (_audioCommandLock)
        {
            _audioCommand = command;
        }
    }

    public bool? GetAudioSessionChoice(string sessionId)
    {
        lock (_audioSessionLock)
        {
            return _audioSessionChoices.TryGetValue(sessionId, out var hasAudio) ? hasAudio : null;
        }
    }

    private void SetAudioSessionChoice(string sessionId, bool hasAudio)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        lock (_audioSessionLock)
        {
            _audioSessionChoices[sessionId] = hasAudio;
        }
    }

    public void AddReminderHistory(string type, string title, string body, DateTime occurredAt)
    {
        lock (_reminderHistoryLock)
        {
            _reminderHistory.Insert(0, new ExamReminderHistoryEntry(type, title, body, occurredAt));
            if (_reminderHistory.Count > 20)
            {
                _reminderHistory.RemoveRange(20, _reminderHistory.Count - 20);
            }
        }
    }

    private ExamReminderHistoryEntry[] GetReminderHistory()
    {
        lock (_reminderHistoryLock)
        {
            return _reminderHistory.ToArray();
        }
    }

    private ExamAudioCommand? GetAudioCommand()
    {
        lock (_audioCommandLock)
        {
            if (_audioCommand is not { } command)
            {
                return null;
            }

            if (command.ExpiresAtUnixSeconds > 0 &&
                DateTimeOffset.Now.ToUnixTimeSeconds() > command.ExpiresAtUnixSeconds)
            {
                _audioCommand = null;
                return null;
            }

            return command;
        }
    }

    private void ClearAudioCommand()
    {
        lock (_audioCommandLock)
        {
            _audioCommand = null;
        }
    }

    private void ClearAudioCommandForSession(string sessionId)
    {
        lock (_audioCommandLock)
        {
            if (string.Equals(_audioCommand?.SessionId, sessionId, StringComparison.Ordinal))
            {
                _audioCommand = null;
            }
        }
    }

    public Task StartAsync()
    {
        if (IsRunning) return Task.CompletedTask;
        if (!Enabled) { Logger.Info("[ExamMode] 服务器未启用（EnableExamModeLocalServer=false）"); return Task.CompletedTask; }

        Exception? lastException = null;
        for (var port = _startPort; port <= MaxPort; port++)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");

            try
            {
                listener.Start();
                _listener = listener;
                _port = port;
                _cts = new CancellationTokenSource();
                IsRunning = true;
                _ = Task.Run(() => ListenLoop(_cts.Token));
                _ = Task.Run(() => CheckTimeCalibrationAsync(_cts.Token));
                Logger.Info($"[ExamMode] 服务器已启动: {Url}");

                // 通知 AI 服务进入考试模式，自动切换严肃语气
                var ai = Plugin.GetAIService();
                if (ai != null) ai.IsInExam = true;
                Logger.Info("[ExamMode] AI 语气已切换为严肃模式");

                return Task.CompletedTask;
            }
            catch (Exception ex) when (ex is HttpListenerException or InvalidOperationException)
            {
                lastException = ex;
                try { listener.Close(); } catch { }
                Logger.Info($"[ExamMode] 端口 {port} 启动失败，尝试下一个端口: {ex}");
            }
        }

        throw new InvalidOperationException($"考试模式服务器启动失败：{_startPort}-{MaxPort} 端口均不可用。", lastException);
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _listener = null;
        _cts?.Dispose();
        _cts = null;
        ClearAudioCommand();
        lock (_audioSessionLock)
        {
            _audioSessionChoices.Clear();
        }
        lock (_timeCalibrationLock)
        {
            _timeCalibration = TimeCalibrationResult.Pending;
        }
        IsRunning = false;
        Logger.Info("[ExamMode] 服务器已停止");

        // 通知 AI 服务退出考试模式，恢复用户偏好的语气
        var ai = Plugin.GetAIService();
        if (ai != null) ai.IsInExam = false;
        Logger.Info("[ExamMode] AI 语气已恢复正常");
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var listener = _listener;
                if (listener == null) break;

                var getContextTask = listener.GetContextAsync();
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(5000);

                var completedTask = await Task.WhenAny(getContextTask, Task.Delay(-1, timeoutCts.Token));
                if (completedTask != getContextTask) continue;

                var ctx = await getContextTask;
                _ = Task.Run(() => HandleRequest(ctx), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) { break; }
            catch (Exception ex)
            {
                Logger.Info($"[ExamMode] 请求处理异常: {ex}");
            }
        }
    }

    private async Task HandleRequest(HttpListenerContext ctx)
    {
        var path = ctx.Request.Url?.AbsolutePath ?? "/";
        Logger.Info($"[ExamMode] {ctx.Request.HttpMethod} {path}");

        if (path == "/" || path == "/index.html")
        {
            ServeHtml(ctx);
            return;
        }

        if (path == "/api/state")
        {
            ServeApiState(ctx);
            return;
        }

        if (path == "/api/settings")
        {
            if (ctx.Request.HttpMethod == "GET")
                ServeExamSettings(ctx);
            else if (ctx.Request.HttpMethod == "POST")
                await SaveExamSettingsAsync(ctx);
            else
                CloseWithStatus(ctx, 405);
            return;
        }

        if (path == "/api/audio/select-file" && ctx.Request.HttpMethod == "POST")
        {
            await SelectAudioFileAsync(ctx);
            return;
        }

        if (path == "/api/audio/session" && ctx.Request.HttpMethod == "POST")
        {
            await SaveAudioSessionChoiceAsync(ctx);
            return;
        }

        if (path == "/api/audio/reset" && ctx.Request.HttpMethod == "POST")
        {
            ResetAudioFile(ctx);
            return;
        }

        if (path == "/api/audio/file" && ctx.Request.HttpMethod == "GET")
        {
            await ServeAudioFileAsync(ctx);
            return;
        }

        if (path == "/api/time/open-settings" && ctx.Request.HttpMethod == "POST")
        {
            OpenTimeSettings(ctx);
            return;
        }

        ctx.Response.StatusCode = 404;
        ctx.Response.Close();
    }

    private void ServeHtml(HttpListenerContext ctx)
    {
        var html = GetDashboardHtml();
        var bytes = Encoding.UTF8.GetBytes(html);
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.Close();
    }

    private void ServeApiState(HttpListenerContext ctx)
    {
        try
        {
            var state = GetExamState();
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }
        catch (Exception ex)
        {
            Logger.Error($"[ExamMode] 获取考试状态失败: {ex}");
            try { WriteJson(ctx, new { error = "获取考试状态失败", debug = ex.Message }, 500); } catch { }
        }
    }

    private object GetExamState()
    {
        var lessons = Plugin.LessonsService;
        var profile = Plugin.ProfileService;

        if (lessons == null || profile == null)
        {
            return new
            {
                error = "ClassIsland 服务未就绪",
                state = "Loading",
                stateText = "服务未就绪",
                phase = "loading",
                isClassTime = false,
                isBreaking = false,
                isPrepareOnClass = false,
                isAfterSchool = false,
                remainingSeconds = 0,
                totalSeconds = 0,
                currentStart = "--:--",
                currentEnd = "--:--",
                nextStart = "--:--",
                debug = "ProfileService 或 LessonsService 仍为 null",
                serverUnixSeconds = DateTimeOffset.Now.ToUnixTimeSeconds()
            };
        }

        var now = DateTime.Now;
        var nowTime = now.TimeOfDay;
        var currentState = lessons.CurrentState;
        var currentSubject = lessons.CurrentSubject;
        var nextSubject = lessons.NextClassSubject;
        var currentItem = lessons.CurrentTimeLayoutItem;
        var nextClassItem = lessons.NextClassTimeLayoutItem;

        var isClassTime = currentState == TimeState.OnClass;
        var isPrepareOnClass = currentState == TimeState.PrepareOnClass;
        var isBreaking = currentState == TimeState.Breaking;
        var isAfterSchool = currentState == TimeState.AfterSchool;

        // ClassIsland API 语义：上课/考试中，OnBreakingTimeLeftTime 是距下课/考试结束；非上课时，OnClassLeftTime 是距下一场开始。
        var apiRemainingTime = isClassTime ? lessons.OnBreakingTimeLeftTime : lessons.OnClassLeftTime;
        var remainingTime = apiRemainingTime;
        var fallbackUsed = false;

        if (remainingTime <= TimeSpan.Zero)
        {
            remainingTime = isClassTime
                ? currentItem.EndTime - nowTime
                : nextClassItem.StartTime - nowTime;
            fallbackUsed = true;
        }
        if (remainingTime < TimeSpan.Zero)
            remainingTime = TimeSpan.Zero;

        var currentStartTime = currentItem.StartTime;
        var currentEndTime = currentItem.EndTime;
        var nextStartTime = nextClassItem.StartTime;
        var nextEndTime = nextClassItem.EndTime;

        var totalSeconds = 0;
        if (isClassTime && currentItem.TimeType == 0)
        {
            var duration = currentEndTime - currentStartTime;
            if (duration <= TimeSpan.Zero)
                duration = currentItem.Last;
            totalSeconds = Math.Max(0, (int)duration.TotalSeconds);
        }

        var remainingSeconds = Math.Max(0, (int)remainingTime.TotalSeconds);
        var elapsedSeconds = totalSeconds > 0 ? Math.Clamp(totalSeconds - remainingSeconds, 0, totalSeconds) : 0;
        var progressPercent = totalSeconds > 0 ? Math.Clamp(elapsedSeconds * 100.0 / totalSeconds, 0, 100) : 0;

        var stateText = currentState switch
        {
            TimeState.OnClass => "考试中",
            TimeState.PrepareOnClass => "准备上课",
            TimeState.Breaking => "课间 / 考间休息",
            TimeState.AfterSchool => "已放学",
            _ => "等待课程"
        };

        var phase = currentState switch
        {
            TimeState.OnClass => "exam",
            TimeState.PrepareOnClass => "prepare",
            TimeState.Breaking => "break",
            TimeState.AfterSchool => "afterSchool",
            _ => "idle"
        };

        var subjectName = currentState switch
        {
            TimeState.OnClass => currentSubject?.Name ?? "当前考试",
            TimeState.PrepareOnClass => nextSubject?.Name ?? "即将开始",
            TimeState.Breaking => "考间休息",
            TimeState.AfterSchool => "考试结束",
            _ => currentSubject?.Name ?? nextSubject?.Name ?? "等待课程"
        };

        var rawNextName = nextSubject?.Name ?? "";
        var nextSubjectName = string.IsNullOrWhiteSpace(rawNextName) || rawNextName == "???"
            ? ""
            : rawNextName;
        var timerLabel = currentState switch
        {
            TimeState.OnClass => remainingSeconds <= 300 ? "即将结束" : "距离考试结束",
            TimeState.PrepareOnClass => "距离考试开始",
            TimeState.Breaking => "距离下一场开始",
            TimeState.AfterSchool => "今日已结束",
            _ => "等待同步"
        };

        var currentSessionId = $"{(now.Date + currentStartTime):yyyyMMdd-HHmm}:{subjectName}";
        var nextSessionId = $"{(now.Date + nextStartTime):yyyyMMdd-HHmm}:{nextSubjectName}";
        var sessionId = isClassTime ? currentSessionId : "not-in-exam";
        var hasNextExam = !string.IsNullOrWhiteSpace(nextSubjectName);
        var examTargetSessionId = isClassTime ? currentSessionId : hasNextExam ? nextSessionId : "";
        var audioTargetSessionId = examTargetSessionId;
        var audioSessionChoice = GetAudioSessionChoice(audioTargetSessionId);
        var isEnglishExam = IsEnglishSubject(isClassTime ? subjectName : nextSubjectName);
        var shouldShowPreExamPrompts = !isClassTime && hasNextExam && remainingSeconds is > 0 and <= 300;
        TimeCalibrationResult timeCalibration;
        lock (_timeCalibrationLock)
        {
            timeCalibration = _timeCalibration;
        }

        var reminderTimeline = CreateReminderTimeline(
            now,
            isClassTime,
            subjectName,
            nextSubjectName,
            currentStartTime,
            currentEndTime,
            nextStartTime);
        var nextReminder = reminderTimeline.FirstOrDefault();

        var debug = string.Join(" | ", new[]
        {
            $"state={currentState}",
            $"timeType={currentItem.TimeType}",
            $"apiRemaining={(int)apiRemainingTime.TotalSeconds}s",
            $"remaining={remainingSeconds}s",
            $"fallback={fallbackUsed}",
            $"current={FormatClock(currentStartTime)}-{FormatClock(currentEndTime)}",
            $"nextStart={FormatClock(nextStartTime)}",
            $"timerRunning={lessons.IsTimerRunning}"
        });

        return new
        {
            state = currentState.ToString(),
            stateText,
            phase,
            subjectName,
            subtitle = stateText,
            timerLabel,
            isClassTime,
            isBreaking,
            isPrepareOnClass,
            isAfterSchool,
            remainingSeconds,
            totalSeconds,
            elapsedSeconds,
            progressPercent,
            currentStart = FormatClock(currentStartTime),
            currentEnd = FormatClock(currentEndTime),
            nextStart = FormatClock(nextStartTime),
            nextEnd = FormatClock(nextEndTime),
            nextSubjectName,
            currentState = currentState.ToString(),
            isTimerRunning = lessons.IsTimerRunning,
            startTime = FormatClock(isClassTime ? currentStartTime : nextStartTime),
            endTime = FormatClock(isClassTime ? currentEndTime : nextEndTime),
            serverUnixSeconds = DateTimeOffset.Now.ToUnixTimeSeconds(),
            serverTime = now.ToString("HH:mm:ss"),
            serverDate = now.ToString("yyyy-MM-dd"),
            dayOfWeek = GetChineseDayOfWeek(now.DayOfWeek),
            sessionId,
            examTargetSessionId,
            audioTargetSessionId,
            audioSessionChoice,
            isEnglishExam,
            shouldAskListening = shouldShowPreExamPrompts && isEnglishExam && audioSessionChoice == null,
            shouldAskExamInfo = shouldShowPreExamPrompts,
            timeCalibration = new
            {
                timeCalibration.Status,
                timeCalibration.OffsetSeconds,
                timeCalibration.UncertaintySeconds,
                needsCalibration = timeCalibration.Status == "completed" && Math.Abs(timeCalibration.OffsetSeconds) > 30,
                checkedAt = timeCalibration.CheckedAt?.ToString("HH:mm:ss"),
                timeCalibration.Error
            },
            examSettings = CreateExamSettingsDto(),
            audioCommand = GetAudioCommand(),
            nextReminder,
            reminderTimeline,
            reminderHistory = GetReminderHistory(),
            diagnostics = new
            {
                classIslandConnected = true,
                localServer = $"localhost:{_port}",
                lastSync = now.ToString("HH:mm:ss"),
                lessons.IsTimerRunning
            },
            debug
        };
    }

    private object[] CreateReminderTimeline(
        DateTime now,
        bool isClassTime,
        string subjectName,
        string nextSubjectName,
        TimeSpan currentStart,
        TimeSpan currentEnd,
        TimeSpan nextStart)
    {
        var settings = Plugin.GetAISettings()?.ExamMode ?? new ExamModeSettings();
        var entries = new List<(DateTime Trigger, string Type, string Label)>();
        if (isClassTime)
        {
            var end = now.Date + currentEnd;
            if (settings.RemainingNotificationEnabled)
            {
                entries.AddRange(settings.RemainingMinutes
                    .Where(m => m is > 0 and <= 120)
                    .Distinct()
                    .Select(minute => (end.AddMinutes(-minute), "remaining", $"{subjectName}剩余 {minute} 分钟")));
            }
            if (settings.EndNotificationEnabled)
            {
                entries.Add((end, "end", $"{subjectName}考试结束"));
            }
        }
        else
        {
            var start = now.Date + nextStart;
            var nextName = string.IsNullOrWhiteSpace(nextSubjectName) ? "下一场考试" : nextSubjectName;
            if (settings.ReminderEnabled)
            {
                entries.AddRange(settings.ReminderMinutes
                    .Where(m => m is > 0 and <= 120)
                    .Distinct()
                    .Select(minute => (start.AddMinutes(-minute), "pre", $"距{nextName}开始 {minute} 分钟")));
            }
            if (settings.StartNotificationEnabled)
            {
                entries.Add((start, "start", $"{nextName}考试开始"));
            }
        }

        return entries
            .Where(entry => entry.Trigger >= now.AddSeconds(-2))
            .OrderBy(entry => entry.Trigger)
            .Take(8)
            .Select(entry => (object)new
            {
                time = entry.Trigger.ToString("HH:mm"),
                unixSeconds = new DateTimeOffset(entry.Trigger).ToUnixTimeSeconds(),
                entry.Type,
                entry.Label
            })
            .ToArray();
    }

    private object CreateExamSettingsDto()
    {
        var settings = Plugin.GetAISettings()?.ExamMode ?? new ExamModeSettings();
        return new
        {
            settings.EnableScheduler,
            settings.ReminderPreset,
            settings.ReminderEnabled,
            reminderMinutes = settings.ReminderMinutes,
            settings.StartNotificationEnabled,
            settings.RemainingNotificationEnabled,
            remainingMinutes = settings.RemainingMinutes,
            settings.EndNotificationEnabled,
            settings.SuppressOtherNotifications,
            audioPath = settings.AudioPath,
            audioFileName = string.IsNullOrWhiteSpace(settings.AudioPath)
                ? ""
                : Path.GetFileName(settings.AudioPath),
            settings.AutoPlayEnabled,
            settings.AutoPlayOffsetMinutes,
            audioVolume = Math.Clamp(settings.AudioVolume, 0, 1)
        };
    }

    private void ServeExamSettings(HttpListenerContext ctx)
    {
        WriteJson(ctx, CreateExamSettingsDto());
    }

    private async Task SaveExamSettingsAsync(HttpListenerContext ctx)
    {
        try
        {
            using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8);
            var json = await reader.ReadToEndAsync();
            var request = JsonSerializer.Deserialize<ExamSettingsUpdateRequest>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (request == null)
            {
                CloseWithStatus(ctx, 400);
                return;
            }

            var aiSettings = Plugin.GetAISettings();
            if (aiSettings == null)
            {
                WriteJson(ctx, new { error = "AIIsland 设置尚未就绪" }, 503);
                return;
            }

            var settings = aiSettings.ExamMode;
            settings.EnableScheduler = request.EnableScheduler;
            settings.ReminderPreset = NormalizeReminderPreset(request.ReminderPreset);
            settings.ReminderEnabled = request.ReminderEnabled;
            settings.ReminderMinutes = NormalizeReminderMinutes(request.ReminderMinutes);
            settings.StartNotificationEnabled = request.StartNotificationEnabled;
            settings.RemainingNotificationEnabled = request.RemainingNotificationEnabled;
            settings.RemainingMinutes = NormalizeReminderMinutes(request.RemainingMinutes);
            settings.EndNotificationEnabled = request.EndNotificationEnabled;
            settings.SuppressOtherNotifications = request.SuppressOtherNotifications;
            settings.AutoPlayEnabled = request.AutoPlayEnabled;
            settings.AutoPlayOffsetMinutes = Math.Clamp(request.AutoPlayOffsetMinutes, -60, 60);
            settings.AudioVolume = Math.Clamp(request.AudioVolume, 0, 1);
            Plugin.SaveAISettingsFile(aiSettings);
            Plugin.SyncAISettings(aiSettings);
            WriteJson(ctx, CreateExamSettingsDto());
        }
        catch (JsonException ex)
        {
            Logger.Info($"[ExamMode] 保存考试设置 JSON 无效: {ex.Message}");
            WriteJson(ctx, new { error = "设置格式无效" }, 400);
        }
        catch (Exception ex)
        {
            Logger.Error($"[ExamMode] 保存考试设置失败: {ex}");
            WriteJson(ctx, new { error = "保存考试设置失败" }, 500);
        }
    }

    private async Task SelectAudioFileAsync(HttpListenerContext ctx)
    {
        try
        {
            var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    var topLevel = AppBase.Current?.MainWindow;
                    if (topLevel == null)
                    {
                        completion.TrySetResult(null);
                        return;
                    }

                    var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = "选择考试听力文件",
                        AllowMultiple = false,
                        FileTypeFilter = new[]
                        {
                            new FilePickerFileType("音频文件")
                            {
                                Patterns = new[] { "*.mp3", "*.wav", "*.flac", "*.ogg", "*.m4a", "*.aac" }
                            }
                        }
                    });
                    completion.TrySetResult(files.Count == 0 ? null : files[0].TryGetLocalPath());
                }
                catch (Exception ex)
                {
                    Logger.Error($"[ExamMode] 选择听力文件失败: {ex}");
                    completion.TrySetException(ex);
                }
            });

            var path = await completion.Task;
            if (string.IsNullOrWhiteSpace(path))
            {
                WriteJson(ctx, new { canceled = true });
                return;
            }

            var aiSettings = Plugin.GetAISettings();
            if (aiSettings == null)
            {
                WriteJson(ctx, new { error = "AIIsland 设置尚未就绪" }, 503);
                return;
            }

            aiSettings.ExamMode.AudioPath = Path.GetFullPath(path);
            Plugin.SaveAISettingsFile(aiSettings);
            Plugin.SyncAISettings(aiSettings);
            WriteJson(ctx, CreateExamSettingsDto());
        }
        catch (Exception ex)
        {
            Logger.Error($"[ExamMode] 选择听力文件接口失败: {ex}");
            WriteJson(ctx, new { error = "选择听力文件失败" }, 500);
        }
    }

    private async Task SaveAudioSessionChoiceAsync(HttpListenerContext ctx)
    {
        try
        {
            using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8);
            var request = JsonSerializer.Deserialize<AudioSessionChoiceRequest>(await reader.ReadToEndAsync(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (request == null || string.IsNullOrWhiteSpace(request.SessionId))
            {
                WriteJson(ctx, new { error = "考试场次无效" }, 400);
                return;
            }

            SetAudioSessionChoice(request.SessionId, request.HasAudio);
            if (!request.HasAudio)
            {
                ClearAudioCommandForSession(request.SessionId);
            }
            WriteJson(ctx, new { request.SessionId, request.HasAudio });
        }
        catch (JsonException)
        {
            WriteJson(ctx, new { error = "设置格式无效" }, 400);
        }
        catch (Exception ex)
        {
            Logger.Error($"[ExamMode] 保存本场听力状态失败: {ex}");
            WriteJson(ctx, new { error = "保存本场听力状态失败" }, 500);
        }
    }

    private void ResetAudioFile(HttpListenerContext ctx)
    {
        try
        {
            var aiSettings = Plugin.GetAISettings();
            if (aiSettings == null)
            {
                WriteJson(ctx, new { error = "AIIsland 设置尚未就绪" }, 503);
                return;
            }

            aiSettings.ExamMode.AudioPath = "";
            aiSettings.ExamMode.AutoPlayEnabled = false;
            Plugin.SaveAISettingsFile(aiSettings);
            Plugin.SyncAISettings(aiSettings);
            ClearAudioCommand();
            WriteJson(ctx, CreateExamSettingsDto());
        }
        catch (Exception ex)
        {
            Logger.Error($"[ExamMode] 清除听力文件失败: {ex}");
            WriteJson(ctx, new { error = "清除听力文件失败" }, 500);
        }
    }

    private async Task ServeAudioFileAsync(HttpListenerContext ctx)
    {
        var path = Plugin.GetAISettings()?.ExamMode.AudioPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            CloseWithStatus(ctx, 404);
            return;
        }

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var length = stream.Length;
            long start = 0;
            long end = length - 1;
            var range = ctx.Request.Headers["Range"];
            if (!string.IsNullOrWhiteSpace(range) && range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            {
                var parts = range[6..].Split('-', 2);
                if (long.TryParse(parts[0], out var parsedStart)) start = Math.Clamp(parsedStart, 0, length - 1);
                if (parts.Length > 1 && long.TryParse(parts[1], out var parsedEnd)) end = Math.Clamp(parsedEnd, start, length - 1);
                ctx.Response.StatusCode = 206;
                ctx.Response.AddHeader("Content-Range", $"bytes {start}-{end}/{length}");
            }

            var count = end - start + 1;
            ctx.Response.ContentType = GetAudioContentType(path);
            ctx.Response.AddHeader("Accept-Ranges", "bytes");
            ctx.Response.ContentLength64 = count;
            stream.Position = start;

            var buffer = new byte[64 * 1024];
            while (count > 0)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, count)));
                if (read <= 0) break;
                await ctx.Response.OutputStream.WriteAsync(buffer.AsMemory(0, read));
                count -= read;
            }
        }
        catch (HttpListenerException)
        {
            // 浏览器暂停或拖动进度时会主动中断旧连接。
        }
        catch (Exception ex)
        {
            Logger.Error($"[ExamMode] 输出听力文件失败: {ex}");
        }
        finally
        {
            try { ctx.Response.Close(); } catch { }
        }
    }

    private static string GetAudioContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".flac" => "audio/flac",
            ".ogg" => "audio/ogg",
            ".m4a" => "audio/mp4",
            ".aac" => "audio/aac",
            _ => "application/octet-stream"
        };
    }

    private async Task CheckTimeCalibrationAsync(CancellationToken cancellationToken)
    {
        lock (_timeCalibrationLock)
        {
            _timeCalibration = new TimeCalibrationResult("checking", 0, 0, DateTime.Now, null);
        }

        try
        {
            Exception? lastException = null;
            foreach (var server in TimeCalibrationServers)
            {
                try
                {
                    var result = await QueryNtpOffsetAsync(server, cancellationToken);
                    lock (_timeCalibrationLock)
                    {
                        _timeCalibration = result;
                    }
                    Logger.Info($"[ExamMode] 时间校准完成（{server}），系统偏差 {result.OffsetSeconds:+0.0;-0.0;0.0} 秒");
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    Logger.Info($"[ExamMode] 时间源 {server} 校准失败，尝试下一个来源: {ex.Message}");
                }
            }

            throw new InvalidOperationException("所有时间源均不可用", lastException);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            lock (_timeCalibrationLock)
            {
                _timeCalibration = new TimeCalibrationResult("failed", 0, 0, DateTime.Now, ex.Message);
            }
            Logger.Info($"[ExamMode] 时间校准失败，不影响考试模式: {ex.Message}");
        }
    }

    private static async Task<TimeCalibrationResult> QueryNtpOffsetAsync(
        string server,
        CancellationToken cancellationToken)
    {
        const long ntpEpochOffsetSeconds = 2_208_988_800L;
        var request = new byte[48];
        request[0] = 0x1B;
        var addresses = await Dns.GetHostAddressesAsync(server, cancellationToken);
        var endpoint = new IPEndPoint(
            addresses.First(address => address.AddressFamily == AddressFamily.InterNetwork),
            123);
        var requestStarted = DateTimeOffset.UtcNow;
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        await udp.SendAsync(request, endpoint, cancellationToken);
        var response = await udp.ReceiveAsync(cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        var responseReceived = DateTimeOffset.UtcNow;
        if (response.Buffer.Length < 48)
        {
            throw new FormatException($"{server} 返回的数据长度无效");
        }

        var seconds = ReadUInt32BigEndian(response.Buffer, 40);
        var fraction = ReadUInt32BigEndian(response.Buffer, 44);
        var unixSeconds = seconds - ntpEpochOffsetSeconds + fraction / 4_294_967_296d;
        var serverAtTransmit = DateTimeOffset.FromUnixTimeMilliseconds(
            checked((long)Math.Round(unixSeconds * 1000d)));
        var roundTrip = responseReceived - requestStarted;
        var estimatedServerAtReceive = serverAtTransmit.AddTicks(roundTrip.Ticks / 2);
        // 正数表示本机系统时间快于标准时间，负数表示本机慢。
        var offsetSeconds = (responseReceived - estimatedServerAtReceive).TotalSeconds;
        return new TimeCalibrationResult(
            "completed",
            Math.Round(offsetSeconds, 1),
            Math.Round(roundTrip.TotalSeconds / 2, 1),
            DateTime.Now,
            null);
    }

    private static uint ReadUInt32BigEndian(byte[] buffer, int offset)
    {
        return ((uint)buffer[offset] << 24) |
               ((uint)buffer[offset + 1] << 16) |
               ((uint)buffer[offset + 2] << 8) |
               buffer[offset + 3];
    }

    private static void OpenTimeSettings(HttpListenerContext ctx)
    {
        try
        {
            var navigationService = ClassIsland.Shared.IAppHost.TryGetService<IUriNavigationService>();
            if (navigationService != null)
            {
                navigationService.NavigateWrapped(new Uri("classisland://app/settings/clock"), out var exception);
                if (exception == null)
                {
                    WriteJson(ctx, new { opened = true, target = "classisland-clock-settings" });
                    return;
                }

                Logger.Info($"[ExamMode] 打开 ClassIsland 时钟设置失败，改用系统设置: {exception.Message}");
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:dateandtime",
                UseShellExecute = true
            });
            WriteJson(ctx, new { opened = true, target = "windows-date-time" });
        }
        catch (Exception ex)
        {
            Logger.Error($"[ExamMode] 打开时间校准设置失败: {ex}");
            WriteJson(ctx, new { error = "无法打开时间校准设置" }, 500);
        }
    }

    private static void WriteJson(HttpListenerContext ctx, object value, int statusCode = 200)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.Close();
    }

    private static void CloseWithStatus(HttpListenerContext ctx, int statusCode)
    {
        ctx.Response.StatusCode = statusCode;
        ctx.Response.Close();
    }

    private static List<int> NormalizeReminderMinutes(IEnumerable<int>? minutes)
    {
        return (minutes ?? Array.Empty<int>())
            .Where(m => m is > 0 and <= 120)
            .Distinct()
            .OrderBy(m => m)
            .ToList();
    }

    private static string NormalizeReminderPreset(string? preset)
    {
        return preset is "compact" or "standard" or "full" or "custom"
            ? preset
            : "custom";
    }

    private sealed record TimeCalibrationResult(
        string Status,
        double OffsetSeconds,
        double UncertaintySeconds,
        DateTime? CheckedAt,
        string? Error)
    {
        public static TimeCalibrationResult Pending { get; } = new("pending", 0, 0, null, null);
    }

    private sealed record ExamReminderHistoryEntry(
        string Type,
        string Title,
        string Body,
        DateTime OccurredAt)
    {
        public string Time => OccurredAt.ToString("HH:mm:ss");
    }

    private sealed class AudioSessionChoiceRequest
    {
        public string SessionId { get; set; } = "";
        public bool HasAudio { get; set; }
    }

    private sealed class ExamSettingsUpdateRequest
    {
        public bool EnableScheduler { get; set; } = true;
        public string ReminderPreset { get; set; } = "standard";
        public bool ReminderEnabled { get; set; } = true;
        public List<int> ReminderMinutes { get; set; } = new();
        public bool StartNotificationEnabled { get; set; } = true;
        public bool RemainingNotificationEnabled { get; set; } = true;
        public List<int> RemainingMinutes { get; set; } = new();
        public bool EndNotificationEnabled { get; set; } = true;
        public bool SuppressOtherNotifications { get; set; }
        public bool AutoPlayEnabled { get; set; }
        public int AutoPlayOffsetMinutes { get; set; }
        public double AudioVolume { get; set; } = 0.8;
    }

    private static bool IsEnglishSubject(string? subjectName)
    {
        if (string.IsNullOrWhiteSpace(subjectName)) return false;
        var name = subjectName.Trim();
        return name.Contains("英语", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("英文", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("English", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatClock(TimeSpan time)
    {
        return $"{time.Hours:D2}:{time.Minutes:D2}";
    }

    private static string GetChineseDayOfWeek(DayOfWeek dayOfWeek)
    {
        return dayOfWeek switch
        {
            DayOfWeek.Monday => "星期一",
            DayOfWeek.Tuesday => "星期二",
            DayOfWeek.Wednesday => "星期三",
            DayOfWeek.Thursday => "星期四",
            DayOfWeek.Friday => "星期五",
            DayOfWeek.Saturday => "星期六",
            DayOfWeek.Sunday => "星期日",
            _ => ""
        };
    }

    private string? _cachedHtml;

    private string GetDashboardHtml()
    {
        if (_cachedHtml != null) return _cachedHtml;

        var assemblyDir = Path.GetDirectoryName(typeof(ExamModeServer).Assembly.Location)
            ?? AppContext.BaseDirectory;
        var path = Path.Combine(assemblyDir, "Data", "ExamModeDashboard.html");

        if (!File.Exists(path))
        {
            Logger.Error($"考试模式仪表盘 HTML 资源缺失: {path}");
            return "<html><body>考试模式仪表盘资源缺失，请检查 Data/ExamModeDashboard.html 是否存在。</body></html>";
        }

        _cachedHtml = File.ReadAllText(path, Encoding.UTF8);
        return _cachedHtml;
    }

    public void Dispose()
    {
        Stop();
        lock (InstanceLock)
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
        try { _listener?.Close(); } catch { }
        _cts?.Dispose();
    }
}
