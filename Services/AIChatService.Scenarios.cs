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

    public async Task<string> SummarizeToday(List<string> subjectNames, CancellationToken ct = default, bool throwOnError = false)
    {
        ct.ThrowIfCancellationRequested();
        if (subjectNames.Count == 0)
        {
            const string empty = "今天没有课程安排~";
            await LogLocalResultAsync(
                "今日课表总结",
                null,
                "今日课程：无",
                empty,
                reason: "今日无课程，无需请求 AI").ConfigureAwait(false);
            return empty;
        }

        var (systemPrompt, userMessage) = BuildSummarizeTodayPrompt(subjectNames);
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            if (throwOnError)
            {
                await LogLocalResultAsync(
                    "今日课表总结",
                    systemPrompt,
                    userMessage,
                    "",
                    reason: "未配置 API Key，调用要求抛出错误").ConfigureAwait(false);
                throw new InvalidOperationException("请先配置 AI API Key");
            }

            var ruleBased = GenerateRuleBasedSummary(subjectNames);
            await LogLocalResultAsync(
                "今日课表总结",
                systemPrompt,
                userMessage,
                ruleBased,
                reason: "未配置 API Key，使用规则总结").ConfigureAwait(false);
            return ruleBased;
        }

        return await ChatAsync(systemPrompt, userMessage, ct: ct, throwOnError: throwOnError).ConfigureAwait(false);
    }

    public async Task<string> SummarizeTodayStream(
        List<string> subjectNames,
        Action<string> onUpdate,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (subjectNames.Count == 0)
        {
            var empty = "今天没有课程安排~";
            onUpdate(empty);
            await LogLocalResultAsync(
                "今日课表总结",
                null,
                "今日课程：无",
                empty,
                true,
                "今日无课程，无需请求 AI").ConfigureAwait(false);
            return empty;
        }

        var (systemPrompt, userMessage) = BuildSummarizeTodayPrompt(subjectNames);
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            var ruleBased = GenerateRuleBasedSummary(subjectNames);
            onUpdate(ruleBased);
            await LogLocalResultAsync(
                "今日课表总结",
                systemPrompt,
                userMessage,
                ruleBased,
                true,
                "未配置 API Key，使用规则总结").ConfigureAwait(false);
            return ruleBased;
        }

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
        string? previousSubject, string nextSubject, CancellationToken ct = default, bool throwOnError = false,
        string? context = null)
    {
        var systemPrompt = PromptTemplates.GetBeforeClassSystem(EffectiveToneStyle);
        var userMessage = string.Format(PromptTemplates.GetBeforeClassUser(EffectiveToneStyle),
            previousSubject ?? "无", nextSubject);
        userMessage = AppendThoughtfulContext(userMessage, context);
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            if (throwOnError)
            {
                await LogLocalResultAsync(
                    "课前提醒",
                    systemPrompt,
                    userMessage,
                    "",
                    reason: "未配置 API Key，调用要求抛出错误").ConfigureAwait(false);
                throw new InvalidOperationException("请先配置 AI API Key");
            }
            var fallback = _fallback.GetRandomPhrase("before_class", nextSubject);
            await LogLocalResultAsync(
                "课前提醒",
                systemPrompt,
                userMessage,
                fallback,
                reason: "未配置 API Key").ConfigureAwait(false);
            return fallback;
        }

        var result = await ChatAsync(systemPrompt, userMessage, ct: ct, throwOnError: throwOnError).ConfigureAwait(false);

        if (IsFallbackPhrase(result))
        {
            var fallback = _fallback.GetRandomPhrase("before_class", nextSubject);
            await LogLocalResultAsync(
                "课前提醒",
                systemPrompt,
                userMessage,
                fallback,
                reason: "通用 AI 请求失败，改用场景降级内容").ConfigureAwait(false);
            return fallback;
        }

        return result;
    }

    public async Task<string> GenerateBeforeClassReminderStream(
        string? previousSubject, string nextSubject,
        Action<string> onUpdate,
        CancellationToken ct = default)
    {
        var systemPrompt = PromptTemplates.GetBeforeClassSystem(EffectiveToneStyle);
        var userMessage = string.Format(PromptTemplates.GetBeforeClassUser(EffectiveToneStyle),
            previousSubject ?? "无", nextSubject);
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            var fallback = _fallback.GetRandomPhrase("before_class", nextSubject);
            onUpdate(fallback);
            await LogLocalResultAsync(
                "课前提醒",
                systemPrompt,
                userMessage,
                fallback,
                true,
                "未配置 API Key").ConfigureAwait(false);
            return fallback;
        }


        var result = await ChatStreamAsync(systemPrompt, userMessage, onUpdate, ct: ct).ConfigureAwait(false);

        if (IsFallbackPhrase(result))
        {
            var fallback = _fallback.GetRandomPhrase("before_class", nextSubject);
            onUpdate(fallback);
            await LogLocalResultAsync(
                "课前提醒",
                systemPrompt,
                userMessage,
                fallback,
                true,
                "通用 AI 请求失败，改用场景降级内容").ConfigureAwait(false);
            return fallback;
        }

        return result;
    }

    // ========================================
    //  课程提示
    // ========================================

    public async Task<string> GenerateMusicInsight(string title, string artist, string album, CancellationToken ct = default)
    {
        const string systemPrompt = "你是学生身边贴心的音乐助手。根据正在播放的歌曲给出一句自然、简短、有趣的介绍或学习建议。不要编造事实，不超过50字。";
        var userMessage = $"歌曲：{title}\n歌手：{(string.IsNullOrWhiteSpace(artist) ? "未知" : artist)}\n专辑：{(string.IsNullOrWhiteSpace(album) ? "未知" : album)}";
        if (string.IsNullOrWhiteSpace(ApiKey))
            return $"正在播放《{title}》{(string.IsNullOrWhiteSpace(artist) ? "" : $" · {artist}")}，享受这段音乐吧。";
        var result = await ChatAsync(systemPrompt, userMessage, ct: ct).ConfigureAwait(false);
        return IsFallbackPhrase(result) ? $"正在播放《{title}》{(string.IsNullOrWhiteSpace(artist) ? "" : $" · {artist}")}。" : result;
    }

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
        CancellationToken ct = default,
        bool throwOnError = false)
    {
        var safeScene = string.IsNullOrWhiteSpace(scene) ? "当前没有正在进行的课程" : scene.Trim();
        var safeFocus = string.IsNullOrWhiteSpace(focus) ? "自主学习" : focus.Trim();
        var systemPrompt = PromptTemplates.GetCurrentHintSystem(EffectiveToneStyle);
        var userMessage = $"当前状态：{safeScene}\n学习重点：{safeFocus}\n请结合当前状态给一句15字以内的学习提示，直接输出提示。";
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            if (throwOnError)
            {
                await LogLocalResultAsync(
                    "学习提示",
                    systemPrompt,
                    userMessage,
                    "",
                    true,
                    "未配置 API Key，调用要求抛出错误").ConfigureAwait(false);
                throw new InvalidOperationException("请先配置 AI API Key");
            }

            var fallback = _fallback.GetRandomPhrase("current_hint", safeFocus);
            onUpdate(fallback);
            await LogLocalResultAsync(
                "学习提示",
                systemPrompt,
                userMessage,
                fallback,
                true,
                "未配置 API Key").ConfigureAwait(false);
            return fallback;
        }


        var result = await ChatStreamAsync(systemPrompt, userMessage, onUpdate, ct: ct).ConfigureAwait(false);

        if (IsFallbackPhrase(result))
        {
            if (throwOnError)
            {
                await LogLocalResultAsync(
                    "学习提示",
                    systemPrompt,
                    userMessage,
                    "",
                    true,
                    "AI 请求失败，调用要求抛出错误").ConfigureAwait(false);
                throw new InvalidOperationException("AI 学习提示生成失败");
            }

            var fallback = _fallback.GetRandomPhrase("current_hint", safeFocus);
            onUpdate(fallback);
            await LogLocalResultAsync(
                "学习提示",
                systemPrompt,
                userMessage,
                fallback,
                true,
                "通用 AI 请求失败，改用场景降级内容").ConfigureAwait(false);
            return fallback;
        }

        return result;
    }

    // ========================================
    //  放学总结
    // ========================================

    public async Task<string> GenerateDailySummary(List<string> todaySubjects, CancellationToken ct = default, bool throwOnError = false,
        string? context = null)
    {
        var systemPrompt = PromptTemplates.GetDailySummarySystem(EffectiveToneStyle);
        var userMessage = string.Format(PromptTemplates.GetDailySummaryUser(EffectiveToneStyle),
            string.Join("\n", todaySubjects.Select((s, i) => $"第{i + 1}节：{s}")));
        userMessage = AppendBriefingContext(userMessage, context);
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            if (throwOnError)
            {
                await LogLocalResultAsync(
                    "放学总结",
                    systemPrompt,
                    userMessage,
                    "",
                    reason: "未配置 API Key，调用要求抛出错误").ConfigureAwait(false);
                throw new InvalidOperationException("请先配置 AI API Key");
            }
            var fallback = _fallback.GetRandomPhrase("after_school");
            await LogLocalResultAsync(
                "放学总结",
                systemPrompt,
                userMessage,
                fallback,
                reason: "未配置 API Key").ConfigureAwait(false);
            return fallback;
        }

        var result = await ChatAsync(systemPrompt, userMessage, ct: ct, throwOnError: throwOnError).ConfigureAwait(false);

        if (IsFallbackPhrase(result))
        {
            var fallback = _fallback.GetRandomPhrase("after_school");
            await LogLocalResultAsync(
                "放学总结",
                systemPrompt,
                userMessage,
                fallback,
                reason: "通用 AI 请求失败，改用场景降级内容").ConfigureAwait(false);
            return fallback;
        }

        return result;
    }

    /// <summary>生成智能每日简报。</summary>
    public async Task<string> GenerateDailyBriefing(
        IReadOnlyList<string> todayClasses,
        CancellationToken ct = default,
        bool throwOnError = false,
        string? context = null)
    {
        ct.ThrowIfCancellationRequested();
        var systemPrompt = PromptTemplates.GetDailyBriefingSystem(EffectiveToneStyle);
        var userMessage = string.Format(
            PromptTemplates.GetDailyBriefingUser(EffectiveToneStyle),
            DateTime.Now.ToString("yyyy-MM-dd dddd"),
            todayClasses.Count == 0 ? "无课程安排" : string.Join("\n", todayClasses));
        userMessage = AppendThoughtfulContext(userMessage, context);

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            if (throwOnError)
            {
                await LogLocalResultAsync("智能每日简报", systemPrompt, userMessage, "",
                    reason: "未配置 API Key，调用要求抛出错误").ConfigureAwait(false);
                throw new InvalidOperationException("请先配置 AI API Key");
            }

            var local = BuildLocalDailyBriefing(todayClasses, context);
            await LogLocalResultAsync("智能每日简报", systemPrompt, userMessage, local,
                reason: "未配置 API Key，使用本地简报").ConfigureAwait(false);
            return local;
        }

        var result = await ChatAsync(systemPrompt, userMessage, ct: ct, throwOnError: throwOnError)
            .ConfigureAwait(false);
        if (IsFallbackPhrase(result))
        {
            var local = BuildLocalDailyBriefing(todayClasses, context);
            await LogLocalResultAsync("智能每日简报", systemPrompt, userMessage, local,
                reason: "通用 AI 请求失败，改用本地简报").ConfigureAwait(false);
            return local;
        }

        return result;
    }

    private static string BuildLocalDailyBriefing(IReadOnlyList<string> classes, string? context)
    {
        var first = classes.Count > 0 ? $"第一节是{classes[0]}" : "今天暂无课程安排";
        return $"早上好，{first}。";
    }

    private static string AppendThoughtfulContext(string prompt, string? context)
    {
        return string.IsNullOrWhiteSpace(context)
            ? prompt
            : $"{prompt}\n\n当前情境：\n{context.Trim()}\n请结合当前情境生成贴心提醒。";
    }

    private static string AppendBriefingContext(string prompt, string? context)
    {
        return string.IsNullOrWhiteSpace(context)
            ? $"{prompt}\n请围绕全天安排生成智能每日简报。"
            : $"{prompt}\n\n简报数据：\n{context.Trim()}\n请汇总这些信息生成全天智能每日简报；触发时间只是发送时机，不是内容主题，不要只写出发、衣物或第一节课提醒。";
    }

    // ========================================
    //  自然语言提醒解析
    // ========================================

    public async Task<ReminderParseResult> ParseNaturalLanguage(
        string input, CancellationToken ct = default)
    {
        var result = new ReminderParseResult { RawInput = input };

        var systemPrompt = PromptTemplates.NLParseSystem;
        var userMessage = string.Format(PromptTemplates.NLParseUser,
            input, DateTime.Now.ToString("yyyy-MM-dd"), DateTime.Now.DayOfWeek);
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            result.Success = false;
            result.ErrorMessage = "请先配置 AI API Key";
            await LogLocalResultAsync(
                "自然语言提醒解析",
                systemPrompt,
                userMessage,
                result.ErrorMessage,
                reason: "未配置 API Key，返回本地错误结果").ConfigureAwait(false);
            return result;
        }

        try
        {

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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
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

    // ========================================
    //  作业解析
    // ========================================

    public async Task<HomeworkParseResult> ParseHomeworkAsync(
        string input, CancellationToken ct = default)
    {
        var result = new HomeworkParseResult { RawInput = input };

        var systemPrompt = PromptTemplates.HomeworkParseSystem;
        var userMessage = string.Format(PromptTemplates.HomeworkParseUser,
            input, DateTime.Now.ToString("yyyy-MM-dd"), DateTime.Now.DayOfWeek);
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            result.Success = false;
            result.ErrorMessage = "请先配置 AI API Key";
            return await TryFallbackToRuleParserAsync(
                input,
                result,
                result.ErrorMessage,
                systemPrompt,
                userMessage,
                "未配置 API Key").ConfigureAwait(false);
        }

        string? rawResponse = null;
        try
        {
            // 结构化解析必须拿到真实的 AI JSON。若请求失败，不能返回普通降级句子
            // 再交给 JsonDocument 解析，否则界面会误报为"无法理解"，看起来像未调用 AI。
            rawResponse = await ChatAsync(
                systemPrompt,
                userMessage,
                temperature: 0.1,
                ct: ct,
                throwOnError: true).ConfigureAwait(false);

            var json = ExtractJsonPayload(rawResponse);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            result.Success = root.GetProperty("success").GetBoolean();

            if (!result.Success)
            {
                result.ErrorMessage = root.TryGetProperty("error", out var err)
                    ? err.GetString() : "无法理解这条作业描述";
                return await TryFallbackToRuleParserAsync(
                    input,
                    result,
                    result.ErrorMessage ?? "AI 未能理解作业描述",
                    systemPrompt,
                    userMessage,
                    "AI 返回 success=false").ConfigureAwait(false);
            }

            if (root.TryGetProperty("items", out var itemsElement) &&
                itemsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var itemElement in itemsElement.EnumerateArray())
                {
                    var item = new HomeworkParseItem
                    {
                        Subject = itemElement.TryGetProperty("subject", out var s)
                            ? s.GetString() ?? "" : "",
                        Content = itemElement.TryGetProperty("content", out var c)
                            ? c.GetString() ?? "" : "",
                        DueDate = itemElement.TryGetProperty("dueDate", out var d)
                            ? d.GetString() ?? "" : "",
                        Type = itemElement.TryGetProperty("type", out var t)
                            ? t.GetString() ?? "书面作业" : "书面作业",
                        EstimatedMinutes = itemElement.TryGetProperty("estimatedMinutes", out var m)
                            ? m.GetInt32() : 30
                    };
                    if (!string.IsNullOrWhiteSpace(item.Subject) &&
                        !string.IsNullOrWhiteSpace(item.Content))
                    {
                        result.Items.Add(item);
                    }
                }
            }

            if (result.Items.Count == 0)
            {
                result.Success = false;
                result.ErrorMessage = "AI 未能识别出有效的作业条目";
                return await TryFallbackToRuleParserAsync(
                    input,
                    result,
                    "AI 未识别出有效作业条目",
                    systemPrompt,
                    userMessage,
                    "AI 返回的 items 为空").ConfigureAwait(false);
            }

            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            Logger.Error($"作业解析 AI 请求失败: {ex.Message}");
            var error = BuildHomeworkParseErrorMessage(ex, rawResponse);
            return await TryFallbackToRuleParserAsync(
                input,
                new HomeworkParseResult { RawInput = input, Success = false, ErrorMessage = error },
                error,
                systemPrompt,
                userMessage,
                "AI 请求失败").ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            Logger.Error($"作业解析返回格式无效: {ex.Message}");
            var error = BuildHomeworkParseErrorMessage(ex, rawResponse);
            return await TryFallbackToRuleParserAsync(
                input,
                new HomeworkParseResult { RawInput = input, Success = false, ErrorMessage = error },
                error,
                systemPrompt,
                userMessage,
                "AI 返回格式无效").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Error($"作业解析失败: {ex.Message}");
            var error = BuildHomeworkParseErrorMessage(ex, rawResponse);
            return await TryFallbackToRuleParserAsync(
                input,
                new HomeworkParseResult { RawInput = input, Success = false, ErrorMessage = error },
                error,
                systemPrompt,
                userMessage,
                "作业解析发生未预期异常").ConfigureAwait(false);
        }
    }

    private async Task<HomeworkParseResult> TryFallbackToRuleParserAsync(
        string input,
        HomeworkParseResult aiResult,
        string aiError,
        string systemPrompt,
        string userMessage,
        string fallbackReason)
    {
        var ruleResult = HomeworkRuleParser.Parse(input);
        if (ruleResult.Success && ruleResult.Items.Count > 0)
        {
            Logger.Info($"作业解析回退到本地规则引擎成功，识别 {ruleResult.Items.Count} 项作业");
            ruleResult.RawInput = input;
            ruleResult.UsedLocalRules = true;
            ruleResult.ErrorMessage = aiError; // 保留 AI 错误信息供诊断，不作为失败状态展示
            await LogLocalResultAsync(
                "作业解析",
                systemPrompt,
                userMessage,
                JsonSerializer.Serialize(ruleResult.Items),
                reason: $"{fallbackReason}，已回退到本地规则引擎。AI 错误: {aiError}").ConfigureAwait(false);
            return ruleResult;
        }

        Logger.Info("作业解析本地规则引擎未能识别有效作业，返回 AI 错误信息");
        await LogLocalResultAsync(
            "作业解析",
            systemPrompt,
            userMessage,
            "",
            reason: $"{fallbackReason}，本地规则引擎未能识别。AI 错误: {aiError}").ConfigureAwait(false);
        return aiResult;
    }

    private static string BuildHomeworkParseErrorMessage(Exception ex, string? rawResponse)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"AI 解析失败: {ex.Message}");

        // 提取诊断信息中的关键内容
        AIRequestDiagnostics? diagnostics = null;
        if (ex.Data["AIRequestDiagnostics"] is AIRequestDiagnostics d)
        {
            diagnostics = d;
        }

        if (diagnostics != null && diagnostics.Attempts.Count > 0)
        {
            sb.AppendLine($"请求端点: {diagnostics.Endpoint}");
            sb.AppendLine($"尝试次数: {diagnostics.AttemptCount}");
            foreach (var attempt in diagnostics.Attempts)
            {
                sb.AppendLine($"尝试 {attempt.Attempt}: [{attempt.Source}] {attempt.Category}");
                if (attempt.HttpStatusCode.HasValue)
                {
                    sb.AppendLine($"  HTTP 状态: {attempt.HttpStatusCode} {attempt.HttpReasonPhrase}");
                }
                if (!string.IsNullOrWhiteSpace(attempt.ApiResponseBody))
                {
                    var body = attempt.ApiResponseBody.Length > 500
                        ? attempt.ApiResponseBody[..500] + "..."
                        : attempt.ApiResponseBody;
                    sb.AppendLine($"  AI 返回: {body}");
                }
                if (!string.IsNullOrWhiteSpace(attempt.Message) && attempt.Message != ex.Message)
                {
                    sb.AppendLine($"  错误详情: {attempt.Message}");
                }
            }
        }

        // 如果 AI 有原始返回但没有被诊断信息收录，直接附加
        if (!string.IsNullOrWhiteSpace(rawResponse) &&
            (diagnostics == null || diagnostics.Attempts.All(a => string.IsNullOrWhiteSpace(a.ApiResponseBody))))
        {
            var response = rawResponse.Length > 500
                ? rawResponse[..500] + "..."
                : rawResponse;
            sb.AppendLine($"AI 原始返回: {response}");
        }

        return sb.ToString().Trim();
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

    private string BuildHomeworkEstimateUserMessage(List<string> subjectNames)
    {
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        var message = string.Format(
            PromptTemplates.GetHomeworkEstimateUser(EffectiveToneStyle),
            string.Join("、", subjectNames),
            date);

        // 兼容用户保留的旧版自定义提示词：即使模板没有 {1}，也确保日期进入请求和缓存键。
        return message.Contains(date, StringComparison.Ordinal)
            ? message
            : $"{message}\n今日日期：{date}";
    }

    /// <summary>AI 估算今日作业量</summary>
    public async Task<string> EstimateHomeworkLoad(List<string> subjectNames, CancellationToken ct = default, bool throwOnError = false)
    {
        ct.ThrowIfCancellationRequested();
        if (subjectNames.Count == 0)
        {
            const string empty = "今天没有课程，无作业~";
            await LogLocalResultAsync(
                "作业量估算",
                null,
                "今日课程：无",
                empty,
                reason: "今日无课程，无需请求 AI").ConfigureAwait(false);
            return empty;
        }

        var systemPrompt = PromptTemplates.GetHomeworkEstimateSystem(EffectiveToneStyle);
        var userMessage = BuildHomeworkEstimateUserMessage(subjectNames);
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            if (throwOnError)
            {
                await LogLocalResultAsync(
                    "作业量估算",
                    systemPrompt,
                    userMessage,
                    "",
                    reason: "未配置 API Key，调用要求抛出错误").ConfigureAwait(false);
                throw new InvalidOperationException("请先配置 AI API Key");
            }

            var ruleBased = RuleBasedHomeworkEstimate(subjectNames);
            await LogLocalResultAsync(
                "作业量估算",
                systemPrompt,
                userMessage,
                ruleBased,
                reason: "未配置 API Key，使用规则估算").ConfigureAwait(false);
            return ruleBased;
        }

        try
        {
            var result = await ChatAsync(systemPrompt, userMessage, ct: ct, throwOnError: throwOnError).ConfigureAwait(false);

            if (IsFallbackPhrase(result))
            {
                if (throwOnError)
                {
                    await LogLocalResultAsync(
                        "作业量估算",
                        systemPrompt,
                        userMessage,
                        "",
                        reason: "AI 请求失败，调用要求抛出错误").ConfigureAwait(false);
                    throw new InvalidOperationException("AI 作业量估算失败");
                }

                var ruleBased = RuleBasedHomeworkEstimate(subjectNames);
                await LogLocalResultAsync(
                    "作业量估算",
                    systemPrompt,
                    userMessage,
                    ruleBased,
                    reason: "通用 AI 请求失败，改用规则估算").ConfigureAwait(false);
                return ruleBased;
            }

            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (!throwOnError)
        {
            Logger.Info($"作业量估算失败: {ex.Message}");
            var ruleBased = RuleBasedHomeworkEstimate(subjectNames);
            await LogLocalResultAsync(
                "作业量估算",
                systemPrompt,
                userMessage,
                ruleBased,
                reason: $"AI 调用异常，使用规则估算：{ex.Message}").ConfigureAwait(false);
            return ruleBased;
        }
    }

    public async Task<string> EstimateHomeworkLoadStream(
        List<string> subjectNames,
        Action<string> onUpdate,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (subjectNames.Count == 0)
        {
            var empty = "今天没有课程，无作业~";
            onUpdate(empty);
            await LogLocalResultAsync(
                "作业量估算",
                null,
                "今日课程：无",
                empty,
                true,
                "今日无课程，无需请求 AI").ConfigureAwait(false);
            return empty;
        }

        var systemPrompt = PromptTemplates.GetHomeworkEstimateSystem(EffectiveToneStyle);
        var userMessage = BuildHomeworkEstimateUserMessage(subjectNames);
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            var ruleBased = RuleBasedHomeworkEstimate(subjectNames);
            onUpdate(ruleBased);
            await LogLocalResultAsync(
                "作业量估算",
                systemPrompt,
                userMessage,
                ruleBased,
                true,
                "未配置 API Key，使用规则估算").ConfigureAwait(false);
            return ruleBased;
        }

        try
        {
            var result = await ChatStreamAsync(systemPrompt, userMessage, onUpdate, ct: ct).ConfigureAwait(false);

            if (IsFallbackPhrase(result))
            {
                var ruleBased = RuleBasedHomeworkEstimate(subjectNames);
                onUpdate(ruleBased);
                await LogLocalResultAsync(
                    "作业量估算",
                    systemPrompt,
                    userMessage,
                    ruleBased,
                    true,
                    "通用 AI 请求失败，改用规则估算").ConfigureAwait(false);
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
            await LogLocalResultAsync(
                "作业量估算",
                systemPrompt,
                userMessage,
                ruleBased,
                true,
                $"AI 调用异常，使用规则估算：{ex.Message}").ConfigureAwait(false);
            return ruleBased;
        }
    }
}
