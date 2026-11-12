using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ClassIsland.AISmartClass.Models;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 一次 AI 调用的完整结果：正文 + 思考过程。
/// 思考过程只有开启深度思考且厂商返回 <c>reasoning_content</c> 时才有内容。
/// </summary>
public readonly record struct AIChatResponse(string Content, string Reasoning)
{
    /// <summary>是否有正文。</summary>
    public bool HasContent => !string.IsNullOrWhiteSpace(Content);

    /// <summary>是否拿到了思考过程。</summary>
    public bool HasReasoning => !string.IsNullOrWhiteSpace(Reasoning);

    /// <summary>
    /// 模型请求执行的工具调用。为空表示这一轮不需要工具。
    /// 用 init 属性而非位置参数，保持现有两参构造与解构完全兼容；
    /// 类型仅程序集内可见，不进入公开 API 面。
    /// </summary>
    internal IReadOnlyList<ChatToolCall> ToolCalls { get; init; } = Array.Empty<ChatToolCall>();

    /// <summary>
    /// <see cref="ToolCalls"/> 是否来自正文里的文本标记（<c>✿FUNCTION✿</c>）而非结构化
    /// <c>tool_calls</c>。这类调用回传结果时不能用 <c>role:tool</c>，要沿用模型自己的文本协议。
    /// </summary>
    internal bool TextualToolCalls { get; init; }
}

/// <summary>
/// 流式回调的一次推送：本次增量 + 截至目前的完整快照。
/// 同一次推送里正文与思考过程通常只有一段有增量。
/// </summary>
public readonly record struct AIChatStreamUpdate(
    string ContentDelta,
    string ReasoningDelta,
    string Content,
    string Reasoning)
{
    /// <summary>
    /// 阶段状态。<c>"searching"</c> 表示模型要求联网、正在执行检索；
    /// <c>"answering"</c> 表示检索完成、开始生成最终回答（调用方应清空此前已显示的内容）；
    /// 空字符串表示普通输出。
    /// </summary>
    public string Status { get; init; } = "";

    /// <summary>
    /// 阶段补充信息。目前只在 <see cref="Status"/> 为 <c>"searching"</c> 时携带
    /// 模型实际使用的检索关键词（多个用「；」分隔），便于调用方显示「正在搜索：xxx」。
    /// </summary>
    public string SearchQuery { get; init; } = "";
}

/// <summary>
/// 统一 AI 后端：封装 HTTP 调用、缓存、重试、降级。
/// 场景方法拆分到 <see cref="AIChatService.Scenarios"/> 和 <see cref="AIChatService.Rules"/>。
/// </summary>
public partial class AIChatService : IDisposable
{
    // ===== 依赖 =====
    private readonly HttpClient _http;
    private readonly AiErrorPhraseService _fallback;
    private readonly AIInvocationLogger _invocationLogger;

    // ===== 缓存 / 并发控制 =====
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<AIChatResult>>> _inflightRequests = new();
    private readonly object _settingsLock = new();
    private long _cacheGeneration;

    private class CacheEntry
    {
        public string Result { get; set; } = "";
        public string Reasoning { get; set; } = "";
        public DateTime ExpireAt { get; set; }
    }

    private sealed record AIChatResult(
        string Content,
        string Reasoning,
        AIRequestDiagnostics? Diagnostics);

    // ===== 可配置属性 =====
    public string Endpoint { get; set; } = "https://api.deepseek.com/chat/completions";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "deepseek-chat";
    public int ToneStyle { get; set; } = 1;     // 0=活泼，1=标准，2=严肃
    public double? CustomTemperature { get; set; } = null; // 自定义温度，为null时使用语气风格映射
    public bool PersonaEnabled { get; set; } = false;      // 是否启用角色语气
    public string PersonaPrompt { get; set; } = "";        // 已组装的角色设定前缀（空=未启用）
    /// <summary>单次回复输出上限；0 表示不限制（不下发 max_tokens）。</summary>
    public int MaxTokens { get; set; } = 200;
    /// <summary>单次请求超时秒数；0 表示不设置超时。</summary>
    public int TimeoutSeconds { get; set; } = 10;
    public int CacheMinutes { get; set; } = 5;
    public int MaxRetries { get; set; } = 1;

