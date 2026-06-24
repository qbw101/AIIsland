using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ClassIsland.AISmartClass.Models;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 统一 AI 后端：封装 HTTP 调用、缓存、重试、降级。
/// 所有 AI 场景通过此类统一管理。
/// </summary>
public class AIChatService : IDisposable
{
    // ===== 依赖 =====
    private readonly HttpClient _http;
    private readonly FallbackPhraseService _fallback;

    // ===== 缓存 =====
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

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
    public int CacheMinutes { get; set; } = 5;
    public int MaxRetries { get; set; } = 1;

    public AIChatService(HttpClient http, FallbackPhraseService fallback)
    {
        _http = http;
        _fallback = fallback;
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    /// <summary>从 AISettings 同步配置</summary>
    public void SyncFrom(AISettings settings)
    {
        Endpoint = settings.Endpoint;
        ApiKey = settings.ApiKey;
        Model = settings.Model;
        ToneStyle = settings.ToneStyle;
        MaxTokens = settings.MaxTokens;
        CacheMinutes = settings.CacheMinutes;
        MaxRetries = settings.MaxRetries;
        _http.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);

        // 设置变更后清除缓存，确保立即使用新配置重新调用 AI
        _cache.Clear();

        // 同步语气风格到降级句子库
        _fallback.ToneStyle = settings.ToneStyle;
    }

    /// <summary>根据语气风格获取 temperature</summary>
    private double GetTemperature()
    {
        return ToneStyle switch
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
        CancellationToken ct = default)
    {
        // 1. API Key 缺失 → 降级
        if (string.IsNullOrWhiteSpace(ApiKey))
            return _fallback.GetRandomPhrase("api_key_missing");

        // 2. 检查缓存
        var cacheKey = ComputeCacheKey(systemPrompt, userMessage);
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpireAt > DateTime.UtcNow)
            return cached.Result;

        // 3. 如果未指定 temperature，使用语气风格映射值
        var effectiveTemp = temperature >= 0 ? temperature : GetTemperature();

        // 4. 发起请求（含重试）
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(1000, ct);

