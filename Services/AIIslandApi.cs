using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ClassIsland.AISmartClass.Models;
using ClassIsland.AISmartClass.PublicApi;

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
            pluginId, pluginName, "ChatAsync", options?.Description);
        if (!allowed)
            return AIIslandChatResult.Fail("用户拒绝了授权请求");

        if (options?.BypassCache == true)
            _aiService.ClearCache();

        var sw = Stopwatch.StartNew();
        try
        {
            var temperature = options?.Temperature ?? -1;
            var result = await _aiService.ChatAsync(
                systemPrompt,
                userMessage,
                temperature: temperature,
                ct: ct,
                throwOnError: true);

            sw.Stop();
            var isFallback = _aiService.IsFallbackResult(result);
            return new AIIslandChatResult
            {
                Content = result,
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
            return AIIslandChatResult.Fail(ex.Message, sw.ElapsedMilliseconds);
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