    /// <summary>是否开启深度思考（推理模式）。</summary>
    public bool EnableDeepThinking { get; set; } = false;

    /// <summary>思考强度，仅 <see cref="EnableDeepThinking"/> 为 true 时下发。</summary>
    public ThinkingEffort ThinkingEffort { get; set; } = ThinkingEffort.Auto;

    // ===== 偏好开关（从 AISettings 同步） =====
    public bool EnableCache { get; set; } = true;

    /// <summary>是否允许模型调用 web_fetch 打开网页读取正文。</summary>
    public bool EnableWebFetch { get; set; } = true;
    public bool UseSeriousToneInExamMode { get; set; } = true;
    public bool IsInExam { get; set; } = false;
    public bool EnableExamModeLocalServer { get; set; } = true;
    public bool ShowConfigStatusOnStartup { get; set; } = true;

    /// <summary>
    /// 当前模型的图片识别能力，随设置同步。图片请求只在该值明确为「不支持」时被拦截，
    /// 按名称推断的结果不作为拦截依据。
    /// </summary>
    public VisionCapabilityInfo VisionCapability { get; set; } = VisionCapabilityInfo.Unknown;

    /// <summary>获取实际生效的语气风格（考试模式可覆盖为严肃）</summary>
    public int EffectiveToneStyle =>
        (UseSeriousToneInExamMode && IsInExam) ? 2 : ToneStyle;

    /// <summary>
    /// 在生成类场景的 system prompt 前拼接角色语气前缀。
    /// 未启用 / 无有效角色 / 考试强制严肃时原样返回，不引入角色设定。
    /// </summary>
    internal string ApplyPersona(string basePrompt)
    {
        if (!PersonaEnabled || (IsInExam && UseSeriousToneInExamMode)) return basePrompt;
        if (string.IsNullOrWhiteSpace(PersonaPrompt)) return basePrompt;
        return $"{PersonaPrompt}\n\n{basePrompt}";
    }

    public AIChatService(
        HttpClient http,
        AiErrorPhraseService fallback,
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
            CustomTemperature = settings.CustomTemperature;
            PersonaEnabled = settings.PersonaEnabled;
            PersonaPrompt = PersonaCatalog.BuildPersonaPrompt(settings);
            MaxTokens = Math.Max(0, settings.MaxTokens);
            // 0（或负数）= 不设置超时，一直等到模型返回或调用方取消
            TimeoutSeconds = settings.TimeoutSeconds <= 0
                ? 0
                : Math.Clamp(settings.TimeoutSeconds, 1, 120);
            CacheMinutes = settings.CacheMinutes;
            MaxRetries = settings.MaxRetries;
            EnableDeepThinking = settings.EnableDeepThinking;
            ThinkingEffort = settings.ThinkingEffort;
            // 注意：不要在这里修改 _http.Timeout。HttpClient.Timeout 在请求进行中变更会抛
            // InvalidOperationException，多个组件同时调用时极易触发。超时统一用每个请求
            // 独立的 CancellationTokenSource.CancelAfter 控制。

            // 同步偏好开关
            EnableCache = settings.EnableApiCache;
            EnableWebFetch = settings.EnableWebFetch;
            UseSeriousToneInExamMode = settings.UseSeriousToneInExamMode;
            EnableExamModeLocalServer = settings.EnableExamModeLocalServer;
            ShowConfigStatusOnStartup = settings.ShowConfigStatusOnStartup;
            VisionCapability = VisionCapabilityService.Evaluate(settings);
        }

        // 设置变更后清除缓存，确保立即使用新配置重新调用 AI。
        // 先推进代次，阻止设置变更前已经开始的请求在结束后重新写回旧结果。
        Interlocked.Increment(ref _cacheGeneration);
        _cache.Clear();
        _inflightRequests.Clear();

