using System.Text.Json;
using ClassIsland.AISmartClass.Models;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// AIChatService 场景方法：课表总结、课前提醒、课程提示、放学总结、自然语言解析等。
/// </summary>
public partial class AIChatService
{
    // ========================================
    //  课表总结
    // ========================================

    public async Task<string> SummarizeToday(List<string> subjectNames, CancellationToken ct = default)
    {
        if (subjectNames.Count == 0)
            return "今天没有课程安排~";

        if (string.IsNullOrWhiteSpace(ApiKey))
            return GenerateRuleBasedSummary(subjectNames);

        var (systemPrompt, userMessage) = BuildSummarizeTodayPrompt(subjectNames);
        return await ChatAsync(systemPrompt, userMessage, ct: ct).ConfigureAwait(false);
    }

    public async Task<string> SummarizeTodayStream(
        List<string> subjectNames,
        Action<string> onUpdate,
        CancellationToken ct = default)
    {
        if (subjectNames.Count == 0)
        {
            var empty = "今天没有课程安排~";
            onUpdate(empty);
            return empty;
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            var ruleBased = GenerateRuleBasedSummary(subjectNames);
            onUpdate(ruleBased);
            return ruleBased;
        }

        var (systemPrompt, userMessage) = BuildSummarizeTodayPrompt(subjectNames);
        return await ChatStreamAsync(systemPrompt, userMessage, onUpdate, ct: ct).ConfigureAwait(false);
    }

    private (string System, string User) BuildSummarizeTodayPrompt(List<string> subjectNames)
    {
        var systemPrompt = PromptTemplates.GetTodaySummarySystem(EffectiveToneStyle);
        var userMessage = string.Format(PromptTemplates.GetTodaySummaryUser(EffectiveToneStyle),
            string.Join("、", subjectNames),
            DateTime.Now.DayOfWeek switch
            {
                DayOfWeek.Monday => "周一",
                DayOfWeek.Tuesday => "周二",
                DayOfWeek.Wednesday => "周三",
                DayOfWeek.Thursday => "周四",
                DayOfWeek.Friday => "周五",
                _ => "周末"
            });
        return (systemPrompt, userMessage);
    }

    // ========================================
    //  课前提醒
    // ========================================

