using System.Net;
using System.Text;
using System.Text.Json;
using ClassIsland.Shared.Enums;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 考试模式 HTTP 服务器。在本地端口上提供 REST API 和仪表盘 HTML，
/// 浏览器打开后可显示考试倒计时、当前科目等信息。
/// </summary>
public class ExamModeServer : IDisposable
{
    private static ExamModeServer? _instance;
    private const int MaxPort = 9895;
    private HttpListener? _listener;
    private readonly int _startPort;
    private int _port;
    private CancellationTokenSource? _cts;

    public bool IsRunning { get; private set; }
    public string Url => $"http://localhost:{_port}";
    public bool Enabled { get; set; } = true;

    public static ExamModeServer GetOrCreate(int port = 9876)
    {
        if (_instance != null) return _instance;
        _instance = new ExamModeServer(port);
        return _instance;
    }

    public ExamModeServer(int port = 9876)
    {
        _startPort = port;
        _port = port;
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

        var nextSubjectName = nextSubject?.Name ?? "";
        var timerLabel = currentState switch
        {
            TimeState.OnClass => remainingSeconds <= 300 ? "即将结束" : "距离考试结束",
            TimeState.PrepareOnClass => "距离考试开始",
            TimeState.Breaking => "距离下一场开始",
            TimeState.AfterSchool => "今日已结束",
            _ => "等待同步"
        };

        var sessionId = isClassTime
            ? $"{now:yyyyMMdd}-{subjectName}-{(int)currentStartTime.TotalMinutes}"
            : "not-in-exam";

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
            debug
        };
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
        if (_instance == this) _instance = null;
        try { _listener?.Close(); } catch { }
        _cts?.Dispose();
    }
}
