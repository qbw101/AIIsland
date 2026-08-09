using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 将每次 AI 调用及本地降级结果按天追加到插件数据目录，便于排查提示词与返回内容。
/// 日志写入失败不会影响正常 AI 调用。
/// </summary>
public sealed class AIInvocationLogger : IDisposable
{
    private readonly string? _logDirectory;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private int _disposed;
    private int _retentionDays = 30;
    private int _lastCleanupDayNumber = int.MinValue;

    public AIInvocationLogger(string? logDirectory)
    {
        _logDirectory = string.IsNullOrWhiteSpace(logDirectory) ? null : logDirectory;
    }

    /// <summary>AI 调用日志保留天数，范围为 1–365 天。</summary>
    public int RetentionDays
    {
        get => Volatile.Read(ref _retentionDays);
        set
        {
            var normalized = Math.Clamp(value, 1, 365);
            if (Interlocked.Exchange(ref _retentionDays, normalized) != normalized)
            {
                Interlocked.Exchange(ref _lastCleanupDayNumber, int.MinValue);
            }
        }
    }

    /// <summary>立即清理超过保留期的按日日志；清理失败不会影响 AI 调用。</summary>
    public async Task CleanupExpiredLogsAsync(DateTimeOffset? now = null)
    {
        if (_logDirectory == null || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            await _writeGate.WaitAsync().ConfigureAwait(false);
            try
            {
                CleanupExpiredLogsCore(now ?? DateTimeOffset.Now);
            }
            finally
            {
                _writeGate.Release();
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "清理过期 AI 调用日志失败");
        }
    }

    public async Task WriteAsync(
        string scenario,
        string? systemPrompt,
        string? userPrompt,
        string result,
        string resultType,
        bool isStreaming = false,
        string? model = null,
        string? error = null,
        AIRequestDiagnostics? diagnostics = null)
    {
        if (_logDirectory == null || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var entry = new AIInvocationLogEntry(
            DateTimeOffset.Now,
            scenario,
            resultType,
            isStreaming,
            model,
            systemPrompt,
            userPrompt,
            result,
            error,
            diagnostics);

        try
        {
            await _writeGate.WaitAsync().ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(_logDirectory);
                var today = DateOnly.FromDateTime(entry.Timestamp.LocalDateTime);
                if (Volatile.Read(ref _lastCleanupDayNumber) != today.DayNumber)
                {
                    CleanupExpiredLogsCore(entry.Timestamp);
                }

                var path = Path.Combine(_logDirectory, $"{entry.Timestamp:yyyy-MM-dd}.jsonl");
                var json = JsonSerializer.Serialize(entry, _jsonOptions);
                await File.AppendAllTextAsync(path, json + Environment.NewLine).ConfigureAwait(false);
            }
            finally
            {
                _writeGate.Release();
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "写入 AI 调用日志失败");
        }
    }

    private void CleanupExpiredLogsCore(DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.DateTime);

        if (!Directory.Exists(_logDirectory))
        {
            return;
        }

        var cutoffDate = today.AddDays(-(RetentionDays - 1));
        foreach (var path in Directory.EnumerateFiles(_logDirectory, "*.jsonl", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (!DateOnly.TryParseExact(
                    fileName,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var logDate) ||
                logDate >= cutoffDate)
            {
                continue;
            }

            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"删除过期 AI 调用日志失败: {path}");
            }
        }

        Interlocked.Exchange(ref _lastCleanupDayNumber, today.DayNumber);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _disposed, 1);
    }

    private sealed record AIInvocationLogEntry(
        DateTimeOffset Timestamp,
        string Scenario,
        string ResultType,
        bool IsStreaming,
        string? Model,
        string? SystemPrompt,
        string? UserPrompt,
        string Result,
        string? Error,
        AIRequestDiagnostics? Diagnostics);
}
