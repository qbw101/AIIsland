using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using ClassIsland.AISmartClass.Models;

namespace ClassIsland.AISmartClass.Services.Telemetry;

/// <summary>
/// 匿名遥测：会话在线状态、插件错误、崩溃现场。
/// 只上报运行元数据与异常信息，不上报课表内容、提示词、API Key 或任何账号标识。
/// </summary>
public sealed class TelemetryService : IAsyncDisposable
{
    private const int HeartbeatSeconds = 30;
    private const string MarkerFileName = "session.json";

    private static readonly Regex SecretPattern = new(
        @"(sk-[A-Za-z0-9_\-]{10,})|(?i)(api[-_ ]?key\s*[=:]\s*)\S+|(?i)(bearer\s+)[A-Za-z0-9\-\._~\+/]{12,}",
        RegexOptions.Compiled);

    private readonly string _spoolFolder;    private readonly Action _persistSettings;
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<string, long> _recentReports = new(StringComparer.Ordinal);
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;

    private TelemetrySettings _settings;
    private TelemetryClient? _client;
    private CancellationTokenSource? _heartbeatCts;
    private Task? _heartbeatTask;
    private long _frameSeq;
    private bool _started;

    // 最近一次「真正用于建连」的端点快照。注意：_settings 与设置页共享同一实例，
    // 用户改完设置后对象内的值早已是新值，直接拿 _settings 前后对比永远相等，
    // 必须用这份快照才能判断端点是否真的变化。
    private string _activeHost = "";
    private int _activePort;
    private string _activeToken = "";
    private bool _activeEnabled;
    private bool _hooked;
    private bool _isFirstRun;
    private bool _reportingFromLogger;
    private string _statusText = "未启用";
    private readonly string _pluginVersion;
    private readonly string? _ciVersion;
    private readonly string _osName;

    public TelemetryService(TelemetrySettings settings, string dataFolder, Action persistSettings)
    {
        _settings = settings;
        _spoolFolder = Path.Combine(dataFolder, "telemetry");
        _persistSettings = persistSettings;
        _pluginVersion = ResolvePluginVersion();
        _ciVersion = ResolveClassIslandVersion();
        _osName = ResolveOsName();
    }

    /// <summary>当前运行的遥测服务，供设置页与 Logger 钩子使用。</summary>
    public static TelemetryService? Current { get; private set; }

    public string StatusText => _statusText;

    public bool IsConnected => _client?.State == TelemetryConnectionState.Connected;

    public string SessionId => _sessionId;

    public event Action? StatusChanged;

    // ================= 生命周期 =================