    public async Task<string> GenerateBeforeClassReminder(
        string? previousSubject, string nextSubject, CancellationToken ct = default, bool throwOnError = false)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            if (throwOnError) throw new InvalidOperationException("请先配置 AI API Key");
            return _fallback.GetRandomPhrase("before_class", nextSubject);
        }

        var systemPrompt = PromptTemplates.GetBeforeClassSystem(EffectiveToneStyle);
        var userMessage = string.Format(PromptTemplates.GetBeforeClassUser(EffectiveToneStyle),
            previousSubject ?? "无", nextSubject);
        var result = await ChatAsync(systemPrompt, userMessage, ct: ct, throwOnError: throwOnError).ConfigureAwait(false);

        if (IsFallbackPhrase(result))
            return _fallback.GetRandomPhrase("before_class", nextSubject);

        return result;
    }

    public async Task<string> GenerateBeforeClassReminderStream(
        string? previousSubject, string nextSubject,
        Action<string> onUpdate,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            var fallback = _fallback.GetRandomPhrase("before_class", nextSubject);
            onUpdate(fallback);
            return fallback;
        }

        var systemPrompt = PromptTemplates.GetBeforeClassSystem(EffectiveToneStyle);
        var userMessage = string.Format(PromptTemplates.GetBeforeClassUser(EffectiveToneStyle),
            previousSubject ?? "无", nextSubject);

        var result = await ChatStreamAsync(systemPrompt, userMessage, onUpdate, ct: ct).ConfigureAwait(false);

        if (IsFallbackPhrase(result))
        {
            var fallback = _fallback.GetRandomPhrase("before_class", nextSubject);
            onUpdate(fallback);
            return fallback;
        }

        return result;
    }

    // ========================================
    //  课程提示
    // ========================================

    public Task<string> GenerateCurrentHintStream(
        string? currentSubject,
        Action<string> onUpdate,
        CancellationToken ct = default)
    {
        var subject = string.IsNullOrWhiteSpace(currentSubject) ? "自主学习" : currentSubject.Trim();
        return GenerateLearningHintStream("正在上课", subject, onUpdate, ct);
    }

    public async Task<string> GenerateLearningHintStream(
        string scene,
        string focus,
        Action<string> onUpdate,
        CancellationToken ct = default)
    {
        var safeScene = string.IsNullOrWhiteSpace(scene) ? "当前没有正在进行的课程" : scene.Trim();
        var safeFocus = string.IsNullOrWhiteSpace(focus) ? "自主学习" : focus.Trim();
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            var fallback = _fallback.GetRandomPhrase("current_hint", safeFocus);
            onUpdate(fallback);
            return fallback;
        }

        var systemPrompt = PromptTemplates.GetCurrentHintSystem(EffectiveToneStyle);
        var userMessage = $"当前状态：{safeScene}\n学习重点：{safeFocus}\n请结合当前状态给一句15字以内的学习提示，直接输出提示。";

        var result = await ChatStreamAsync(systemPrompt, userMessage, onUpdate, ct: ct).ConfigureAwait(false);

        if (IsFallbackPhrase(result))
        {
            var fallback = _fallback.GetRandomPhrase("current_hint", safeFocus);
            onUpdate(fallback);
            return fallback;
        }

        return result;
    }

    // ========================================
    //  放学总结
    // ========================================

    public async Task<string> GenerateDailySummary(List<string> todaySubjects, CancellationToken ct = default, bool throwOnError = false)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            if (throwOnError) throw new InvalidOperationException("请先配置 AI API Key");
            return _fallback.GetRandomPhrase("after_school");
        }

        var systemPrompt = PromptTemplates.GetDailySummarySystem(EffectiveToneStyle);
        var userMessage = string.Format(PromptTemplates.GetDailySummaryUser(EffectiveToneStyle),
            string.Join("\n", todaySubjects.Select((s, i) => $"第{i + 1}节：{s}")));
        var result = await ChatAsync(systemPrompt, userMessage, ct: ct, throwOnError: throwOnError).ConfigureAwait(false);

        if (IsFallbackPhrase(result))
            return _fallback.GetRandomPhrase("after_school");

        return result;
    }

    // ========================================
    //  自然语言提醒解析
    // ========================================

    public async Task<ReminderParseResult> ParseNaturalLanguage(
        string input, CancellationToken ct = default)
    {
        var result = new ReminderParseResult { RawInput = input };

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            result.Success = false;
            result.ErrorMessage = "请先配置 AI API Key";
            return result;
        }

        try
        {
            var systemPrompt = PromptTemplates.NLParseSystem;
            var userMessage = string.Format(PromptTemplates.NLParseUser,
                input, DateTime.Now.ToString("yyyy-MM-dd"), DateTime.Now.DayOfWeek);

            // 结构化解析必须拿到真实的 AI JSON。若请求失败，不能返回普通降级句子
            // 再交给 JsonDocument 解析，否则界面会误报为“无法理解”，看起来像未调用 AI。
            var response = await ChatAsync(
                systemPrompt,
                userMessage,
                temperature: 0.1,
                ct: ct,
                throwOnError: true).ConfigureAwait(false);

            var json = ExtractJsonPayload(response);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            result.Success = root.GetProperty("success").GetBoolean();

            if (!result.Success)
            {
                result.ErrorMessage = root.TryGetProperty("error", out var err)
                    ? err.GetString() : "无法理解这条提醒";
                return result;
            }

            var typeStr = root.GetProperty("type").GetString();
            result.Type = typeStr switch
            {
                "fixed_time" => ReminderType.FixedTime,
                "daily_repeat" => ReminderType.DailyRepeat,
                "subject_linked" => ReminderType.SubjectLinked,
                _ => ReminderType.FixedTime
            };

            result.Date = root.TryGetProperty("date", out var d) && d.ValueKind != JsonValueKind.Null
                ? d.GetString() : null;
            result.Time = root.TryGetProperty("time", out var t) && t.ValueKind != JsonValueKind.Null
                ? t.GetString() : null;
            result.SubjectName = root.TryGetProperty("subjectName", out var sn) && sn.ValueKind != JsonValueKind.Null
                ? sn.GetString() : null;
            result.Content = root.GetProperty("content").GetString() ?? "";
            result.MinutesBefore = root.TryGetProperty("minutesBefore", out var mb)
                ? mb.GetInt32() : 3;

            return result;
        }
        catch (InvalidOperationException ex)
        {
            Logger.Error($"自然语言解析 AI 请求失败: {ex.Message}");
            result.Success = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
        catch (JsonException ex)
        {
            Logger.Error($"自然语言解析返回格式无效: {ex.Message}");
            result.Success = false;
            result.ErrorMessage = "AI 返回的提醒格式无效，请重试或换一种表述";
            return result;
        }
        catch (Exception ex)
        {
            Logger.Error($"自然语言解析失败: {ex.Message}");
            result.Success = false;
            result.ErrorMessage = $"解析失败: {ex.Message}";
            return result;
        }
    }

    private static string ExtractJsonPayload(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            throw new JsonException("AI 返回内容为空");

        var text = response.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = text.IndexOf('\n');
            var closingFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLineEnd >= 0 && closingFence > firstLineEnd)
                text = text[(firstLineEnd + 1)..closingFence].Trim();
        }

        var objectStart = text.IndexOf('{');
        var objectEnd = text.LastIndexOf('}');
        if (objectStart < 0 || objectEnd < objectStart)
            throw new JsonException("AI 返回内容中没有 JSON 对象");

        return text[objectStart..(objectEnd + 1)];
    }

    // ========================================
    //  作业量估算
    // ========================================

    /// <summary>AI 估算今日作业量</summary>
    public async Task<string> EstimateHomeworkLoad(List<string> subjectNames, CancellationToken ct = default)
    {
        if (subjectNames.Count == 0)
            return "今天没有课程，无作业~";

        if (string.IsNullOrWhiteSpace(ApiKey))
            return RuleBasedHomeworkEstimate(subjectNames);

        try
        {
            var systemPrompt = PromptTemplates.GetHomeworkEstimateSystem(EffectiveToneStyle);
            var userMessage = string.Format(PromptTemplates.GetHomeworkEstimateUser(EffectiveToneStyle),
                string.Join("、", subjectNames));
            var result = await ChatAsync(systemPrompt, userMessage, ct: ct).ConfigureAwait(false);

            if (IsFallbackPhrase(result))
                return RuleBasedHomeworkEstimate(subjectNames);

            return result;
        }
        catch (Exception ex)
        {
            Logger.Info($"作业量估算失败: {ex.Message}");
            return RuleBasedHomeworkEstimate(subjectNames);
        }
    }

    public async Task<string> EstimateHomeworkLoadStream(
        List<string> subjectNames,
        Action<string> onUpdate,
        CancellationToken ct = default)
    {
        if (subjectNames.Count == 0)
        {
            var empty = "今天没有课程，无作业~";
            onUpdate(empty);
            return empty;
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            var ruleBased = RuleBasedHomeworkEstimate(subjectNames);
            onUpdate(ruleBased);
            return ruleBased;
        }

        try
        {
            var systemPrompt = PromptTemplates.GetHomeworkEstimateSystem(EffectiveToneStyle);
            var userMessage = string.Format(PromptTemplates.GetHomeworkEstimateUser(EffectiveToneStyle),
                string.Join("、", subjectNames));

            var result = await ChatStreamAsync(systemPrompt, userMessage, onUpdate, ct: ct).ConfigureAwait(false);

            if (IsFallbackPhrase(result))
            {
                var ruleBased = RuleBasedHomeworkEstimate(subjectNames);
                onUpdate(ruleBased);
                return ruleBased;
            }

            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Info($"作业量估算流式失败: {ex.Message}");
            var ruleBased = RuleBasedHomeworkEstimate(subjectNames);
            onUpdate(ruleBased);
            return ruleBased;
        }
    }
}
