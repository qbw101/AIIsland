using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ClassIsland.AISmartClass.Models;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 统一 AI 后端：封装 HTTP 调用、缓存、重试、降级。
/// 场景方法拆分到 <see cref="AIChatService.Scenarios"/> 和 <see cref="AIChatService.Rules"/>。
/// </summary>
public partial class AIChatService : IDisposable
{
    // ===== 依赖 =====
    private readonly HttpClient _http;
    private readonly FallbackPhraseService _fallback;

    // ===== 缓存 / 并发控制 =====
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _inflightRequests = new();
    private readonly object _settingsLock = new();

    private class CacheEntry
    {
        public string Result { get; set; } = "";
        public DateTime ExpireAt { get; set; }
    }

    // ===== 可配置属性 =====
    public string Endpoint { get; set; } = "https://api.deepseek.com/v1/chat/completions";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "deepseek-chat";
    public int ToneStyle { get; set; } = 1;     // 0=活泼，1=标准，2=严肃
    public int MaxTokens { get; set; } = 200;
    public int TimeoutSeconds { get; set; } = 10;
    public int CacheMinutes { get; set; } = 5;
    public int MaxRetries { get; set; } = 1;

    // ===== 偏好开关（从 AISettings 同步） =====
    public bool EnableCache { get; set; } = true;
    public bool EnableFallback { get; set; } = true;
    public bool UseSeriousToneInExamMode { get; set; } = true;
    public bool IsInExam { get; set; } = false;
    public bool EnableExamModeLocalServer { get; set; } = true;
    public bool ShowConfigStatusOnStartup { get; set; } = true;

    /// <summary>获取实际生效的语气风格（考试模式可覆盖为严肃）</summary>
    public int EffectiveToneStyle =>
        (UseSeriousToneInExamMode && IsInExam) ? 2 : ToneStyle;

    public AIChatService(HttpClient http, FallbackPhraseService fallback)
    {
        _http = http;
        _fallback = fallback;
        _http.Timeout = Timeout.InfiniteTimeSpan;
    }

    /// <summary>从 AISettings 同步配置</summary>
    public void SyncFrom(AISettings settings)
    {
        lock (_settingsLock)
        {
            Endpoint = settings.Endpoint;
            ApiKey = settings.ApiKey;
            Model = settings.Model;
            ToneStyle = settings.ToneStyle;
            MaxTokens = settings.MaxTokens;
            TimeoutSeconds = Math.Clamp(settings.TimeoutSeconds, 3, 120);
            CacheMinutes = settings.CacheMinutes;
            MaxRetries = settings.MaxRetries;
            // 注意：不要在这里修改 _http.Timeout。HttpClient.Timeout 在请求进行中变更会抛
            // InvalidOperationException，多个组件同时调用时极易触发。超时统一用每个请求
            // 独立的 CancellationTokenSource.CancelAfter 控制。

            // 同步偏好开关
            EnableCache = settings.EnableApiCache;
            EnableFallback = settings.EnableFallbackWhenAiUnavailable;
            UseSeriousToneInExamMode = settings.UseSeriousToneInExamMode;
            EnableExamModeLocalServer = settings.EnableExamModeLocalServer;
            ShowConfigStatusOnStartup = settings.ShowConfigStatusOnStartup;
        }

        // 设置变更后清除缓存，确保立即使用新配置重新调用 AI
        _cache.Clear();
        _inflightRequests.Clear();

        // 同步语气风格到降级句子库
        _fallback.ToneStyle = settings.ToneStyle;
    }

    /// <summary>清除全部缓存，供手动重新生成时绕过缓存获取新内容。</summary>
    public void ClearCache()
    {
        _cache.Clear();
        _inflightRequests.Clear();
    }

    /// <summary>根据语气风格获取 temperature</summary>
    private double GetTemperature()
    {
        return EffectiveToneStyle switch
        {
            0 => 1.0,   // 活泼
            1 => 0.7,   // 标准
            2 => 0.3,   // 严肃
            _ => 0.7
        };
    }

    // ========================================
    //  通用聊天接口
    // ========================================

