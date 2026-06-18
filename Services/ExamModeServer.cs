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
    private readonly HttpListener _listener;
    private readonly int _port;
    private CancellationTokenSource? _cts;

    public bool IsRunning { get; private set; }
    public string Url => $"http://localhost:{_port}";

    public ExamModeServer(int port = 9876)
    {
        _port = port;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://+:{_port}/");
    }

    public async Task StartAsync()
    {
        if (IsRunning) return;
        try
        {
            _listener.Start();
        }
        catch (HttpListenerException)
        {
            // 端口可能被占用，换一个端口再试
            _listener.Close();
            var newPort = _port + 1;
            var l = new HttpListener();
            l.Prefixes.Add($"http://+:{newPort}/");
            l.Start();
            System.Diagnostics.Debug.WriteLine($"[ExamMode] 端口 {_port} 被占用，改用 {newPort}");
        }

        IsRunning = true;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ListenLoop(_cts.Token));
        System.Diagnostics.Debug.WriteLine($"[ExamMode] 服务器已启动: {Url}");
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener.Stop(); } catch { }
        IsRunning = false;
        System.Diagnostics.Debug.WriteLine("[ExamMode] 服务器已停止");
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var getContextTask = _listener.GetContextAsync();
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
                System.Diagnostics.Debug.WriteLine($"[ExamMode] 请求处理异常: {ex.Message}");
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

        // 404
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
            return new { error = "服务未就绪" };
        }

        var currentSubject = lessons.CurrentSubject;
        var isClassTime = lessons.CurrentState == TimeState.OnClass;
        var isBreaking = lessons.CurrentState == TimeState.Breaking;
        var isAfterSchool = lessons.CurrentState == TimeState.AfterSchool;

        // 剩余时间（上课时=下课倒计时，课间=上课倒计时）
        var remainingTime = isClassTime
            ? lessons.OnBreakingTimeLeftTime
            : lessons.OnClassLeftTime;
        var remainingSeconds = (int)remainingTime.TotalSeconds;

        // 总时长
        var totalSeconds = 0;
        var currentItem = lessons.CurrentTimeLayoutItem;
        if (currentItem.TimeType == 1)
            totalSeconds = (int)currentItem.Last.TotalSeconds;
        else if (currentItem.TimeType == 0)
            totalSeconds = remainingSeconds; // 课间用剩余时间

        // 下一门课/考试
        var nextSubject = lessons.NextClassSubject;
        var nextSubjectName = nextSubject?.Name ?? "";

        // 当前科目名
        var subjectName = currentSubject?.Name ?? (isBreaking ? "课间休息" : "暂无课程");

        return new
        {
            subjectName,
            isClassTime,
            isBreaking,
            isAfterSchool,
            remainingSeconds,
            totalSeconds,
            nextSubjectName,
            currentState = lessons.CurrentState.ToString(),
            isTimerRunning = lessons.IsTimerRunning,
            serverTime = DateTime.Now.ToString("HH:mm:ss"),
            serverDate = DateTime.Now.ToString("yyyy-MM-dd"),
            dayOfWeek = DateTime.Now.DayOfWeek.ToString()
        };
    }

    private string GetDashboardHtml()
    {
        return @"<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
<meta charset=""UTF-8"">
<meta name=""viewport"" content=""width=device-width,initial-scale=1"">
<title>考试模式 — AIIsland</title>
<style>
*{margin:0;padding:0;box-sizing:border-box}
body{font-family:'Segoe UI','Microsoft YaHei',sans-serif;background:#0d1117;color:#c9d1d9;display:flex;align-items:center;justify-content:center;min-height:100vh;overflow:hidden;user-select:none}
.container{text-align:center;padding:40px;max-width:800px;width:100%}
.header{font-size:14px;opacity:.5;margin-bottom:12px;letter-spacing:2px;text-transform:uppercase}
.subject{font-size:48px;font-weight:700;margin-bottom:8px;color:#58a6ff}
.subtitle{font-size:16px;opacity:.4;margin-bottom:40px}
.timer-section{margin:32px 0}
.timer{font-size:120px;font-weight:900;font-variant-numeric:tabular-nums;line-height:1;margin-bottom:8px;transition:color .5s}
.timer.urgent{color:#f85149}
.timer.warning{color:#d29922}
.timer.normal{color:#58a6ff}
.timer-label{font-size:14px;opacity:.4;letter-spacing:4px;text-transform:uppercase}
.progress-bar{width:100%;height:6px;background:#21262d;border-radius:3px;margin:24px 0;overflow:hidden}
.progress-fill{height:100%;border-radius:3px;transition:width 1s linear,background .5s}
.progress-fill.normal{background:#58a6ff}
.progress-fill.warning{background:#d29922}
.progress-fill.urgent{background:#f85149}
.info-grid{display:grid;grid-template-columns:1fr 1fr;gap:16px;margin-top:40px;text-align:left}
.info-card{background:#161b22;border:1px solid #21262d;border-radius:8px;padding:16px}
.info-label{font-size:11px;opacity:.4;letter-spacing:1px;text-transform:uppercase;margin-bottom:4px}
.info-value{font-size:18px;font-weight:600}
.time-over{font-size:80px;font-weight:900;color:#f85149;animation:pulse 1s infinite}
@keyframes pulse{0%,100%{opacity:1}50%{opacity:.5}}
.next-exam{font-size:14px;opacity:.5;margin-top:24px}
.footer{position:fixed;bottom:16px;left:50%;transform:translateX(-50%);font-size:11px;opacity:.2}
.footer a{color:inherit}
</style>
</head>
<body>
<div class=""container"">
  <div class=""header"" id=""header"">AIIsland 考试模式</div>
  <div class=""subject"" id=""subject"">加载中...</div>
  <div class=""subtitle"" id=""subtitle""></div>
  <div class=""timer-section"">
    <div class=""timer normal"" id=""timer"">--:--:--</div>
    <div class=""timer-label"" id=""timerLabel"">剩余时间</div>
  </div>
  <div class=""progress-bar""><div class=""progress-fill normal"" id=""progress"" style=""width:0""></div></div>
  <div class=""info-grid"">
    <div class=""info-card"">
      <div class=""info-label"">当前时间</div>
      <div class=""info-value"" id=""clock"">--:--:--</div>
    </div>
    <div class=""info-card"">
      <div class=""info-label"">状态</div>
      <div class=""info-value"" id=""status"">--</div>
    </div>
  </div>
  <div class=""next-exam"" id=""nextExam""></div>
</div>
<div class=""footer"">AIIsland Exam Mode · <a href=""https://github.com/qbw101/AIIsland"" target=""_blank"">GitHub</a></div>
<script>
let totalSec=0,remainSec=0;
async function fetchState(){
  try{
    const r=await fetch('/api/state');
    const s=await r.json();
    if(s.error){document.getElementById('subject').textContent=s.error;return}
    // Subject
    document.getElementById('subject').textContent=s.subjectName||'课间休息';
    document.getElementById('subtitle').textContent=s.isClassTime?'考试进行中':s.isBreaking?'课间休息':s.isAfterSchool?'已放学':'';
    // Timer
    remainSec=s.remainingSeconds||0;
    totalSec=s.totalSeconds||(remainSec>0?remainSec:1);
    updateTimerDisplay();
    // Progress
    updateProgress();
    // Info
    const now=new Date();
    document.getElementById('clock').textContent=now.toTimeString().slice(0,8);
    document.getElementById('status').textContent=s.isClassTime?'考试中':s.isBreaking?'课间':'就绪';
    // Next
    document.getElementById('nextExam').textContent=s.nextSubjectName?'下一场: '+s.nextSubjectName:'';
    // Header date
    document.getElementById('header').textContent='AIIsland 考试模式 · '+s.serverDate;
  }catch(e){
    document.getElementById('subject').textContent='连接中...';
  }
}
function updateTimerDisplay(){
  const el=document.getElementById('timer');
  const h=Math.floor(remainSec/3600),m=Math.floor((remainSec%3600)/60),s=remainSec%60;
  el.textContent=String(h).padStart(2,'0')+':'+String(m).padStart(2,'0')+':'+String(s).padStart(2,'0');
  // Color
  el.classList.remove('normal','warning','urgent');
  if(remainSec<=300) el.classList.add('urgent');
  else if(remainSec<=900) el.classList.add('warning');
  else el.classList.add('normal');
  // Timer label
  const label=document.getElementById('timerLabel');
  if(remainSec<=0) label.textContent='时间到！';
  else if(remainSec<=300) label.textContent='即将结束';
  else if(remainSec<=900) label.textContent='剩余时间（最后阶段）';
  else label.textContent='剩余时间';
}
function updateProgress(){
  if(totalSec<=0)return;
  const pct=Math.max(0,Math.min(100,(remainSec/totalSec)*100));
  const bar=document.getElementById('progress');
  bar.style.width=pct+'%';
  bar.classList.remove('normal','warning','urgent');
  if(remainSec<=300)bar.classList.add('urgent');
  else if(remainSec<=900)bar.classList.add('warning');
  else bar.classList.add('normal');
}
// Countdown locally between fetches
setInterval(()=>{
  if(remainSec>0)remainSec--;
  updateTimerDisplay();
  updateProgress();
  const now=new Date();
  document.getElementById('clock').textContent=now.toTimeString().slice(0,8);
},1000);
// Fetch from server every 15s to sync
setInterval(fetchState,15000);
fetchState();
// F11 hint
document.addEventListener('keydown',e=>{if(e.key==='F11')e.preventDefault()});
</script>
</body>
</html>";
    }

    public void Dispose()
    {
        Stop();
        try { (_listener as IDisposable)?.Dispose(); } catch { }
        _cts?.Dispose();
    }
}
