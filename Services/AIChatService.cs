using System.Collections.Concurrent;
using System.Diagnostics;
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
    private readonly AIInvocationLogger _invocationLogger;

    // ===== 缓存 / 并发控制 =====
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<AIChatResult>>> _inflightRequests = new();
    private readonly object _settingsLock = new();
    private long _cacheGeneration;

    private class CacheEntry
    {
        public string Result { get; set; } = "";
        public DateTime ExpireAt { get; set; }
    }

    private sealed record AIChatResult(
        string Content,
        AIRequestDiagnostics? Diagnostics);

    // ===== 可配置属性 =====
    public string Endpoint { get; set; } = "https://api.deepseek.com/chat/completions";
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

    public AIChatService(
        HttpClient http,
        FallbackPhraseService fallback,
        AIInvocationLogger? invocationLogger = null)
    {
        _http = http;
        _fallback = fallback;
        _invocationLogger = invocationLogger ?? new AIInvocationLogger(null);
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

        // 设置变更后清除缓存，确保立即使用新配置重新调用 AI。
        // 先推进代次，阻止设置变更前已经开始的请求在结束后重新写回旧结果。
        Interlocked.Increment(ref _cacheGeneration);
        _cache.Clear();
        _inflightRequests.Clear();

        // 同步语气风格到降级句子库
        _fallback.ToneStyle = settings.ToneStyle;
    }

    /// <summary>清除全部缓存，供手动重新生成时绕过缓存获取新内容。</summary>
    public void ClearCache()
    {
        // 先推进代次，阻止清理前已经开始的请求在结束后重新写回旧结果。
        Interlocked.Increment(ref _cacheGeneration);
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
            {
                await LogInvocationAsync(
                    "通用聊天",
                    systemPrompt,
                    userMessage,
                    "",
                    "错误",
                    false,
                    snapshot.Model,
                    "请先配置 AI API Key").ConfigureAwait(false);
                throw new InvalidOperationException("请先配置 AI API Key");
            }

            var fallback = snapshot.EnableFallback ? _fallback.GetRandomPhrase("api_key_missing") : "";
            await LogInvocationAsync(
                "通用聊天",
                systemPrompt,
                userMessage,
                fallback,
                "本地降级",
                false,
                snapshot.Model,
                "未配置 API Key").ConfigureAwait(false);
            return fallback;
        }

        // 2. 检查缓存（受 EnableCache 控制）
        var cacheKey = ComputeCacheKey(systemPrompt, userMessage, snapshot);
        var cacheGeneration = Volatile.Read(ref _cacheGeneration);
        if (snapshot.EnableCache && _cache.TryGetValue(cacheKey, out var cached) && cached.ExpireAt > DateTime.UtcNow)
        {
            await LogInvocationAsync(
                "通用聊天",
                systemPrompt,
                userMessage,
                cached.Result,
                "缓存",
                false,
                snapshot.Model).ConfigureAwait(false);
            return cached.Result;
        }

        // 3. 合并相同请求，避免多个组件同时加载时把同一条 AI 请求并发打出去。
        // 共享底层请求不传递调用方的 CancellationToken，否则一个调用方取消会导致所有等待方失败；
        // 但当前调用方可通过 WaitAsync 停止等待，让 ClassIsland 自动化正确进入“已取消”状态。
        // 失败时返回降级内容与抛出具体错误是不同执行语义，不能合并为同一个进行中请求。
        var inflightKey = $"{cacheKey}|throwOnError:{throwOnError}";
        var lazyRequest = _inflightRequests.GetOrAdd(
            inflightKey,
            _ => CreateSharedRequest(
                systemPrompt,
                userMessage,
                cacheKey,
                inflightKey,
                cacheGeneration,
                snapshot,
                throwOnError));

        try
        {
            var chatResult = await lazyRequest.Value.WaitAsync(ct).ConfigureAwait(false);
            await LogInvocationAsync(
                "通用聊天",
                systemPrompt,
                userMessage,
                chatResult.Content,
                IsFallbackResult(chatResult.Content) ? "本地降级" : "AI 返回",
                false,
                snapshot.Model,
                chatResult.Diagnostics == null
                    ? null
                    : BuildFailureSummary(chatResult.Diagnostics.Attempts),
                chatResult.Diagnostics).ConfigureAwait(false);
            return chatResult.Content;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await LogInvocationAsync(
                "通用聊天",
                systemPrompt,
                userMessage,
                "",
                "已取消",
                false,
                snapshot.Model,
                "调用方取消等待").ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await LogInvocationAsync(
                "通用聊天",
                systemPrompt,
                userMessage,
                "",
                "错误",
                false,
                snapshot.Model,
                ex.Message,
                ex.Data["AIRequestDiagnostics"] as AIRequestDiagnostics).ConfigureAwait(false);
            throw;
        }
    }

    private Lazy<Task<AIChatResult>> CreateSharedRequest(
        string systemPrompt,
        string userMessage,
        string cacheKey,
        string inflightKey,
        long cacheGeneration,
        AiRequestSnapshot snapshot,
        bool throwOnError)
    {
        Lazy<Task<AIChatResult>>? request = null;
        request = new Lazy<Task<AIChatResult>>(
            async () =>
            {
                try
                {
                    // 使用 Task.Run 确保 ChatCoreAsync 在线程池上执行，避免 UI 线程同步上下文死锁。
                    return await Task.Run(() => ChatCoreAsync(
                            systemPrompt,
                            userMessage,
                            cacheKey,
                            snapshot,
                            CancellationToken.None,
                            throwOnError,
                            cacheGeneration))
                        .ConfigureAwait(false);
                }
                finally
                {
                    // 使用键值对条件删除，避免 ClearCache 后旧请求结束时误删同键的新请求。
                    ((ICollection<KeyValuePair<string, Lazy<Task<AIChatResult>>>>)_inflightRequests)
                        .Remove(new KeyValuePair<string, Lazy<Task<AIChatResult>>>(inflightKey, request!));
                }
            },
            LazyThreadSafetyMode.ExecutionAndPublication);
        return request;
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
            await LogInvocationAsync(
                "通用流式聊天",
                systemPrompt,
                userMessage,
                fallback,
                "本地降级",
                true,
                snapshot.Model,
                "未配置 API Key").ConfigureAwait(false);
            return fallback;
        }

        var cacheKey = ComputeCacheKey(systemPrompt, userMessage, snapshot);
        var cacheGeneration = Volatile.Read(ref _cacheGeneration);
        if (snapshot.EnableCache && _cache.TryGetValue(cacheKey, out var cached) && cached.ExpireAt > DateTime.UtcNow)
        {
            var replay = new StringBuilder();
            foreach (var ch in cached.Result)
            {
                replay.Append(ch);
                onUpdate(replay.ToString());
                await Task.Delay(8, ct).ConfigureAwait(false);
            }
            await LogInvocationAsync(
                "通用流式聊天",
                systemPrompt,
                userMessage,
                cached.Result,
                "缓存",
                true,
                snapshot.Model).ConfigureAwait(false);
            return cached.Result;
        }

        var failures = new List<AIRequestFailureInfo>();
        var totalStopwatch = Stopwatch.StartNew();
        for (int attempt = 0; attempt <= snapshot.MaxRetries; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(1000, ct).ConfigureAwait(false);

            var attemptStopwatch = Stopwatch.StartNew();
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

                if (snapshot.EnableCache &&
                    cacheGeneration == Volatile.Read(ref _cacheGeneration))
                {
                    _cache[cacheKey] = new CacheEntry
                    {
                        Result = result,
                        ExpireAt = DateTime.UtcNow.AddMinutes(Math.Max(1, snapshot.CacheMinutes))
                    };
                }

                if (Random.Shared.Next(20) == 0)
                    CleanExpiredCache();

                await LogInvocationAsync(
                    "通用流式聊天",
                    systemPrompt,
                    userMessage,
                    result,
                    "AI 返回",
                    true,
                    snapshot.Model).ConfigureAwait(false);
                return result;
            }
            catch (OperationCanceledException ex)
            {
                attemptStopwatch.Stop();
                if (ct.IsCancellationRequested)
                {
                    var canceled = AIRequestFailureClassifier.Classify(
                        ex,
                        attempt + 1,
                        attemptStopwatch.ElapsedMilliseconds,
                        callerCanceled: true);
                    failures.Add(canceled);
                    totalStopwatch.Stop();
                    await LogInvocationAsync(
                        "通用流式聊天",
                        systemPrompt,
                        userMessage,
                        "",
                        "已取消",
                        true,
                        snapshot.Model,
                        "调用方取消",
                        CreateDiagnostics(snapshot, failures, totalStopwatch.ElapsedMilliseconds)).ConfigureAwait(false);
                    throw;
                }

                var timeout = new TimeoutException(
                    $"本地等待 API 响应超过 {snapshot.TimeoutSeconds} 秒",
                    ex);
                var failure = AIRequestFailureClassifier.Classify(
                    timeout,
                    attempt + 1,
                    attemptStopwatch.ElapsedMilliseconds);
                failures.Add(failure);
                Logger.Info($"AI 流式请求超时 (attempt {attempt + 1})");
                if (!failure.IsRetryable)
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                attemptStopwatch.Stop();
                var failure = AIRequestFailureClassifier.Classify(
                    ex,
                    attempt + 1,
                    attemptStopwatch.ElapsedMilliseconds);
                failures.Add(failure);
                Logger.Info($"AI 流式请求失败 (attempt {attempt + 1}): {failure.Category}: {failure.Message}");
                if (!failure.IsRetryable)
                {
                    break;
                }
            }
        }

        totalStopwatch.Stop();
        var finalFallback = snapshot.EnableFallback ? _fallback.GetRandomPhrase("api_error") : "";
        onUpdate(finalFallback);
        await LogInvocationAsync(
            "通用流式聊天",
            systemPrompt,
            userMessage,
            finalFallback,
            "本地降级",
            true,
            snapshot.Model,
            BuildFailureSummary(failures),
            CreateDiagnostics(snapshot, failures, totalStopwatch.ElapsedMilliseconds)).ConfigureAwait(false);
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

    private async Task<AIChatResult> ChatCoreAsync(
        string systemPrompt,
        string userMessage,
        string cacheKey,
        AiRequestSnapshot snapshot,
        CancellationToken ct,
        bool throwOnError = false,
        long cacheGeneration = 0)
    {
        Exception? lastException = null;
        var failures = new List<AIRequestFailureInfo>();
        var totalStopwatch = Stopwatch.StartNew();
        for (int attempt = 0; attempt <= snapshot.MaxRetries; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(1000, ct).ConfigureAwait(false);

            var attemptStopwatch = Stopwatch.StartNew();
            try
            {
                var result = await SendRequestAsync(systemPrompt, userMessage, snapshot, ct).ConfigureAwait(false);

                if (snapshot.EnableCache &&
                    cacheGeneration == Volatile.Read(ref _cacheGeneration))
                {
                    _cache[cacheKey] = new CacheEntry
                    {
                        Result = result,
                        ExpireAt = DateTime.UtcNow.AddMinutes(Math.Max(1, snapshot.CacheMinutes))
                    };
                }

                if (Random.Shared.Next(20) == 0)
                    CleanExpiredCache();

                totalStopwatch.Stop();
                return new AIChatResult(result, null);
            }
            catch (OperationCanceledException ex)
            {
                attemptStopwatch.Stop();
                // 调用方主动取消才抛出；请求自身超时属于可重试错误。
                if (ct.IsCancellationRequested) throw;
                lastException = new TimeoutException(
                    $"本地等待 API 响应超过 {snapshot.TimeoutSeconds} 秒",
                    ex);
                var failure = AIRequestFailureClassifier.Classify(
                    lastException,
                    attempt + 1,
                    attemptStopwatch.ElapsedMilliseconds);
                failures.Add(failure);
                Logger.Info($"AI 请求超时 (attempt {attempt + 1})");
                if (!failure.IsRetryable)
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                attemptStopwatch.Stop();
                lastException = ex;
                var failure = AIRequestFailureClassifier.Classify(
                    ex,
                    attempt + 1,
                    attemptStopwatch.ElapsedMilliseconds);
                failures.Add(failure);
                Logger.Info($"AI 请求失败 (attempt {attempt + 1}): {failure.Category}: {failure.Message}");
                if (!failure.IsRetryable)
                {
                    break;
                }
            }
        }

        totalStopwatch.Stop();
        var diagnostics = CreateDiagnostics(snapshot, failures, totalStopwatch.ElapsedMilliseconds);
        if (throwOnError)
        {
            var exception = new InvalidOperationException(
                $"AI 请求失败: {lastException?.Message ?? "未知错误"}",
                lastException);
            exception.Data["AIRequestDiagnostics"] = diagnostics;
            throw exception;
        }

        return new AIChatResult(
            snapshot.EnableFallback ? _fallback.GetRandomPhrase("api_error") : "",
            diagnostics);
    }

    internal Task LogLocalResultAsync(
        string scenario,
        string? systemPrompt,
        string? userPrompt,
        string result,
        bool isStreaming = false,
        string? reason = null)
    {
        return LogInvocationAsync(
            scenario,
            systemPrompt,
            userPrompt,
            result,
            "本地降级",
            isStreaming,
            Model,
            reason);
    }

    private Task LogInvocationAsync(
        string scenario,
        string? systemPrompt,
        string? userPrompt,
        string result,
        string resultType,
        bool isStreaming,
        string? model,
        string? error = null,
        AIRequestDiagnostics? diagnostics = null)
    {
        return _invocationLogger.WriteAsync(
            scenario,
            systemPrompt,
            userPrompt,
            result,
            resultType,
            isStreaming,
            model,
            error,
            diagnostics);
    }

    private static AIRequestDiagnostics CreateDiagnostics(
        AiRequestSnapshot snapshot,
        IReadOnlyList<AIRequestFailureInfo> failures,
        long totalDurationMs)
    {
        return new AIRequestDiagnostics(
            AIRequestFailureClassifier.SanitizeEndpoint(snapshot.Endpoint),
            failures.Count,
            totalDurationMs,
            failures);
    }

    private static string BuildFailureSummary(IReadOnlyList<AIRequestFailureInfo> failures)
    {
        if (failures.Count == 0)
        {
            return "AI 请求失败，未获得详细异常";
        }

        var final = failures[^1];
        return $"{final.Source} / {final.Category}: {final.Message}（共尝试 {failures.Count} 次）";
    }

    private static string ComputeCacheKey(string system, string user, AiRequestSnapshot snapshot)
    {
        // 使用 SHA256 生成跨进程稳定哈希；包含模型/参数，避免设置变更后复用旧结果。
        var raw = $"{snapshot.Endpoint}|{snapshot.Model}|{snapshot.MaxTokens}|{snapshot.TimeoutSeconds}|{snapshot.Temperature:R}|{system}|{user}";
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