    public async Task<string> ChatAsync(
        string systemPrompt,
        string userMessage,
        double temperature = -1,
        CancellationToken ct = default,
        bool throwOnError = false)
    {
        AiRequestSnapshot snapshot;
        lock (_settingsLock)
        {
            snapshot = new AiRequestSnapshot(
                Endpoint,
                ApiKey,
                Model,
                MaxTokens,
                TimeoutSeconds,
                Math.Max(0, CacheMinutes),
                Math.Clamp(MaxRetries, 0, 5),
                EnableCache,
                EnableFallback,
                temperature >= 0 ? temperature : GetTemperature());
        }

        // 1. API Key 缺失 → 降级或抛出异常
        if (string.IsNullOrWhiteSpace(snapshot.ApiKey))
        {
            if (throwOnError)
                throw new InvalidOperationException("请先配置 AI API Key");
            return snapshot.EnableFallback ? _fallback.GetRandomPhrase("api_key_missing") : "";
        }

        // 2. 检查缓存（受 EnableCache 控制）
        var cacheKey = ComputeCacheKey(systemPrompt, userMessage, snapshot);
        if (snapshot.EnableCache && _cache.TryGetValue(cacheKey, out var cached) && cached.ExpireAt > DateTime.UtcNow)
            return cached.Result;

        // 3. 合并相同请求，避免多个组件同时加载时把同一条 AI 请求并发打出去。
        // 共享任务不传递调用方的 CancellationToken，否则一个调用方取消会导致所有等待方失败。
        // 使用 Task.Run 确保 ChatCoreAsync 在线程池上执行，避免 UI 线程同步上下文死锁。
        var lazyRequest = _inflightRequests.GetOrAdd(cacheKey, _ => new Lazy<Task<string>>(
            () => Task.Run(() => ChatCoreAsync(systemPrompt, userMessage, cacheKey, snapshot, CancellationToken.None, throwOnError)),
            LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await lazyRequest.Value.ConfigureAwait(false);
        }
        finally
        {
            _inflightRequests.TryRemove(cacheKey, out _);
        }
    }

    /// <summary>
    /// 流式聊天接口。每收到一个 token 就通过回调返回当前完整快照，适合 UI 直接替换显示。
    /// 重试时快照会从头开始，避免将中断的半截文本与新响应拼接。
    /// 返回值是最终完整结果。
    /// </summary>
    public async Task<string> ChatStreamAsync(
        string systemPrompt,
        string userMessage,
        Action<string> onUpdate,
        double temperature = -1,
        CancellationToken ct = default)
    {
        if (onUpdate == null) throw new ArgumentNullException(nameof(onUpdate));

        AiRequestSnapshot snapshot;
        lock (_settingsLock)
        {
            snapshot = new AiRequestSnapshot(
                Endpoint,
                ApiKey,
                Model,
                MaxTokens,
                TimeoutSeconds,
                Math.Max(0, CacheMinutes),
                Math.Clamp(MaxRetries, 0, 5),
                EnableCache,
                EnableFallback,
                temperature >= 0 ? temperature : GetTemperature());
        }

        if (string.IsNullOrWhiteSpace(snapshot.ApiKey))
        {
            var fallback = snapshot.EnableFallback ? _fallback.GetRandomPhrase("api_key_missing") : "";
            onUpdate(fallback);
            return fallback;
        }

        var cacheKey = ComputeCacheKey(systemPrompt, userMessage, snapshot);
        if (snapshot.EnableCache && _cache.TryGetValue(cacheKey, out var cached) && cached.ExpireAt > DateTime.UtcNow)
        {
            var replay = new StringBuilder();
            foreach (var ch in cached.Result)
            {
                replay.Append(ch);
                onUpdate(replay.ToString());
                await Task.Delay(8, ct).ConfigureAwait(false);
            }
            return cached.Result;
        }

        for (int attempt = 0; attempt <= snapshot.MaxRetries; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(1000, ct).ConfigureAwait(false);

            try
            {
                var fullResult = new StringBuilder();
                await foreach (var token in SendStreamRequestAsync(systemPrompt, userMessage, snapshot, ct).ConfigureAwait(false))
                {
                    fullResult.Append(token);
                    onUpdate(fullResult.ToString());
                }

                var result = fullResult.ToString().Trim();
                if (string.IsNullOrEmpty(result))
                    throw new InvalidDataException("AI 流式响应内容为空");

                if (snapshot.EnableCache)
                {
                    _cache[cacheKey] = new CacheEntry
                    {
                        Result = result,
                        ExpireAt = DateTime.UtcNow.AddMinutes(Math.Max(1, snapshot.CacheMinutes))
                    };
                }

                if (Random.Shared.Next(20) == 0)
                    CleanExpiredCache();

                return result;
            }
            catch (OperationCanceledException)
            {
                if (ct.IsCancellationRequested) throw;
                Logger.Info($"AI 流式请求超时 (attempt {attempt})");
            }
            catch (Exception ex)
            {
                Logger.Info($"AI 流式请求失败 (attempt {attempt}): {ex.Message}");
            }
        }

        var finalFallback = snapshot.EnableFallback ? _fallback.GetRandomPhrase("api_error") : "";
        onUpdate(finalFallback);
        return finalFallback;
    }

    // ========================================
    //  私有方法
    // ========================================

    private sealed record AiRequestSnapshot(
        string Endpoint,
        string ApiKey,
        string Model,
        int MaxTokens,
        int TimeoutSeconds,
        int CacheMinutes,
        int MaxRetries,
        bool EnableCache,
        bool EnableFallback,
        double Temperature);

    private async Task<string> ChatCoreAsync(
        string systemPrompt,
        string userMessage,
        string cacheKey,
        AiRequestSnapshot snapshot,
        CancellationToken ct,
        bool throwOnError = false)
    {
        Exception? lastException = null;
        for (int attempt = 0; attempt <= snapshot.MaxRetries; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(1000, ct).ConfigureAwait(false);

            try
            {
                var result = await SendRequestAsync(systemPrompt, userMessage, snapshot, ct).ConfigureAwait(false);

                if (snapshot.EnableCache)
                {
                    _cache[cacheKey] = new CacheEntry
                    {
                        Result = result,
                        ExpireAt = DateTime.UtcNow.AddMinutes(Math.Max(1, snapshot.CacheMinutes))
                    };
                }

                if (Random.Shared.Next(20) == 0)
                    CleanExpiredCache();

                return result;
            }
            catch (OperationCanceledException)
            {
                // 调用方主动取消才抛出；请求自身超时属于可重试错误。
                if (ct.IsCancellationRequested) throw;
                Logger.Info($"AI 请求超时 (attempt {attempt})");
                lastException = new TimeoutException("AI 请求超时");
            }
            catch (Exception ex)
            {
                Logger.Info($"AI 请求失败 (attempt {attempt}): {ex.Message}");
                lastException = ex;
            }
        }

        if (throwOnError)
            throw new InvalidOperationException($"AI 请求失败: {lastException?.Message ?? "未知错误"}");
        return snapshot.EnableFallback ? _fallback.GetRandomPhrase("api_error") : "";
    }

    private static string ComputeCacheKey(string system, string user, AiRequestSnapshot snapshot)
    {
        // 使用 SHA256 生成跨进程稳定哈希；包含模型/参数，避免设置变更后复用旧结果。
        var raw = $"{snapshot.Endpoint}|{snapshot.Model}|{snapshot.MaxTokens}|{snapshot.TimeoutSeconds}|{snapshot.Temperature:F2}|{system}|{user}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }

    private void CleanExpiredCache()
    {
        var now = DateTime.UtcNow;
        foreach (var key in _cache.Keys)
        {
            if (_cache.TryGetValue(key, out var entry) && entry.ExpireAt < now)
                _cache.TryRemove(key, out _);
        }
    }

    /// <summary>判断文本是否为 API 缺失或 API 失败时的降级句子。</summary>
    public bool IsFallbackResult(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;

        var apiKeyMissing = _fallback.GetRandomPhrase("api_key_missing");
        var apiError = _fallback.GetRandomPhrase("api_error");
        return string.Equals(text, apiKeyMissing, StringComparison.Ordinal) ||
               string.Equals(text, apiError, StringComparison.Ordinal) ||
               text.Contains("请在设置中配置", StringComparison.Ordinal) ||
               text.Contains("未配置 AI API Key", StringComparison.Ordinal) ||
               text.Contains("还没有配置 AI", StringComparison.Ordinal) ||
               text.Contains("AI 暂时不可用", StringComparison.Ordinal) ||
               text.Contains("AI 服务不可用", StringComparison.Ordinal) ||
               text.Contains("AI 小助手暂时", StringComparison.Ordinal);
    }

    private bool IsFallbackPhrase(string text) => IsFallbackResult(text);

    public void Dispose()
    {
        _cache.Clear();
        _inflightRequests.Clear();
    }
}
