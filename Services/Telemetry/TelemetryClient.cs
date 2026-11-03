using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace ClassIsland.AISmartClass.Services.Telemetry;

public enum TelemetryConnectionState
{
    Disabled,
    Connecting,
    Connected,
    Unauthorized,
    Error
}

/// <summary>
/// 遥测传输层：到服务器的一条 TCP 长连接，NDJSON 帧，断线自动重连。
/// 队列满了丢最旧的帧，保证不会因为网络问题拖慢 ClassIsland 主线程。
/// </summary>
internal sealed class TelemetryClient : IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly string? _token;
    private readonly Func<HelloPayload> _helloFactory;
    private readonly Channel<TelemetryFrame> _queue;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _stateLock = new();

    private TcpClient? _client;
    private StreamWriter? _writer;
    private Task? _loop;
    private Task? _readerTask;
    private long _frameId;
    private volatile bool _fatal;

    public TelemetryClient(string host, int port, string? token, Func<HelloPayload> helloFactory, int queueCapacity = 512)
    {
        _host = host;
        _port = port;
        _token = string.IsNullOrWhiteSpace(token) ? null : token;
        _helloFactory = helloFactory;
        _queue = Channel.CreateBounded<TelemetryFrame>(new BoundedChannelOptions(Math.Max(32, queueCapacity))
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
    }

    public TelemetryConnectionState State { get; private set; } = TelemetryConnectionState.Disabled;

    public string? LastError { get; private set; }

    public int PendingFrames => _queue.Reader.Count;

    /// <summary>连接状态变化（状态, 错误信息）。</summary>
    public event Action<TelemetryConnectionState, string?>? StateChanged;

    /// <summary>连接建立并握手成功。</summary>
    public event Action? Connected;

    public void Start()
    {
        if (_loop != null) return;
        _loop = Task.Run(RunAsync);
    }

    /// <summary>入队一帧。非阻塞，队列满时丢弃最旧的数据。</summary>
    public void Enqueue(TelemetryFrame frame) => _queue.Writer.TryWrite(frame);

    /// <summary>立即同步发送（用于崩溃现场），带超时，失败返回 false。</summary>
    public async Task<bool> TrySendNowAsync(TelemetryFrame frame, TimeSpan timeout)
    {
        if (State != TelemetryConnectionState.Connected) return false;
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await WriteAsync(frame, cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    public async Task StopAsync(ByePayload? bye, TimeSpan timeout)
    {
        if (bye != null && State == TelemetryConnectionState.Connected)
        {
            await TrySendNowAsync(new TelemetryFrame
            {
                Type = "bye",
                Id = NextId(),
                Data = bye
            }, TimeSpan.FromSeconds(1.5)).ConfigureAwait(false);
        }

        try { _cts.Cancel(); } catch { /* ignore */ }
        CloseConnection();

        if (_loop != null)
        {
            try { await _loop.WaitAsync(timeout).ConfigureAwait(false); }
            catch { /* 退出阶段忽略 */ }
        }
    }

    private async Task RunAsync()
    {
        var backoff = TimeSpan.FromSeconds(3);
        TelemetryFrame? pending = null;

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                SetState(TelemetryConnectionState.Connecting);
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                connectCts.CancelAfter(TimeSpan.FromSeconds(10));

                var client = new TcpClient { NoDelay = true };
                await client.ConnectAsync(_host, _port, connectCts.Token).ConfigureAwait(false);

                var stream = client.GetStream();
                var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                var reader = new StreamReader(stream, new UTF8Encoding(false));
                _client = client;
                _writer = writer;

                await WriteAsync(new TelemetryFrame
                {
                    Type = "hello",
                    Id = NextId(),
                    Token = _token,
                    Data = _helloFactory()
                }, _cts.Token).ConfigureAwait(false);

                var ack = await ReadLineAsync(reader, TimeSpan.FromSeconds(10), _cts.Token).ConfigureAwait(false);
                if (ack == null)
                    throw new IOException("服务器未返回握手应答");

                if (ack.Contains("\"unauthorized\"", StringComparison.Ordinal))
                {
                    LastError = "接入令牌无效";
                    SetState(TelemetryConnectionState.Unauthorized);
                    _fatal = true;
                    break;
                }

                SetState(TelemetryConnectionState.Connected);
                backoff = TimeSpan.FromSeconds(3);
                Logger.Info($"[Telemetry] 已连接遥测服务器 {_host}:{_port}");

                _readerTask = Task.Run(() => DrainRepliesAsync(reader, _cts.Token));
                try { Connected?.Invoke(); } catch { /* ignore */ }

                while (!_cts.IsCancellationRequested)
                {
                    pending ??= await _queue.Reader.ReadAsync(_cts.Token).ConfigureAwait(false);
                    using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                    sendCts.CancelAfter(TimeSpan.FromSeconds(15));
                    await WriteAsync(pending, sendCts.Token).ConfigureAwait(false);
                    pending = null;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                SetState(TelemetryConnectionState.Error);
                Logger.Info($"[Telemetry] 连接中断：{ex.Message}");
            }
            finally
            {
                CloseConnection();
            }

            if (_fatal) break;

            try
            {
                await Task.Delay(backoff, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            backoff = TimeSpan.FromSeconds(Math.Min(60, backoff.TotalSeconds * 2));
        }
    }

    private async Task WriteAsync(TelemetryFrame frame, CancellationToken ct)
    {
        var writer = _writer ?? throw new IOException("尚未建立连接");
        var json = JsonSerializer.Serialize(frame, TelemetryJson.Options);
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await writer.WriteLineAsync(json.AsMemory(), ct).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task DrainRepliesAsync(StreamReader reader, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line == null) break;
                if (line.Contains("\"unauthorized\"", StringComparison.Ordinal))
                {
                    LastError = "接入令牌无效";
                    _fatal = true;
                    break;
                }
            }
        }
        catch
        {
            // 连接关闭
        }

        // 对端关闭后主动关闭 socket，让写入端尽快失败并触发重连
        if (!ct.IsCancellationRequested)
            CloseConnection();
    }

    private static async Task<string?> ReadLineAsync(StreamReader reader, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            return await reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private void SetState(TelemetryConnectionState state)
    {
        lock (_stateLock)
        {
            if (State == state) return;
            State = state;
        }

        try { StateChanged?.Invoke(state, LastError); } catch { /* ignore */ }
    }

    private string NextId() => Interlocked.Increment(ref _frameId).ToString("x8");

    private void CloseConnection()
    {
        try { _writer?.Dispose(); } catch { /* ignore */ }
        try { _client?.Close(); } catch { /* ignore */ }
        _writer = null;
        _client = null;
    }

    public async ValueTask DisposeAsync()
    {
        try { _cts.Cancel(); } catch { /* ignore */ }
        CloseConnection();
        if (_loop != null)
        {
            try { await _loop.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); } catch { /* ignore */ }
        }
        if (_readerTask != null)
        {
            try { await _readerTask.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); } catch { /* ignore */ }
        }
        _writeGate.Dispose();
        _cts.Dispose();
    }
}