        // 同步联网搜索配置（开关、OpenSERP 地址、默认引擎等）
        WebSearch?.SyncFrom(settings);
    }

    /// <summary>清除全部缓存，供手动重新生成时绕过缓存获取新内容。</summary>
    public void ClearCache()
    {
        // 先推进代次，阻止清理前已经开始的请求在结束后重新写回旧结果。
        Interlocked.Increment(ref _cacheGeneration);
        _cache.Clear();
        _inflightRequests.Clear();
    }

    /// <summary>获取 temperature 值：自定义温度优先，未设置时按语气风格映射。</summary>
    private double GetTemperature()
    {
        // 如果用户设置了自定义温度，使用自定义值（限制在0.0-2.0范围内）
        if (CustomTemperature.HasValue)
        {
            return Math.Clamp(CustomTemperature.Value, 0.0, 2.0);
        }

        // 未自定义时按语气风格映射：活泼高创造性、标准平衡、严肃稳定准确
        return EffectiveToneStyle switch
        {
            0 => 1.0,
            1 => 0.7,
            2 => 0.3,
            _ => 0.7
        };
    }

    // ========================================
    //  通用聊天接口
    // ========================================

    /// <summary>通用聊天接口。</summary>
    /// <param name="bypassCache">
    /// true 时本次调用既不读缓存也不写缓存，用于「重新生成」这类明确要求每次都是新结果的场景
    /// （否则相同提示词会命中缓存原样返回）。
    /// </param>
    public async Task<string> ChatAsync(
        string systemPrompt,
        string userMessage,
        double temperature = -1,
        CancellationToken ct = default,
        bool throwOnError = false,
        bool bypassCache = false,
        bool? enableDeepThinking = null,
        ThinkingEffort? thinkingEffort = null)
    {
        var response = await ExecuteChatAsync(
            systemPrompt, userMessage, null, "通用聊天",
            temperature, ct, throwOnError, bypassCache, enableDeepThinking, thinkingEffort)
            .ConfigureAwait(false);
        return response.Content;
    }

    /// <summary>
    /// 与 <see cref="ChatAsync"/> 相同，但同时返回思考过程（<c>reasoning_content</c>）。
    /// 开启深度思考后可用它拿到模型的推理内容。
    /// </summary>
    public Task<AIChatResponse> ChatResponseAsync(
        string systemPrompt,
        string userMessage,
        double temperature = -1,
        CancellationToken ct = default,
        bool throwOnError = false,
        bool bypassCache = false,
        bool? enableDeepThinking = null,
        ThinkingEffort? thinkingEffort = null)
    {
        return ExecuteChatAsync(
            systemPrompt, userMessage, null, "通用聊天",
            temperature, ct, throwOnError, bypassCache, enableDeepThinking, thinkingEffort);
    }

    /// <summary>
    /// 文本对话与图片识别共用的执行管线：缓存、并发合并、重试、降级与调用日志。
    /// <paramref name="image"/> 为 null 时是纯文本请求。
    /// </summary>
    private async Task<AIChatResponse> ExecuteChatAsync(
        string systemPrompt,
        string userMessage,
        VisionImage? image,
        string scenario,
        double temperature,
        CancellationToken ct,
        bool throwOnError,
        bool bypassCache,
        bool? enableDeepThinking = null,
        ThinkingEffort? thinkingEffort = null)
    {
        AiRequestSnapshot snapshot;
        lock (_settingsLock)
        {
            var thinkingEnabled = enableDeepThinking ?? EnableDeepThinking;
            var effort = thinkingEffort ?? ThinkingEffort;
            snapshot = new AiRequestSnapshot(
                Endpoint,
                ApiKey,
                Model,
                ThinkingRequestBuilder.GetEffectiveMaxTokens(
                    MaxTokens, thinkingEnabled, effort),
                TimeoutSeconds,
                Math.Max(0, CacheMinutes),
                Math.Clamp(MaxRetries, 0, 5),
                EnableCache && !bypassCache,
                temperature >= 0 ? temperature : GetTemperature(),
                thinkingEnabled,
                effort);
        }

        // 1. API Key 缺失 → 降级或抛出异常
        if (string.IsNullOrWhiteSpace(snapshot.ApiKey))
        {
            if (throwOnError)
            {
                await LogInvocationAsync(
                    scenario,
                    systemPrompt,
                    userMessage,
                    "",
                    "错误",
                    false,
                    snapshot.Model,
                    "请先配置 AI API Key").ConfigureAwait(false);
                throw new InvalidOperationException("请先配置 AI API Key");
            }

            var fallback = _fallback.GetErrorPhraseFromReason("未配置 API Key，请在设置中填写");
            await LogInvocationAsync(
                scenario,
                systemPrompt,
                userMessage,
                fallback,
                "本地降级",
                false,
                snapshot.Model,
                "未配置 API Key").ConfigureAwait(false);
            return new AIChatResponse(fallback, "");
        }

        // 2. 检查缓存（受 EnableCache 控制）
        var cacheKey = ComputeCacheKey(systemPrompt, userMessage, snapshot, image);
        var cacheGeneration = Volatile.Read(ref _cacheGeneration);
        if (snapshot.EnableCache && _cache.TryGetValue(cacheKey, out var cached) && cached.ExpireAt > DateTime.UtcNow)
        {
            await LogInvocationAsync(
                scenario,
                systemPrompt,
                userMessage,
                cached.Result,
                "缓存",
                false,
                snapshot.Model).ConfigureAwait(false);
            return new AIChatResponse(cached.Result, cached.Reasoning);
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
                image,
                cacheKey,
                inflightKey,
                cacheGeneration,
                snapshot,
                throwOnError));

        try
        {
            var chatResult = await lazyRequest.Value.WaitAsync(ct).ConfigureAwait(false);
            await LogInvocationAsync(
                scenario,
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
            return new AIChatResponse(chatResult.Content, chatResult.Reasoning);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await LogInvocationAsync(
                scenario,
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
                scenario,
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
        VisionImage? image,
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
                            image,
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

    /// <summary>流式响应的一个增量片段：正文、思考过程或工具调用分片。</summary>
    private readonly record struct AIChatStreamChunk(string Content, string Reasoning)
    {
        /// <summary>本轮的工具调用增量；非空表示模型正在请求调用工具。</summary>
        public IReadOnlyList<ChatToolCallDelta> ToolCallDeltas { get; init; } =
            Array.Empty<ChatToolCallDelta>();
    }

    /// <summary>
    /// 流式回调自身抛出的异常。它不是 AI 请求失败，不应进入重试与降级，
    /// 而是直接透传给调用方（对外表现为调用失败）。
    /// </summary>
    private sealed class StreamCallbackException : Exception
    {
        public StreamCallbackException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// 流式聊天接口。每收到一个 token 就通过回调返回当前完整快照，适合 UI 直接替换显示。
    /// 重试时快照会从头开始，避免将中断的半截文本与新响应拼接。
    /// 返回值是最终正文。
    /// </summary>
    public async Task<string> ChatStreamAsync(
        string systemPrompt,
        string userMessage,
        Action<string> onUpdate,
        double temperature = -1,
        CancellationToken ct = default)
    {
        if (onUpdate == null) throw new ArgumentNullException(nameof(onUpdate));

        var response = await ChatStreamResponseAsync(
            systemPrompt,
            userMessage,
            update => onUpdate(update.Content),
            temperature,
            ct).ConfigureAwait(false);
        return response.Content;
    }

    /// <summary>
    /// 流式聊天的完整版本：回调同时给出正文与思考过程的增量，最终返回两者。
    /// 传入 <paramref name="image"/> 时为带图片的流式对话（多模态）。
    /// </summary>
    /// <param name="onUpdate">增量回调。注意它在线程池线程上执行，UI 场景请自行切回 UI 线程。</param>
    /// <param name="image">可选图片，随用户消息一起发送。</param>
    public async Task<AIChatResponse> ChatStreamResponseAsync(
        string systemPrompt,
        string userMessage,
        Action<AIChatStreamUpdate> onUpdate,
        double temperature = -1,
        CancellationToken ct = default,
        bool? enableDeepThinking = null,
        ThinkingEffort? thinkingEffort = null,
        VisionImage? image = null)
    {
        if (onUpdate == null) throw new ArgumentNullException(nameof(onUpdate));

        // 回调抛出的异常不属于 AI 请求失败：包装后绕过重试与降级，直接透传给调用方
        void Emit(AIChatStreamUpdate update)
        {
            try
            {
                onUpdate(update);
            }
            catch (Exception ex)
            {
                throw new StreamCallbackException(ex.Message, ex);
            }
        }

        var thinkingEnabled = enableDeepThinking ?? EnableDeepThinking;
        var effort = thinkingEffort ?? ThinkingEffort;

        AiRequestSnapshot snapshot;
        lock (_settingsLock)
        {
            snapshot = new AiRequestSnapshot(
                Endpoint,
                ApiKey,
                Model,
                ThinkingRequestBuilder.GetEffectiveMaxTokens(
                    MaxTokens, thinkingEnabled, effort),
                TimeoutSeconds,
                Math.Max(0, CacheMinutes),
                Math.Clamp(MaxRetries, 0, 5),
                EnableCache,
                temperature >= 0 ? temperature : GetTemperature(),
                thinkingEnabled,
                effort);
        }

        if (string.IsNullOrWhiteSpace(snapshot.ApiKey))
        {
            var fallback = _fallback.GetErrorPhraseFromReason("未配置 API Key，请在设置中填写");
            Emit(new AIChatStreamUpdate(fallback, "", fallback, ""));
            await LogInvocationAsync(
                "通用流式聊天",
                systemPrompt,
                userMessage,
                fallback,
                "本地降级",
                true,
                snapshot.Model,
                "未配置 API Key").ConfigureAwait(false);
            return new AIChatResponse(fallback, "");
        }

        var cacheKey = ComputeCacheKey(systemPrompt, userMessage, snapshot, image);
        var cacheGeneration = Volatile.Read(ref _cacheGeneration);
        if (snapshot.EnableCache && _cache.TryGetValue(cacheKey, out var cached) && cached.ExpireAt > DateTime.UtcNow)
        {
            var replay = new StringBuilder();
            foreach (var ch in cached.Result)
            {
                replay.Append(ch);
                Emit(new AIChatStreamUpdate(ch.ToString(), "", replay.ToString(), cached.Reasoning));
                await Task.Delay(8, ct).ConfigureAwait(false);
            }

            if (cached.Result.Length == 0 && cached.Reasoning.Length > 0)
                Emit(new AIChatStreamUpdate("", "", "", cached.Reasoning));

            await LogInvocationAsync(
                "通用流式聊天",
                systemPrompt,
                userMessage,
                cached.Result,
                "缓存",
                true,
                snapshot.Model).ConfigureAwait(false);
            return new AIChatResponse(cached.Result, cached.Reasoning);
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
                var contentBuilder = new StringBuilder();
                var reasoningBuilder = new StringBuilder();
                var toolAccumulator = new ToolCallAccumulator();
                await foreach (var chunk in SendStreamRequestAsync(systemPrompt, userMessage, image, snapshot, ct).ConfigureAwait(false))
                {
                    contentBuilder.Append(chunk.Content);
                    reasoningBuilder.Append(chunk.Reasoning);
                    foreach (var toolDelta in chunk.ToolCallDeltas)
                        toolAccumulator.Append(toolDelta);

                    Emit(new AIChatStreamUpdate(
                        chunk.Content,
                        chunk.Reasoning,
                        contentBuilder.ToString(),
                        reasoningBuilder.ToString()));
                }

                var result = contentBuilder.ToString().Trim();
                var reasoning = reasoningBuilder.ToString().Trim();
                // 三者都为空才算失败：思考型模型可能只回思考内容，工具调用轮次可能只有 tool_calls
                if (string.IsNullOrEmpty(result) && string.IsNullOrEmpty(reasoning) && toolAccumulator.IsEmpty)
                    throw new InvalidDataException("AI 流式响应内容为空");

                // 带工具调用的轮次涉及实时信息，不写缓存（缓存结构也不承载 tool_calls）
                if (snapshot.EnableCache &&
                    toolAccumulator.IsEmpty &&
                    cacheGeneration == Volatile.Read(ref _cacheGeneration))
                {
                    _cache[cacheKey] = new CacheEntry
                    {
                        Result = result,
                        Reasoning = reasoning,
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
                return new AIChatResponse(result, reasoning)
                {
                    ToolCalls = toolAccumulator.Build()
                };
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
                    snapshot.TimeoutSeconds > 0
                        ? $"本地等待 API 响应超过 {snapshot.TimeoutSeconds} 秒"
                        : "请求在未设置超时的情况下被中断",
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
            catch (StreamCallbackException)
            {
                // 回调自身出错：不重试、不降级，直接透传给调用方
                throw;
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
        var finalFallback = _fallback.GetErrorPhrase(failures.Count > 0 ? failures[^1] : null);
        onUpdate(new AIChatStreamUpdate(finalFallback, "", finalFallback, ""));
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
        return new AIChatResponse(finalFallback, "");
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
        double Temperature,
        bool EnableDeepThinking,
        ThinkingEffort ThinkingEffort);

    private async Task<AIChatResult> ChatCoreAsync(
        string systemPrompt,
        string userMessage,
        VisionImage? image,
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
                var result = await SendRequestAsync(systemPrompt, userMessage, image, snapshot, ct).ConfigureAwait(false);

                if (snapshot.EnableCache &&
                    cacheGeneration == Volatile.Read(ref _cacheGeneration))
                {
                    _cache[cacheKey] = new CacheEntry
                    {
                        Result = result.Content,
                        Reasoning = result.Reasoning,
                        ExpireAt = DateTime.UtcNow.AddMinutes(Math.Max(1, snapshot.CacheMinutes))
                    };
                }

                if (Random.Shared.Next(20) == 0)
                    CleanExpiredCache();

                totalStopwatch.Stop();
                return new AIChatResult(result.Content, result.Reasoning, null);
            }
            catch (OperationCanceledException ex)
            {
                attemptStopwatch.Stop();
                // 调用方主动取消才抛出；请求自身超时属于可重试错误。
                if (ct.IsCancellationRequested) throw;
                lastException = new TimeoutException(
                    snapshot.TimeoutSeconds > 0
                        ? $"本地等待 API 响应超过 {snapshot.TimeoutSeconds} 秒"
                        : "请求在未设置超时的情况下被中断",
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
            _fallback.GetErrorPhrase(failures.Count > 0 ? failures[^1] : null),
            "",
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

    private static string ComputeCacheKey(string system, string user, AiRequestSnapshot snapshot, VisionImage? image = null)
    {
        // 使用 SHA256 生成跨进程稳定哈希；包含模型/参数，避免设置变更后复用旧结果。
        var raw = $"{snapshot.Endpoint}|{snapshot.Model}|{snapshot.MaxTokens}|{snapshot.TimeoutSeconds}|{snapshot.Temperature:R}" +
                  $"|thinking:{snapshot.EnableDeepThinking}:{snapshot.ThinkingEffort}|{system}|{user}";
        // 图片请求把图片内容一起纳入哈希，避免同一句提问配不同图片时命中旧缓存。
        if (image != null)
            raw += $"|image:{Convert.ToHexString(SHA256.HashData(image.Data))}";
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

    /// <summary>判断文本是否为 AI 失败时生成的报错文案。</summary>
    public bool IsFallbackResult(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;

        // 新文案统一以固定前缀开头，具体原因无法逐条枚举。
        if (text.StartsWith(AiErrorPhraseService.ErrorPrefix, StringComparison.Ordinal))
            return true;

        // 兼容历史缓存与旧日志中残留的降级文案。
        return text.Contains("请在设置中配置", StringComparison.Ordinal) ||
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
