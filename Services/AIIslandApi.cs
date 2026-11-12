using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ClassIsland.AISmartClass.Models;
using ClassIsland.AISmartClass.PublicApi;
using ClassIsland.AISmartClass.Services.WebSearch;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// IAIIslandApi 的实现。包装 AIChatService，所有外部调用经过 AuthGuard 授权检查。
/// </summary>
public class AIIslandApi : IAIIslandApi
{
    private readonly AIChatService _aiService;
    private readonly AuthGuard _authGuard;
    private readonly ReminderParserService _reminderParser;

    internal AIIslandApi(AIChatService aiService, AuthGuard authGuard, ReminderParserService reminderParser)
    {
        _aiService = aiService;
        _authGuard = authGuard;
        _reminderParser = reminderParser;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_aiService.ApiKey);

    public string? ModelName => _aiService.Model;

    public bool SupportsVision => _aiService.VisionCapability.Support == VisionSupport.Supported;

    public string VisionSupportLevel => VisionSupportValues.ToStorageValue(_aiService.VisionCapability.Support);

    public bool IsVisionSupportVerified => _aiService.VisionCapability.IsVerified;

    public async Task<AIIslandChatResult> ChatAsync(
        string systemPrompt,
        string userMessage,
        AIIslandChatOptions? options = null,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            return AIIslandChatResult.Fail("AIIsland 尚未配置 API Key");

        var (pluginId, pluginName) = AuthGuard.IdentifyCaller();
        var allowed = await _authGuard.ConfirmAsync(
            pluginId, pluginName, "ChatAsync", BuildAuthDescription(options));
        if (!allowed)
            return AIIslandChatResult.Fail("用户拒绝了授权请求");

        if (options?.BypassCache == true)
            _aiService.ClearCache();

        var sw = Stopwatch.StartNew();
        try
        {
            var temperature = options?.Temperature ?? -1;

            if (ShouldUseWebSearch(options))
            {
                var toolResult = await _aiService.ChatWithWebSearchAsync(
                    systemPrompt,
                    userMessage,
                    temperature,
                    ct,
                    throwOnError: true,
                    enableDeepThinking: options?.EnableDeepThinking,
                    thinkingEffort: options?.ThinkingEffort);

                sw.Stop();
                return BuildResult(toolResult, sw.ElapsedMilliseconds);
            }

            var response = await _aiService.ChatResponseAsync(
                systemPrompt,
                userMessage,
                temperature: temperature,
                ct: ct,
                throwOnError: true,
                enableDeepThinking: options?.EnableDeepThinking,
                thinkingEffort: options?.ThinkingEffort);

            sw.Stop();
            var isFallback = _aiService.IsFallbackResult(response.Content) && !response.HasReasoning;
            return new AIIslandChatResult
            {
                Content = response.Content,
                Reasoning = response.Reasoning,
                Success = true,
                IsFallback = isFallback,
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            return AIIslandChatResult.Fail(BuildErrorMessage(ex), sw.ElapsedMilliseconds);
        }
    }

    public async Task<AIIslandChatResult> ChatStreamAsync(
        string systemPrompt,
        string userMessage,
        Action<AIIslandStreamUpdate> onUpdate,
        AIIslandChatOptions? options = null,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            return AIIslandChatResult.Fail("AIIsland 尚未配置 API Key");

        if (onUpdate == null)
            return AIIslandChatResult.Fail("增量回调不能为空");

        var (pluginId, pluginName) = AuthGuard.IdentifyCaller();
        var allowed = await _authGuard.ConfirmAsync(
            pluginId, pluginName, "ChatStreamAsync", BuildAuthDescription(options));
        if (!allowed)
            return AIIslandChatResult.Fail("用户拒绝了授权请求");

        if (options?.BypassCache == true)
            _aiService.ClearCache();

        // 统一把内部增量转换为公开类型，并透传联网阶段状态
        void Forward(AIChatStreamUpdate update) => onUpdate(new AIIslandStreamUpdate
        {
            ContentDelta = update.ContentDelta,
            ReasoningDelta = update.ReasoningDelta,
            Content = update.Content,
            Reasoning = update.Reasoning,
            Status = update.Status,
            SearchQuery = update.SearchQuery
        });

        var sw = Stopwatch.StartNew();
        try
        {
            if (ShouldUseWebSearch(options))
            {
                var toolResult = await _aiService.ChatWithWebSearchStreamAsync(
                    systemPrompt,
                    userMessage,
                    Forward,
                    options?.Temperature ?? -1,
                    ct,
                    throwOnError: true,
                    enableDeepThinking: options?.EnableDeepThinking,
                    thinkingEffort: options?.ThinkingEffort);

                sw.Stop();
                return BuildResult(toolResult, sw.ElapsedMilliseconds);
            }

            var response = await _aiService.ChatStreamResponseAsync(
                systemPrompt,
                userMessage,
                Forward,
                temperature: options?.Temperature ?? -1,
                ct: ct,
                enableDeepThinking: options?.EnableDeepThinking,
                thinkingEffort: options?.ThinkingEffort);

            sw.Stop();
            return new AIIslandChatResult
            {
                Content = response.Content,
                Reasoning = response.Reasoning,
                Success = true,
                IsFallback = _aiService.IsFallbackResult(response.Content) && !response.HasReasoning,
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            return AIIslandChatResult.Fail(BuildErrorMessage(ex), sw.ElapsedMilliseconds);
        }
    }

    public async Task<AIIslandChatResult> ChatWithImageAsync(
        string systemPrompt,
        string userMessage,
        byte[] imageData,
        string? imageMimeType = null,
        AIIslandChatOptions? options = null,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            return AIIslandChatResult.Fail("AIIsland 尚未配置 API Key");

        if (imageData == null || imageData.Length == 0)
            return AIIslandChatResult.Fail("图片内容为空");

        var (pluginId, pluginName) = AuthGuard.IdentifyCaller();
        var allowed = await _authGuard.ConfirmAsync(
            pluginId, pluginName, "ChatWithImageAsync", options?.Description ?? "图片识别");
        if (!allowed)
            return AIIslandChatResult.Fail("用户拒绝了授权请求");

        if (options?.BypassCache == true)
            _aiService.ClearCache();

        var sw = Stopwatch.StartNew();
        try
        {
            var image = new VisionImage(imageData, imageMimeType);
            var response = await _aiService.ChatWithImageResponseAsync(
                systemPrompt,
                userMessage,
                image,
                temperature: options?.Temperature ?? -1,
                ct: ct,
                throwOnError: true,
                enableDeepThinking: options?.EnableDeepThinking,
                thinkingEffort: options?.ThinkingEffort);

            sw.Stop();
            return new AIIslandChatResult
            {
                Content = response.Content,
                Reasoning = response.Reasoning,
                Success = true,
                IsFallback = _aiService.IsFallbackResult(response.Content) && !response.HasReasoning,
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            return AIIslandChatResult.Fail(DescribeImageFailure(ex.Message), sw.ElapsedMilliseconds);
        }
    }

    /// <summary>把厂商返回的图片相关报错补一句可操作的建议，便于调用方直接展示给用户。</summary>
    private static string DescribeImageFailure(string? message)
    {
        var text = message ?? "";
        var looksLikeImageRejection =
            text.Contains("image", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("vision", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("multimodal", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("图片", StringComparison.Ordinal) ||
            text.Contains("图像", StringComparison.Ordinal);

        return looksLikeImageRejection
            ? $"{text}\n当前模型可能不支持图片识别，请在 AIIsland 设置页点「检测」确认，或换用支持视觉的模型。"
            : text;
    }


    public async Task<AIIslandChatResult> ChatWithImageStreamAsync(
        string systemPrompt,
        string userMessage,
        Action<AIIslandStreamUpdate> onUpdate,
        byte[] imageData,
        string? imageMimeType = null,
        AIIslandChatOptions? options = null,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            return AIIslandChatResult.Fail("AIIsland 尚未配置 API Key");

        if (onUpdate == null)
            return AIIslandChatResult.Fail("增量回调不能为空");

        if (imageData == null || imageData.Length == 0)
            return AIIslandChatResult.Fail("图片内容为空");

        if (imageData.Length > VisionImage.MaxBytes)
            return AIIslandChatResult.Fail(
                $"图片过大（{imageData.Length / 1024 / 1024.0:F1} MB），请压缩到 20 MB 以内");

        var (pluginId, pluginName) = AuthGuard.IdentifyCaller();
        var allowed = await _authGuard.ConfirmAsync(
            pluginId, pluginName, "ChatWithImageStreamAsync", options?.Description ?? "图片识别");
        if (!allowed)
            return AIIslandChatResult.Fail("用户拒绝了授权请求");

        if (options?.BypassCache == true)
            _aiService.ClearCache();

        var sw = Stopwatch.StartNew();
        try
        {
            var image = new VisionImage(imageData, imageMimeType);
            var response = await _aiService.ChatWithImageStreamAsync(
                systemPrompt,
                userMessage,
                update => onUpdate(new AIIslandStreamUpdate
                {
                    ContentDelta = update.ContentDelta,
                    ReasoningDelta = update.ReasoningDelta,
                    Content = update.Content,
                    Reasoning = update.Reasoning
                }),
                image,
                temperature: options?.Temperature ?? -1,
                ct: ct,
                enableDeepThinking: options?.EnableDeepThinking,
                thinkingEffort: options?.ThinkingEffort);

            sw.Stop();
            return new AIIslandChatResult
            {
                Content = response.Content,
                Reasoning = response.Reasoning,
                Success = true,
                IsFallback = _aiService.IsFallbackResult(response.Content) && !response.HasReasoning,
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            return AIIslandChatResult.Fail(DescribeImageFailure(ex.Message), sw.ElapsedMilliseconds);
        }
    }

    public async Task<HomeworkParseResult> ParseHomeworkAsync(
        string input, CancellationToken ct = default)
    {
        if (!IsConfigured)
            return new HomeworkParseResult
            {
                Success = false,
                ErrorMessage = "AIIsland 尚未配置 API Key"
            };

        var (pluginId, pluginName) = AuthGuard.IdentifyCaller();
        var allowed = await _authGuard.ConfirmAsync(
            pluginId, pluginName, "ParseHomeworkAsync", "解析作业描述");
        if (!allowed)
            return new HomeworkParseResult
            {
                Success = false,
                ErrorMessage = "用户拒绝了授权请求"
            };

        return await _aiService.ParseHomeworkAsync(input, ct);
    }

    public async Task<ReminderParseResult> ParseReminderAsync(
        string input, CancellationToken ct = default)
    {
        if (!IsConfigured)
            return new ReminderParseResult
            {
                Success = false,
                ErrorMessage = "AIIsland 尚未配置 API Key"
            };

        var (pluginId, pluginName) = AuthGuard.IdentifyCaller();
        var allowed = await _authGuard.ConfirmAsync(
            pluginId, pluginName, "ParseReminderAsync", "解析提醒描述");
        if (!allowed)
            return new ReminderParseResult
            {
                Success = false,
                ErrorMessage = "用户拒绝了授权请求"
            };

        return await _aiService.ParseNaturalLanguage(input, ct);
    }

    public async Task<string> SummarizeTodayAsync(
        List<string> subjects, CancellationToken ct = default)
    {
        return await ExecuteWithAuth(
            "SummarizeTodayAsync", "生成今日课表总结",
            () => _aiService.SummarizeToday(subjects, ct, throwOnError: false), ct);
    }

    public async Task<string> GenerateLearningHintAsync(
        List<string> subjects, string? focusSubject = null,
        CancellationToken ct = default)
    {
        return await ExecuteWithAuth(
            "GenerateLearningHintAsync", "生成学习提示",
            () => _aiService.GenerateLearningHintStream(
                subjects.Count > 0 ? string.Join("、", subjects) : "",
                focusSubject ?? "",
                _ => { },
                ct), ct);
    }

    public async Task<string> EstimateHomeworkLoadAsync(
        List<string> subjects, CancellationToken ct = default)
    {
        return await ExecuteWithAuth(
            "EstimateHomeworkLoadAsync", "估算作业量",
            () => _aiService.EstimateHomeworkLoad(subjects, ct, throwOnError: false), ct);
    }

    public Task<string> TriggerBeforeSchoolReminderAsync(CancellationToken ct = default)
        => ExecuteWithAuth(
            "TriggerBeforeSchoolReminderAsync", "触发智能每日简报（兼容名称）",
            () => Plugin.SmartClassNotifierInstance?.ManualBeforeSchoolReminderAsync(ct: ct)
                ?? Task.FromResult("AIIsland 贴心提醒提供方尚未就绪"), ct);

    public Task<string> TriggerBreakReminderAsync(CancellationToken ct = default)
        => ExecuteWithAuth(
            "TriggerBreakReminderAsync", "触发课间贴心提醒",
            () => Plugin.SmartClassNotifierInstance?.ManualBeforeClassReminderAsync(ct: ct)
                ?? Task.FromResult("AIIsland 贴心提醒提供方尚未就绪"), ct);

    public Task<string> TriggerAfterSchoolSummaryAsync(CancellationToken ct = default)
        => ExecuteWithAuth(
            "TriggerAfterSchoolSummaryAsync", "触发放学贴心总结",
            () => Plugin.SmartClassNotifierInstance?.ManualAfterSchoolSummaryAsync(ct: ct)
                ?? Task.FromResult("AIIsland 贴心提醒提供方尚未就绪"), ct);

    /// <summary>
    /// 本次调用是否走联网路径。<c>null</c> 表示跟随全局设置——此时
    /// <see cref="AIChatService.IsWebSearchAvailable"/> 已经同时校验了开关与搜索源配置。
    /// </summary>
    private bool ShouldUseWebSearch(AIIslandChatOptions? options)
        => options?.EnableWebSearch ?? _aiService.IsWebSearchAvailable;

    /// <summary>联网时在授权描述后追加提示，明确告知问题内容会发送到搜索引擎。</summary>
    private static string? BuildAuthDescription(AIIslandChatOptions? options)
    {
        if (options?.EnableWebSearch != true) return options?.Description;

        const string note = "本次请求可能联网检索，问题内容会发送到搜索引擎";
        return string.IsNullOrWhiteSpace(options.Description)
            ? note
            : $"{options.Description}（{note}）";
    }

    /// <summary>把内部工具结果转换为公开结果类型，并回填来源列表。</summary>
    private AIIslandChatResult BuildResult(AIChatToolResult result, long durationMs)
    {
        return new AIIslandChatResult
        {
            Content = result.Content,
            Reasoning = result.Reasoning,
            Success = true,
            IsFallback = _aiService.IsFallbackResult(result.Content) &&
                         string.IsNullOrEmpty(result.Reasoning),
            UsedWebSearch = result.UsedWebSearch,
            SearchQuery = string.IsNullOrWhiteSpace(result.SearchQuery) ? null : result.SearchQuery,
            WebSources = result.Sources.Count == 0
                ? null
                : result.Sources.Select(ToPublicSource).ToArray(),
            DurationMs = durationMs
        };
    }

    private static AIIslandWebSource ToPublicSource(WebSearchItem item) => new()
    {
        Title = item.Title,
        Url = item.Url,
        Snippet = item.Snippet,
        Date = item.PublishedDate
    };

    /// <summary>
    /// 组织失败消息。400 这类 API 错误的原因通常写在响应体里，
    /// 只回一句「HTTP 400 Bad Request」无法定位，这里把响应体一并带出（截断到 300 字）。
    /// </summary>
    private static string BuildErrorMessage(Exception ex)
    {
        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            if (current is AIHttpResponseException http &&
                !string.IsNullOrWhiteSpace(http.ResponseBody))
            {
                var body = http.ResponseBody.Length > 300
                    ? http.ResponseBody[..300] + "…"
                    : http.ResponseBody;
                return $"{ex.Message}（{body}）";
            }
        }

        // TLS / 连接类异常的外层 Message 往往只有「see inner exception」，
        // 把最内层的原因带出来，排查时才知道是证书问题还是网络被拦
        if (ex is HttpRequestException)
        {
            var inner = ex;
            while (inner.InnerException != null) inner = inner.InnerException;
            if (!ReferenceEquals(inner, ex) && !string.IsNullOrWhiteSpace(inner.Message))
                return $"{ex.Message}（{inner.Message}）";
        }

        return ex.Message;
    }

    private async Task<string> ExecuteWithAuth(
        string method,
        string description,
        Func<Task<string>> action,
        CancellationToken ct)
    {
        if (!IsConfigured)
            return "AIIsland 尚未配置 API Key";

        var (pluginId, pluginName) = AuthGuard.IdentifyCaller();
        var allowed = await _authGuard.ConfirmAsync(
            pluginId, pluginName, method, description);
        if (!allowed)
            return "用户拒绝了授权请求";

        try
        {
            return await action();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error($"外部插件调用 {method} 失败: {ex.Message}");
            return $"调用失败: {ex.Message}";
        }
    }
}
