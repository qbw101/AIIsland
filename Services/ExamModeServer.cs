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

    public static ExamModeServer GetOrCreate(int port = 9876)
    {
        if (_instance != null) return _instance;
        _instance = new ExamModeServer(port);
        return _instance;
    }

    public void Start()
    {
        StartAsync().GetAwaiter().GetResult();
    }

    public ExamModeServer(int port = 9876)
    {
        _startPort = port;
        _port = port;
    }

    public Task StartAsync()
    {
        if (IsRunning) return Task.CompletedTask;

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
                System.Diagnostics.Debug.WriteLine($"[ExamMode] 服务器已启动: {Url}");
                return Task.CompletedTask;
            }
            catch (Exception ex) when (ex is HttpListenerException or InvalidOperationException)
            {
                lastException = ex;
                try { listener.Close(); } catch { }
                System.Diagnostics.Debug.WriteLine($"[ExamMode] 端口 {port} 启动失败，尝试下一个端口: {ex}");
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
        System.Diagnostics.Debug.WriteLine("[ExamMode] 服务器已停止");
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
                System.Diagnostics.Debug.WriteLine($"[ExamMode] 请求处理异常: {ex}");
            }
        }
    }

    private async Task HandleRequest(HttpListenerContext ctx)
    {
        var path = ctx.Request.Url?.AbsolutePath ?? "/";
        System.Diagnostics.Debug.WriteLine($"[ExamMode] {ctx.Request.HttpMethod} {path}");

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

    private string GetDashboardHtml()
    {
        return """
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>考试模式 - AIIsland</title>
<style>
:root{--bg:#0d1117;--panel:#161b22;--panel2:#1c2128;--border:#30363d;--text:#e6edf3;--muted:#8b949e;--blue:#58a6ff;--green:#3fb950;--yellow:#d29922;--red:#f85149;--shadow:0 24px 80px rgba(0,0,0,.45)}
*{box-sizing:border-box}html,body{margin:0;min-height:100%;background:radial-gradient(circle at 50% 0,#13233a 0,#0d1117 40%,#070b10 100%);color:var(--text);font-family:"Segoe UI","Microsoft YaHei",system-ui,sans-serif;user-select:none}body{overflow:hidden}.shell{width:min(1240px,100vw);height:100vh;margin:0 auto;padding:22px;display:grid;grid-template-rows:auto 1fr auto;gap:16px}.topbar{display:flex;align-items:center;justify-content:space-between;padding:14px 18px;background:rgba(22,27,34,.86);border:1px solid var(--border);border-radius:18px;box-shadow:var(--shadow);backdrop-filter:blur(18px)}.brand{display:flex;align-items:center;gap:12px}.logo{width:36px;height:36px;border-radius:12px;background:linear-gradient(135deg,var(--blue),#8957e5);box-shadow:0 0 32px rgba(88,166,255,.28)}.brand-title{font-size:16px;font-weight:800;letter-spacing:.04em}.brand-sub{font-size:12px;color:var(--muted);margin-top:2px}.top-meta{display:flex;gap:10px;align-items:center}.pill{padding:7px 12px;border:1px solid var(--border);border-radius:999px;background:rgba(13,17,23,.65);color:var(--muted);font-size:12px}.pill.live{color:var(--green);border-color:rgba(63,185,80,.35);background:rgba(63,185,80,.08)}
.main{display:grid;grid-template-columns:1fr 370px;gap:16px;min-height:0}.hero{position:relative;display:flex;align-items:center;justify-content:center;padding:30px;background:rgba(22,27,34,.76);border:1px solid var(--border);border-radius:28px;box-shadow:var(--shadow);overflow:hidden}.hero:before{content:"";position:absolute;inset:-30%;background:radial-gradient(circle,rgba(88,166,255,.12),transparent 36%);animation:float 12s ease-in-out infinite}.hero.idle{opacity:.72}.content{position:relative;z-index:1;width:100%;text-align:center}.state-line{display:flex;justify-content:center;gap:10px;align-items:center;margin-bottom:18px}.state-dot{width:10px;height:10px;border-radius:999px;background:var(--green);box-shadow:0 0 18px var(--green)}.state-dot.warn{background:var(--yellow);box-shadow:0 0 18px var(--yellow)}.state-dot.danger{background:var(--red);box-shadow:0 0 18px var(--red)}.state-dot.muted{background:var(--muted);box-shadow:none}.state-text{font-size:14px;color:var(--muted);letter-spacing:.14em;text-transform:uppercase}.subject{font-size:clamp(42px,7vw,78px);font-weight:900;letter-spacing:-.05em;line-height:1.05;margin-bottom:8px}.subtitle{font-size:18px;color:var(--muted);margin-bottom:22px}.timer{font-variant-numeric:tabular-nums;font-size:clamp(88px,15vw,170px);font-weight:950;letter-spacing:-.08em;line-height:.94;color:var(--blue);text-shadow:0 0 42px rgba(88,166,255,.22)}.timer.warning{color:var(--yellow);text-shadow:0 0 42px rgba(210,153,34,.22)}.timer.urgent{color:var(--red);text-shadow:0 0 42px rgba(248,81,73,.22)}.timer.idle{color:#6e7681;text-shadow:none}.timer-label{margin-top:12px;color:var(--muted);font-size:15px;letter-spacing:.24em}.progress-wrap{width:min(760px,92%);margin:30px auto 0}.progress-meta{display:flex;justify-content:space-between;color:var(--muted);font-size:12px;margin-bottom:8px}.progress-bar{height:12px;border-radius:999px;background:#0d1117;border:1px solid var(--border);overflow:hidden}.progress-fill{height:100%;width:0;border-radius:999px;background:linear-gradient(90deg,var(--blue),#79c0ff);transition:width .35s linear,background .3s,opacity .3s}.progress-fill.off{opacity:.18;width:0!important}.progress-fill.warning{background:linear-gradient(90deg,var(--yellow),#e3b341)}.progress-fill.urgent{background:linear-gradient(90deg,var(--red),#ff7b72)}
.side{display:flex;flex-direction:column;gap:12px;min-height:0}.card{background:rgba(22,27,34,.86);border:1px solid var(--border);border-radius:20px;padding:16px;box-shadow:var(--shadow)}.card-title{font-size:13px;font-weight:800;color:var(--muted);letter-spacing:.12em;text-transform:uppercase;margin-bottom:12px}.info-grid{display:grid;grid-template-columns:1fr 1fr;gap:10px}.info{padding:12px;border-radius:14px;background:#0d1117;border:1px solid #21262d;min-height:70px}.info-label{font-size:11px;color:var(--muted);margin-bottom:6px}.info-value{font-size:18px;font-weight:800;word-break:break-all}.next{color:var(--blue)}.form{display:grid;gap:10px}.field{display:grid;grid-template-columns:94px 1fr;gap:10px;align-items:center}.field label{font-size:13px;color:var(--muted)}input,select{width:100%;height:34px;border:1px solid var(--border);border-radius:10px;background:#0d1117;color:var(--text);padding:0 10px;font-size:14px}.actions{display:grid;grid-template-columns:1fr 1fr 1fr;gap:8px}button,.linkbtn{height:36px;border:1px solid var(--border);border-radius:11px;background:var(--panel2);color:var(--text);font-weight:800;cursor:pointer;text-decoration:none;display:flex;align-items:center;justify-content:center}.linkbtn.primary,button.primary{background:var(--blue);border-color:var(--blue);color:#0d1117}.hint{color:var(--muted);font-size:12px;line-height:1.65}.modal-mask{position:fixed;inset:0;z-index:20;display:none;align-items:center;justify-content:center;background:rgba(1,4,9,.62);backdrop-filter:blur(10px)}.modal-mask.show{display:flex}.modal{width:min(460px,calc(100vw - 36px));padding:22px;border-radius:22px;background:rgba(22,27,34,.96);border:1px solid var(--border);box-shadow:var(--shadow)}.modal h2{margin:0 0 8px;font-size:22px}.modal p{margin:0 0 16px;color:var(--muted);font-size:13px;line-height:1.7}.modal .form{margin-bottom:16px}.modal-actions{display:grid;grid-template-columns:1fr 1fr;gap:10px}.debug{display:none;white-space:pre-wrap;word-break:break-word;margin-top:10px;padding:10px;border-radius:12px;background:#0d1117;border:1px solid var(--border);color:#79c0ff;font-size:12px;line-height:1.5}.debug.show{display:block}.footer{display:flex;justify-content:space-between;align-items:center;color:rgba(139,148,158,.65);font-size:12px;padding:0 4px}.kbd{border:1px solid var(--border);border-bottom-width:2px;border-radius:6px;padding:2px 6px;background:#161b22;color:var(--text)}@keyframes float{0%,100%{transform:translateY(0)}50%{transform:translateY(28px)}}@media(max-width:920px){body{overflow:auto}.shell{height:auto;min-height:100vh}.main{grid-template-columns:1fr}.hero{min-height:520px}.side{display:grid;grid-template-columns:1fr 1fr}.card:last-child{grid-column:1/-1}}@media(max-width:640px){.topbar{align-items:flex-start;flex-direction:column}.top-meta{flex-wrap:wrap}.side{grid-template-columns:1fr}.timer{font-size:82px}.actions{grid-template-columns:1fr}.field{grid-template-columns:1fr}}
</style>
</head>
<body>
<div class="shell">
<header class="topbar"><div class="brand"><div class="logo"></div><div><div class="brand-title">AIIsland 考试模式</div><div class="brand-sub" id="dateLine">等待同步</div></div></div><div class="top-meta"><div class="pill live" id="syncState">● 已连接</div><div class="pill" id="clock">--:--:--</div><div class="pill" id="stateName">--</div></div></header>
<main class="main">
<section class="hero" id="hero"><div class="content"><div class="state-line"><span class="state-dot" id="stateDot"></span><span class="state-text" id="phaseText">SYNCING</span></div><div class="subject" id="subject">加载中...</div><div class="subtitle" id="subtitle">正在连接 ClassIsland</div><div class="timer" id="timer">--:--:--</div><div class="timer-label" id="timerLabel">等待同步</div><div class="progress-wrap"><div class="progress-meta"><span id="startTime">--:--</span><span id="progressText">--</span><span id="endTime">--:--</span></div><div class="progress-bar"><div class="progress-fill off" id="progress"></div></div></div></div></section>
<aside class="side">
<section class="card"><div class="card-title">状态卡片</div><div class="info-grid"><div class="info"><div class="info-label">当前状态</div><div class="info-value" id="status">--</div></div><div class="info"><div class="info-label" id="nextExamLabel">下一场</div><div class="info-value next" id="nextExam">--</div></div><div class="info"><div class="info-label">试卷页数</div><div class="info-value" id="paperCard">1 页</div></div><div class="info"><div class="info-label">答题卡页数</div><div class="info-value" id="answerCard">1 页</div></div><div class="info"><div class="info-label">草稿纸状态</div><div class="info-value" id="draftCard">未使用</div></div><div class="info"><div class="info-label">服务器时间</div><div class="info-value" id="serverTime">--</div></div></div></section>
<section class="card"><div class="card-title">考试信息输入</div><div class="form"><div class="field"><label>试卷页数</label><input id="paperInput" type="number" min="1" value="1"></div><div class="field"><label>答题卡页数</label><input id="answerInput" type="number" min="1" value="1"></div><div class="field"><label>草稿纸状态</label><select id="draftInput"><option value="未使用">未使用</option><option value="已发放">已发放</option><option value="使用中">使用中</option><option value="已交回">已交回</option></select></div><div class="actions"><button id="resetBtn">重填</button><button id="fullBtn">全屏</button><button id="debugBtn">调试</button></div><a class="linkbtn primary" href="https://meeting.tencent.com/" target="_blank">打开腾讯会议</a></div></section>
<section class="card"><div class="card-title">提示</div><div class="hint">建议全屏显示。按 <span class="kbd">F11</span> 进入浏览器全屏。非考试状态会自动弱化进度条，避免课间乱跳。</div><div class="debug" id="debugBox">等待调试信息</div></section>
</aside>
</main>
<footer class="footer"><span>AIIsland Exam Dashboard</span><span id="sessionId">session: --</span></footer>
</div>
<div class="modal-mask" id="examInfoModal"><div class="modal"><h2>新考试已开始</h2><p id="modalDesc">试卷页数、答题卡页数、草稿纸状态已自动重置，请填写本场考试信息。</p><div class="form"><div class="field"><label>试卷页数</label><input id="modalPaperInput" type="number" min="1" value="1"></div><div class="field"><label>答题卡页数</label><input id="modalAnswerInput" type="number" min="1" value="1"></div><div class="field"><label>草稿纸状态</label><select id="modalDraftInput"><option value="未使用">未使用</option><option value="已发放">已发放</option><option value="使用中">使用中</option><option value="已交回">已交回</option></select></div></div><div class="modal-actions"><button id="modalLaterBtn">稍后填写</button><button class="primary" id="modalSaveBtn">保存</button></div></div></div>
<script>
let serverAnchor=0,serverRemain=0,totalSec=0,phase='idle',lastSessionId='',currentDebug='',wasClass=false;
let examInfo={paper:1,answer:1,draft:'未使用'};
const $=id=>document.getElementById(id);
function fmt(sec){sec=Math.max(0,Math.floor(sec||0));const h=Math.floor(sec/3600),m=Math.floor((sec%3600)/60),s=sec%60;return String(h).padStart(2,'0')+':'+String(m).padStart(2,'0')+':'+String(s).padStart(2,'0')}
function key(id){return 'aiisland.exam.info.'+(id||'not-in-exam')}function resetExamInfo(){examInfo={paper:1,answer:1,draft:'未使用'};renderInfo();saveInfo()}function loadInfo(){try{const v=JSON.parse(localStorage.getItem(key(lastSessionId))||'{}');examInfo={paper:v.paper||1,answer:v.answer||1,draft:v.draft||'未使用'}}catch{resetExamInfo()}renderInfo()}function saveInfo(){localStorage.setItem(key(lastSessionId),JSON.stringify(examInfo));renderInfo()}function renderInfo(){$('paperInput').value=examInfo.paper;$('answerInput').value=examInfo.answer;$('draftInput').value=examInfo.draft;$('paperCard').textContent=examInfo.paper+' 页';$('answerCard').textContent=examInfo.answer+' 页';$('draftCard').textContent=examInfo.draft;if($('modalPaperInput'))$('modalPaperInput').value=examInfo.paper;if($('modalAnswerInput'))$('modalAnswerInput').value=examInfo.answer;if($('modalDraftInput'))$('modalDraftInput').value=examInfo.draft}function openExamInfoModal(name){renderInfo();$('modalDesc').textContent='“'+(name||'本场考试')+'”已开始，试卷页数、答题卡页数、草稿纸状态已自动重置，请填写本场考试信息。';$('examInfoModal').classList.add('show');setTimeout(()=>$('modalPaperInput').focus(),80)}function closeExamInfoModal(){$('examInfoModal').classList.remove('show')}
['paperInput','answerInput','draftInput'].forEach(id=>$(id).addEventListener('input',()=>{examInfo.paper=Math.max(1,parseInt($('paperInput').value)||1);examInfo.answer=Math.max(1,parseInt($('answerInput').value)||1);examInfo.draft=$('draftInput').value;saveInfo()}));$('resetBtn').onclick=()=>{resetExamInfo();openExamInfoModal(subject.textContent)};$('fullBtn').onclick=()=>{const el=document.documentElement;if(!document.fullscreenElement)el.requestFullscreen?.();else document.exitFullscreen?.()};$('debugBtn').onclick=()=>{$('debugBox').classList.toggle('show');$('debugBox').textContent=currentDebug||'暂无调试信息'};$('modalLaterBtn').onclick=closeExamInfoModal;$('modalSaveBtn').onclick=()=>{examInfo.paper=Math.max(1,parseInt($('modalPaperInput').value)||1);examInfo.answer=Math.max(1,parseInt($('modalAnswerInput').value)||1);examInfo.draft=$('modalDraftInput').value;saveInfo();closeExamInfoModal()};$('examInfoModal').addEventListener('click',e=>{if(e.target===$('examInfoModal'))closeExamInfoModal()});
function colorize(remain){timer.classList.remove('warning','urgent','idle');progress.classList.remove('warning','urgent','off');stateDot.classList.remove('warn','danger','muted');hero.classList.toggle('idle',phase!=='exam');if(phase!=='exam'){timer.classList.add('idle');progress.classList.add('off');stateDot.classList.add('muted');return}if(remain<=300){timer.classList.add('urgent');progress.classList.add('urgent');stateDot.classList.add('danger')}else if(remain<=900){timer.classList.add('warning');progress.classList.add('warning');stateDot.classList.add('warn')}}
function currentRemain(){if(!serverAnchor)return serverRemain;return Math.max(0,serverRemain-Math.floor(Date.now()/1000-serverAnchor))}function updateLocal(){const r=currentRemain();timer.textContent=fmt(r);clock.textContent=new Date().toTimeString().slice(0,8);colorize(r);if(totalSec>0&&phase==='exam'){const pct=Math.max(0,Math.min(100,(totalSec-r)*100/totalSec));progress.style.width=pct.toFixed(1)+'%';progressText.textContent=pct.toFixed(0)+'%'}else{progress.style.width='0%';progressText.textContent='--'}}
async function sync(){try{syncState.textContent='● 同步中';const res=await fetch('/api/state?ts='+Date.now(),{cache:'no-store'});const raw=await res.text();const s=JSON.parse(raw);currentDebug=raw;if(s.error){subject.textContent=s.error;subtitle.textContent='请等待插件服务初始化';syncState.textContent='● 等待';return}phase=s.phase||'idle';serverAnchor=s.serverUnixSeconds||Math.floor(Date.now()/1000);serverRemain=s.remainingSeconds||0;totalSec=s.totalSeconds||0;const isClass=!!s.isClassTime;subject.textContent=s.subjectName||'等待课程';subtitle.textContent=s.stateText||s.subtitle||'等待';timerLabel.textContent=s.timerLabel||'等待同步';phaseText.textContent=(s.stateText||phase).toUpperCase();$('status').textContent=s.stateText||(typeof s.currentState==='string'?s.currentState:'--');stateName.textContent=s.currentState||s.state||'--';if(isClass){$('nextExamLabel').textContent='当前科目';$('nextExam').textContent=s.subjectName||'当前考试'}else{$('nextExamLabel').textContent='下一场';$('nextExam').textContent=s.nextSubjectName||'无'}serverTime.textContent=s.serverTime||'--';dateLine.textContent=(s.serverDate||'')+' '+(s.dayOfWeek||'');startTime.textContent=s.currentStart||s.startTime||'--:--';endTime.textContent=s.currentEnd||s.endTime||'--:--';sessionId.textContent='session: '+(s.sessionId||'--');if($('debugBox').classList.contains('show'))$('debugBox').textContent=raw;if(isClass&&(!wasClass||s.sessionId!==lastSessionId)){lastSessionId=s.sessionId||'';resetExamInfo();openExamInfoModal(s.subjectName)}else if(s.sessionId&&s.sessionId!==lastSessionId){lastSessionId=s.sessionId;loadInfo()}wasClass=isClass;syncState.textContent='● 已连接';updateLocal()}catch(e){syncState.textContent='● 连接中';currentDebug='fetch failed: '+e;console.error(e)}}
setInterval(updateLocal,1000);setInterval(sync,2000);loadInfo();sync();document.addEventListener('keydown',e=>{if(e.key==='F11')e.preventDefault()});
</script>
</body>
</html>
""";
    }

    public void Dispose()
    {
        Stop();
        if (_instance == this) _instance = null;
        try { _listener?.Close(); } catch { }
        _cts?.Dispose();
    }
}