            try
            {
                var result = await SendRequestAsync(systemPrompt, userMessage, effectiveTemp, ct);

                // 写入缓存
                _cache[cacheKey] = new CacheEntry
                {
                    Result = result,
                    ExpireAt = DateTime.UtcNow.AddMinutes(CacheMinutes)
                };

                // 随机清理过期缓存
                if (Random.Shared.Next(20) == 0)
                    CleanExpiredCache();

                return result;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AI 请求失败 (attempt {attempt}): {ex.Message}");
            }
        }

        // 全部重试失败 → 降级
        return _fallback.GetRandomPhrase("api_error");
    }

    // ========================================
    //  专用方法
    // ========================================

    public async Task<string> SummarizeToday(List<string> subjectNames, CancellationToken ct = default)
    {
        if (subjectNames.Count == 0)
            return "今天没有课程安排~";

        if (string.IsNullOrWhiteSpace(ApiKey))
            return GenerateRuleBasedSummary(subjectNames);

        var systemPrompt = PromptTemplates.GetTodaySummarySystem(ToneStyle);
        var userMessage = string.Format(PromptTemplates.GetTodaySummaryUser(ToneStyle),
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
        return await ChatAsync(systemPrompt, userMessage, ct: ct);
    }

    public async Task<string> GenerateBeforeClassReminder(
        string? previousSubject, string nextSubject, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            return _fallback.GetRandomPhrase("before_class", nextSubject);

        var systemPrompt = PromptTemplates.GetBeforeClassSystem(ToneStyle);
        var userMessage = string.Format(PromptTemplates.GetBeforeClassUser(ToneStyle),
            previousSubject ?? "无", nextSubject);
        var result = await ChatAsync(systemPrompt, userMessage, ct: ct);

        // 如果 AI 返回的是降级句子（非科目相关），改为使用科目相关的降级句子
        if (IsFallbackPhrase(result))
            return _fallback.GetRandomPhrase("before_class", nextSubject);

        return result;
    }

    public async Task<string> GenerateDailySummary(List<string> todaySubjects, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            return _fallback.GetRandomPhrase("after_school");

        var systemPrompt = PromptTemplates.GetDailySummarySystem(ToneStyle);
        var userMessage = string.Format(PromptTemplates.GetDailySummaryUser(ToneStyle),
            string.Join("\n", todaySubjects.Select((s, i) => $"第{i + 1}节：{s}")));
        var result = await ChatAsync(systemPrompt, userMessage, ct: ct);

        // 如果 AI 返回降级句子，改为使用放学相关降级句子
        if (IsFallbackPhrase(result))
            return _fallback.GetRandomPhrase("after_school");

        return result;
    }

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

            var json = await ChatAsync(systemPrompt, userMessage, temperature: 0.1, ct);

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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"自然语言解析失败: {ex.Message}");
            result.Success = false;
            result.ErrorMessage = "解析失败，请尝试更直接的表述";
            return result;
        }
    }

    public int EstimateDifficulty(List<string> subjectNames)
    {
        var hardSubjects = new HashSet<string> { "数学", "物理", "化学", "英语" };
        var mediumSubjects = new HashSet<string> { "生物", "地理", "历史", "政治" };

        int score = 0;
        foreach (var s in subjectNames)
        {
            if (hardSubjects.Contains(s)) score += 2;
            else if (mediumSubjects.Contains(s)) score += 1;
        }
        return Math.Clamp((int)Math.Ceiling(score / 2.0), 1, 5);
    }

    /// <summary>AI 估算今日作业量</summary>
    public async Task<string> EstimateHomeworkLoad(List<string> subjectNames, CancellationToken ct = default)
    {
        if (subjectNames.Count == 0)
            return "今天没有课程，无作业~";

        if (string.IsNullOrWhiteSpace(ApiKey))
            return RuleBasedHomeworkEstimate(subjectNames);

        try
        {
            var systemPrompt = PromptTemplates.GetHomeworkEstimateSystem(ToneStyle);
            var userMessage = string.Format(PromptTemplates.GetHomeworkEstimateUser(ToneStyle),
                string.Join("、", subjectNames));
            var result = await ChatAsync(systemPrompt, userMessage, ct: ct);

            if (IsFallbackPhrase(result))
                return RuleBasedHomeworkEstimate(subjectNames);

            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"作业量估算失败: {ex.Message}");
            return RuleBasedHomeworkEstimate(subjectNames);
        }
    }

    /// <summary>规则兜底：根据科目类型估算作业量</summary>
    private string RuleBasedHomeworkEstimate(List<string> subjects)
    {
        var heavySubjects = new HashSet<string> { "数学", "物理", "化学" };
        var normalSubjects = new HashSet<string> { "语文", "英语", "生物" };
        var lightSubjects = new HashSet<string> { "历史", "地理", "政治" };

        int minutes = 0;
        var heavyList = new List<string>();
        foreach (var s in subjects)
        {
            if (heavySubjects.Contains(s))
            {
                minutes += 45;
                heavyList.Add(s);
            }
            else if (normalSubjects.Contains(s)) minutes += 30;
            else if (lightSubjects.Contains(s)) minutes += 15;
        }

        // 连堂检测：同科目出现两次→翻倍
        foreach (var g in subjects.GroupBy(s => s).Where(g => g.Count() >= 2))
        {
            if (heavySubjects.Contains(g.Key) || normalSubjects.Contains(g.Key))
                minutes += 30;
        }

        minutes = Math.Clamp(minutes, 30, 180);
        var hours = (double)minutes / 60;
        var count = subjects.Count(s =>
            heavySubjects.Contains(s) || normalSubjects.Contains(s) || lightSubjects.Contains(s));

        if (count == 0) return "今天没有主科课程，作业不多~";
        var focus = heavyList.Count > 0 ? $"，{string.Join("和", heavyList)}是重点" : "";

        return $"预计{count}项作业，约{hours:F1}小时{focus}";
    }

    // ========================================
    //  私有方法
    // ========================================

    private async Task<string> SendRequestAsync(
        string system, string user, double temperature, CancellationToken ct)
    {
        var temp = temperature >= 0 ? temperature : GetTemperature();

        var body = new
        {
            model = Model,
            temperature = temp,
            max_tokens = MaxTokens,
            messages = new[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            }
        };

        var jsonBody = JsonSerializer.Serialize(body);
        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = content };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {ApiKey}");

        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);

        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString()?.Trim() ?? "";
    }

    private string ComputeCacheKey(string system, string user)
    {
        // 使用完整字符串的稳定哈希，避免截断导致碰撞
        var raw = $"{system}|{user}";
        var hash = new HashCode();
        foreach (var c in raw)
            hash.Add(c);
        return hash.ToHashCode().ToString("X8");
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

    /// <summary>判断文本是否为降级句子（AI 不可用或失败时的回退文本）</summary>
    private bool IsFallbackPhrase(string text)
    {
        if (string.IsNullOrEmpty(text)) return true;
        return text.Contains("请在设置中配置") || text.Contains("AI 暂时不可用");
    }

    private string GenerateRuleBasedSummary(List<string> subjects)
    {
        int count = subjects.Count;
        if (count == 0) return "今天没有课程安排~";

        var hard = subjects.Count(s => s is "数学" or "物理" or "化学");
        var easy = subjects.Count(s => s is "体育" or "音乐" or "美术" or "班会");

        if (hard >= 3) return $"今天偏理科，{count}节课中有{hard}节硬课，做好心理准备~";
        if (easy >= 2) return $"今天有{easy}节轻松课，相对舒适~";
        return $"{count}节课的一天，加油！";
    }

    public void Dispose()
    {
        _cache.Clear();
    }
}