    public void Start()
    {
        lock (_sync)
        {
            if (_started) return;
            _started = true;
            Current = this;
            HookCrashHandlers();
            Logger.ErrorReported += OnLoggerError;
        }

        try
        {
            if (!_settings.Enabled)
            {
                SetStatus("未启用");
                return;
            }

            if (!_settings.HasEndpoint)
            {
                SetStatus("未配置服务器地址");
                return;
            }

            EnsureInstallId();
            var previous = ReadMarker();
            var previousCrashed = previous is { CleanExit: false } &&
                                  previous.StartedAt > DateTimeOffset.Now.AddDays(-7);

            StartClient();

            if (previousCrashed)
            {
                Logger.Info("[Telemetry] 检测到上次会话未正常退出，准备上报崩溃现场");
                _ = Task.Run(() => ReportPreviousSessionCrashAsync(previous!));
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[Telemetry] 启动失败: {ex.Message}");
            SetStatus("启动失败");
        }
    }

    /// <summary>设置保存后调用；端点或开关变化时自动重连。</summary>
    public void ApplySettings(TelemetrySettings settings)
    {
        var host = (settings.ServerHost ?? "").Trim();
        var port = settings.ServerPort;
        var token = settings.Token ?? "";
        var enabled = settings.Enabled;

        var changed = !string.Equals(_activeHost, host, StringComparison.OrdinalIgnoreCase)
                      || _activePort != port
                      || !string.Equals(_activeToken, token, StringComparison.Ordinal)
                      || _activeEnabled != enabled;

        _settings = settings;
        if (!changed) return;

        _activeHost = host;
        _activePort = port;
        _activeToken = token;
        _activeEnabled = enabled;

        if (!_started) return;

        Logger.Info($"[Telemetry] 配置变化，重启遥测连接 → {host}:{port}（启用={enabled}）");
        _ = Task.Run(RestartAsync);
    }

    private async Task RestartAsync()
    {
        try
        {
            await StopClientAsync("restart").ConfigureAwait(false);

            if (!_settings.Enabled)
            {
                SetStatus("未启用");
                return;
            }

            if (!_settings.HasEndpoint)
            {
                SetStatus("未配置服务器地址");
                return;
            }

            EnsureInstallId();
            StartClient();
        }
        catch (Exception ex)
        {
            Logger.Info($"[Telemetry] 重启失败: {ex.Message}");
        }
    }

    private void StartClient()
    {
        // 记录本次真正生效的端点，供 ApplySettings 判断是否需要重连
        _activeHost = (_settings.ServerHost ?? "").Trim();
        _activePort = _settings.ServerPort;
        _activeToken = _settings.Token ?? "";
        _activeEnabled = _settings.Enabled;

        var client = new TelemetryClient(_activeHost, _activePort, _activeToken, BuildHello);
        client.StateChanged += OnConnectionStateChanged;
        client.Connected += OnConnected;
        SetStatus("连接中…");
        client.Start();
        _client = client;

        _heartbeatCts = new CancellationTokenSource();
        _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(_heartbeatCts.Token));

        WriteMarker(cleanExit: false);
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        var tick = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(HeartbeatSeconds), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (ct.IsCancellationRequested) break;

            _client?.Enqueue(new TelemetryFrame
            {
                Type = "heartbeat",
                Id = NextFrameId(),
                Data = new HeartbeatPayload
                {
                    UptimeSeconds = UptimeSeconds,
                    State = "running",
                    Pending = _client?.PendingFrames ?? 0
                }
            });

            // 每两次心跳（约 1 分钟）刷新一次崩溃标记
            if (++tick % 2 == 0) WriteMarker(cleanExit: false);
        }
    }

    public async Task ShutdownAsync()
    {
        try
        {
            if (_hooked)
            {
                UnhookCrashHandlers();
                Logger.ErrorReported -= OnLoggerError;
            }

            try { _heartbeatCts?.Cancel(); } catch { /* ignore */ }

            await StopClientAsync("graceful").ConfigureAwait(false);

            if (_settings.Enabled && _settings.HasEndpoint)
                WriteMarker(cleanExit: true);

            SetStatus("已停止");
        }
        catch (Exception ex)
        {
            Logger.Info($"[Telemetry] 关闭失败: {ex.Message}");
        }
    }

    private async Task StopClientAsync(string reason)
    {
        try { _heartbeatCts?.Cancel(); } catch { /* ignore */ }

        var heartbeatTask = _heartbeatTask;
        _heartbeatTask = null;
        _heartbeatCts?.Dispose();
        _heartbeatCts = null;

        if (heartbeatTask != null)
        {
            try { await heartbeatTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch { /* ignore */ }
        }

        var client = _client;
        _client = null;
        if (client == null) return;

        try
        {
            await client.StopAsync(new ByePayload
            {
                Reason = reason,
                UptimeSeconds = UptimeSeconds
            }, TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            await client.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Info($"[Telemetry] 断开连接时出错: {ex.Message}");
        }
    }

    // ================= 握手 =================

    private HelloPayload BuildHello() => new()
    {
        InstallId = _settings.InstallId,
        SessionId = _sessionId,
        PluginVersion = _pluginVersion,
        CiVersion = _ciVersion,
        Os = _osName,
        Locale = CultureInfo.CurrentUICulture.Name,
        FirstRun = _isFirstRun,
        AiEndpointHost = ResolveEndpointHost(),
        Client = $"AIIsland/{_pluginVersion}"
    };

    private void OnConnectionStateChanged(TelemetryConnectionState state, string? error)
    {
        switch (state)
        {
            case TelemetryConnectionState.Connected:
                SetStatus("已连接");
                break;
            case TelemetryConnectionState.Connecting:
                SetStatus("连接中…");
                break;
            case TelemetryConnectionState.Unauthorized:
                SetStatus("接入令牌无效");
                break;
            case TelemetryConnectionState.Error:
                SetStatus($"重连中：{Trim(error, 60)}");
                break;
        }
    }

    private void OnConnected()
    {
        _ = Task.Run(FlushCrashSpoolAsync);
    }

    private void EnsureInstallId()
    {
        if (!string.IsNullOrWhiteSpace(_settings.InstallId)) return;

        _settings.InstallId = Guid.NewGuid().ToString("N");
        _isFirstRun = true;
        try { _persistSettings(); }
        catch { /* 保存失败不影响上报 */ }
        Logger.Info($"[Telemetry] 已生成匿名设备 ID {_settings.InstallId[..8]}");
    }

    // ================= 错误上报 =================

    public void ReportError(Exception ex, string scenario, IReadOnlyDictionary<string, string>? context = null,
        string severity = "error", string source = "handled")
    {
        try
        {
            if (!CanReport()) return;
            if (!ShouldReport($"{scenario}|{ex.GetType().Name}|{ex.Message}")) return;

            var payload = BuildPayload(ex, scenario, severity, source, context);
            Enqueue(payload);
        }
        catch
        {
            // 遥测绝不能影响主流程
        }
    }

    public void ReportMessage(string message, string scenario, Exception? ex = null,
        string severity = "warning", string source = "logger")
    {
        try
        {
            if (!CanReport()) return;
            if (!ShouldReport($"{scenario}|{message}")) return;

            var payload = new EventPayload
            {
                EventId = Guid.NewGuid().ToString("N"),
                Severity = severity,
                Scenario = scenario,
                Message = Sanitize(Trim(message, 1500)),
                ExceptionType = ex?.GetType().FullName,
                StackTrace = ex != null ? Sanitize(ex.StackTrace) : null,
                Source = source,
                OccurredAt = DateTimeOffset.Now,
                PluginVersion = _pluginVersion,
                CiVersion = _ciVersion,
                Os = _osName
            };

            Enqueue(payload);
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>立即上报崩溃（用于未处理异常）。会在本地留一份副本，发送成功后再删除。</summary>
    public void ReportCrash(Exception? ex, string source, string scenario, string? message = null, bool includeForensics = true)
    {
        if (!CanReport(requireConnection: false) || !_settings.ReportCrashes) return;

        var payload = ex != null
            ? BuildPayload(ex, scenario, "fatal", source, null)
            : new EventPayload
            {
                EventId = Guid.NewGuid().ToString("N"),
                Severity = "fatal",
                Scenario = scenario,
                Message = message ?? "插件进程异常退出",
                Source = source,
                OccurredAt = DateTimeOffset.Now,
                PluginVersion = _pluginVersion,
                CiVersion = _ciVersion,
                Os = _osName
            };

        payload.Severity = "fatal";

        if (includeForensics && _settings.IncludeLogTail)
        {
            payload.LogTail = CrashForensics.ReadLogTail(_startedAt);
            payload.EventLog = CrashForensics.ReadApplicationErrors(_startedAt);
            var dumps = CrashForensics.DescribeCrashDumps(_startedAt);
            if (!string.IsNullOrEmpty(dumps))
            {
                payload.Context ??= new Dictionary<string, string>();
                payload.Context["crashDumps"] = dumps;
            }
        }

        var task = Task.Run(() => SendCrashAsync(payload));

        // 崩溃处理回调里不能长时间阻塞，同步等一小会儿确保数据发出
        if (source is "unhandled" or "dispatcher")
        {
            try { task.Wait(TimeSpan.FromSeconds(4)); } catch { /* ignore */ }
        }
    }

    private async Task SendCrashAsync(EventPayload payload)
    {
        var spoolPath = SpoolCrash(payload);
        try
        {
            if (_client?.State == TelemetryConnectionState.Connected)
            {
                var ok = await _client.TrySendNowAsync(new TelemetryFrame
                {
                    Type = "crash",
                    Id = NextFrameId(),
                    Token = _settings.Token,
                    Data = payload
                }, TimeSpan.FromSeconds(2.5)).ConfigureAwait(false);

                if (ok && spoolPath != null) TryDelete(spoolPath);
            }
        }
        catch
        {
            // 保留 spool 文件，下次启动补传
        }
    }

    private void OnLoggerError(string message, Exception? exception)
    {
        if (_reportingFromLogger) return;
        if (message.Contains("[Telemetry]", StringComparison.Ordinal)) return;

        _reportingFromLogger = true;
        try
        {
            ReportMessage(message, "插件日志", exception, "error", "logger");
        }
        finally
        {
            _reportingFromLogger = false;
        }
    }

    public bool SendTestEvent()
    {
        if (!CanReport())
        {
            SetStatus(_client == null ? "未启用" : "未连接，无法发送测试事件");
            return false;
        }

        Enqueue(new EventPayload
        {
            EventId = Guid.NewGuid().ToString("N"),
            Severity = "warning",
            Scenario = "遥测测试",
            Message = "这是一条来自 AIIsland 设置页的测试事件，收到说明链路正常。",
            ExceptionType = "Telemetry.SelfTest",
            Source = "manual",
            OccurredAt = DateTimeOffset.Now,
            PluginVersion = _pluginVersion,
            CiVersion = _ciVersion,
            Os = _osName,
            Context = new Dictionary<string, string> { ["session"] = _sessionId[..8] }
        });

        SetStatus("已发送测试事件");
        return true;
    }

    private bool CanReport(bool requireConnection = true)
    {
        if (!_settings.Enabled || !_settings.HasEndpoint) return false;
        if (!requireConnection) return true;
        return _client?.State == TelemetryConnectionState.Connected;
    }

    /// <summary>同一问题 5 分钟内只上报一次，避免日志风暴把队列打满。</summary>
    private bool ShouldReport(string key)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (_recentReports.Count > 400) _recentReports.Clear();

        if (_recentReports.TryGetValue(key, out var last) && now - last < 300) return false;
        _recentReports[key] = now;
        return true;
    }

    private EventPayload BuildPayload(Exception ex, string scenario, string severity, string source,
        IReadOnlyDictionary<string, string>? context)
    {
        var payload = new EventPayload
        {
            EventId = Guid.NewGuid().ToString("N"),
            Severity = severity,
            Scenario = scenario,
            Message = Sanitize(Trim(ex.Message, 1500)),
            ExceptionType = ex.GetType().FullName,
            StackTrace = Sanitize(ex.StackTrace),
            Source = source,
            OccurredAt = DateTimeOffset.Now,
            PluginVersion = _pluginVersion,
            CiVersion = _ciVersion,
            Os = _osName
        };

        var merged = new Dictionary<string, string>();
        if (ex.InnerException != null)
        {
            merged["innerType"] = ex.InnerException.GetType().FullName ?? "?";
            merged["innerMessage"] = Trim(ex.InnerException.Message, 300);
        }

        if (context != null)
        {
            foreach (var (key, value) in context)
                merged[key] = Trim(value, 300);
        }

        if (merged.Count > 0) payload.Context = merged;
        return payload;
    }

    private void Enqueue(EventPayload payload)
    {
        _client?.Enqueue(new TelemetryFrame
        {
            Type = "error",
            Id = NextFrameId(),
            Data = payload
        });
    }

    // ================= 崩溃现场 =================

    private async Task ReportPreviousSessionCrashAsync(SessionMarker previous)
    {
        try
        {
            var deadline = DateTimeOffset.Now.AddMinutes(3);
            while (_client?.State != TelemetryConnectionState.Connected && DateTimeOffset.Now < deadline)
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);

            if (_client?.State != TelemetryConnectionState.Connected) return;
            if (!_settings.ReportCrashes) return;

            var payload = new EventPayload
            {
                EventId = Guid.NewGuid().ToString("N"),
                Severity = "fatal",
                Scenario = "进程异常退出",
                Message = $"上次运行未收到正常退出信号（会话 {SafeSession(previous.SessionId)}），疑似 ClassIsland 崩溃或进程被强制结束。",
                ExceptionType = "ClassIsland.UncleanExit",
                Source = "previous-session",
                OccurredAt = previous.LastHeartbeat,
                PluginVersion = previous.PluginVersion ?? _pluginVersion,
                CiVersion = _ciVersion,
                Os = _osName,
                Context = new Dictionary<string, string>
                {
                    ["sessionId"] = previous.SessionId,
                    ["sessionStartedAt"] = previous.StartedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    ["lastHeartbeat"] = previous.LastHeartbeat.ToString("yyyy-MM-dd HH:mm:ss")
                }
            };

            if (_settings.IncludeLogTail)
            {
                payload.LogTail = CrashForensics.ReadLogTail(previous.StartedAt);
                payload.EventLog = CrashForensics.ReadApplicationErrors(previous.LastHeartbeat);
                var dumps = CrashForensics.DescribeCrashDumps(previous.StartedAt);
                if (!string.IsNullOrEmpty(dumps)) payload.Context["crashDumps"] = dumps;
            }

            await SendCrashAsync(payload).ConfigureAwait(false);
            Logger.Info("[Telemetry] 上次异常退出记录已上报");
        }
        catch (Exception ex)
        {
            Logger.Info($"[Telemetry] 上报上次异常退出失败: {ex.Message}");
        }
    }

    private string? SpoolCrash(EventPayload payload)
    {
        try
        {
            Directory.CreateDirectory(_spoolFolder);
            var path = Path.Combine(_spoolFolder,
                $"crash-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{payload.EventId?[..6] ?? "000000"}.json");
            var entry = new SpoolEntry { Type = "crash", Data = payload };
            File.WriteAllText(path, JsonSerializer.Serialize(entry, TelemetryJson.Options));
            return path;
        }
        catch
        {
            return null;
        }
    }

    private async Task FlushCrashSpoolAsync()
    {
        try
        {
            if (!Directory.Exists(_spoolFolder)) return;

            foreach (var file in Directory.EnumerateFiles(_spoolFolder, "crash-*.json")
                         .OrderByDescending(f => f)
                         .Take(20))
            {
                if (_client?.State != TelemetryConnectionState.Connected) return;

                SpoolEntry? entry = null;
                try
                {
                    entry = JsonSerializer.Deserialize<SpoolEntry>(File.ReadAllText(file), TelemetryJson.Options);
                }
                catch
                {
                    TryDelete(file);
                    continue;
                }

                if (entry?.Data == null)
                {
                    TryDelete(file);
                    continue;
                }

                var ok = await _client.TrySendNowAsync(new TelemetryFrame
                {
                    Type = "crash",
                    Id = NextFrameId(),
                    Data = entry.Data
                }, TimeSpan.FromSeconds(3)).ConfigureAwait(false);

                if (ok) TryDelete(file);
                else return;
            }
        }
        catch
        {
            // ignore
        }
    }

    // ================= 标记文件 =================

    private string MarkerPath => Path.Combine(_spoolFolder, MarkerFileName);

    private void WriteMarker(bool cleanExit)
    {
        if (!_settings.Enabled || !_settings.HasEndpoint) return;

        try
        {
            Directory.CreateDirectory(_spoolFolder);
            var marker = new SessionMarker
            {
                SessionId = _sessionId,
                StartedAt = _startedAt,
                LastHeartbeat = DateTimeOffset.Now,
                CleanExit = cleanExit,
                PluginVersion = _pluginVersion
            };
            var json = JsonSerializer.Serialize(marker, TelemetryJson.Options);
            var tmp = MarkerPath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, MarkerPath, overwrite: true);
        }
        catch
        {
            // ignore
        }
    }

    private SessionMarker? ReadMarker()
    {
        try
        {
            if (!File.Exists(MarkerPath)) return null;
            var marker = JsonSerializer.Deserialize<SessionMarker>(File.ReadAllText(MarkerPath), TelemetryJson.Options);
            return marker;
        }
        catch
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* ignore */ }
    }

    // ================= 异常钩子 =================

    private void HookCrashHandlers()
    {
        if (_hooked) return;
        _hooked = true;

        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandled;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandled;
        }
        catch (Exception ex)
        {
            Logger.Info($"[Telemetry] 无法挂接 UI 线程异常钩子: {ex.Message}");
        }
    }

    private void UnhookCrashHandlers()
    {
        if (!_hooked) return;
        _hooked = false;

        AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandled;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        try
        {
            Dispatcher.UIThread.UnhandledException -= OnDispatcherUnhandled;
        }
        catch
        {
            // ignore
        }
    }

    private void OnAppDomainUnhandled(object? sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        var message = ex?.Message ?? e.ExceptionObject?.ToString() ?? "未知异常";
        Logger.Info($"[Telemetry] 捕获进程级未处理异常: {message}");

        if (ex != null) ReportCrash(ex, "unhandled", "未处理异常（进程终止）");
        else ReportCrash(null, "unhandled", "未处理异常（进程终止）", message);
    }

    private void OnDispatcherUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Info($"[Telemetry] 捕获 UI 线程未处理异常: {e.Exception.Message}");
        ReportCrash(e.Exception, "dispatcher", "UI 线程未处理异常");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ReportError(e.Exception, "未观察的任务异常", null, "error", "task");
    }

    // ================= 工具 =================

    private void SetStatus(string status)
    {
        if (_statusText == status) return;
        _statusText = status;
        try { StatusChanged?.Invoke(); } catch { /* ignore */ }
    }

    private int UptimeSeconds => (int)Math.Max(0, (DateTimeOffset.UtcNow - _startedAt).TotalSeconds);

    private string NextFrameId() => Interlocked.Increment(ref _frameSeq).ToString("x8");

    private static string ResolvePluginVersion()
    {
        // 程序集版本默认是 1.0.0.0，真实版本号写在插件目录的 manifest.yml 里
        try
        {
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrEmpty(dir))
            {
                var manifest = Path.Combine(dir, "manifest.yml");
                if (File.Exists(manifest))
                {
                    foreach (var line in File.ReadLines(manifest))
                    {
                        if (!line.StartsWith("version:", StringComparison.OrdinalIgnoreCase)) continue;
                        var value = line["version:".Length..].Trim();
                        if (value.Length > 0) return value;
                    }
                }
            }
        }
        catch
        {
            // 退回程序集版本
        }

        return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
    }

    private static string? ResolveClassIslandVersion()
    {
        try
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "ClassIsland", StringComparison.OrdinalIgnoreCase))
                ?.GetName().Version?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveOsName()
    {
        var version = Environment.OSVersion.Version;
        var name = version.Build >= 22000 ? "Windows 11" : "Windows 10";
        return $"{name} {version.Major}.{version.Minor}.{version.Build}";
    }

    private static string? ResolveEndpointHost()
    {
        try
        {
            var endpoint = Plugin.GetAISettings()?.Endpoint;
            if (string.IsNullOrWhiteSpace(endpoint)) return null;
            return Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ? uri.Host : null;
        }
        catch
        {
            return null;
        }
    }

    private static string SafeSession(string sessionId) =>
        sessionId.Length <= 8 ? sessionId : sessionId[..8];

    private static string Trim(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return "";
        value = value.Replace("\r", " ").Replace("\n", " ");
        return value.Length <= max ? value : value[..max] + "…";
    }

    private static string? Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return SecretPattern.Replace(value, match =>
        {
            if (match.Value.StartsWith("sk-", StringComparison.OrdinalIgnoreCase)) return "sk-***";
            if (match.Value.StartsWith("Bearer", StringComparison.OrdinalIgnoreCase)) return "Bearer ***";
            var idx = match.Value.IndexOfAny(new[] { '=', ':' });
            return idx >= 0 ? match.Value[..(idx + 1)] + "***" : "***";
        });
    }

    public ValueTask DisposeAsync() => new(ShutdownAsync());

    private sealed class SessionMarker
    {
        public string SessionId { get; set; } = "";
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset LastHeartbeat { get; set; }
        public bool CleanExit { get; set; }
        public string? PluginVersion { get; set; }
    }

    private sealed class SpoolEntry
    {
        public string Type { get; set; } = "crash";
        public EventPayload? Data { get; set; }
    }
}
