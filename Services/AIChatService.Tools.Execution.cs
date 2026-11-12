using System.Text;
using System.Text.Json;
using ClassIsland.AISmartClass.Models;
using ClassIsland.AISmartClass.Services.WebFetch;
using ClassIsland.AISmartClass.Services.WebSearch;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 工具调用的执行与协议维护：逐个执行模型请求的调用、按 <c>tool_call_id</c> 回传结果，
/// 并在每次发送前校验工具历史是否完整。从 <c>AIChatService.Tools.cs</c> 拆出，避免单文件过长。
/// </summary>
public partial class AIChatService
{
    /// <summary>工具调用类别，决定结果如何进入 SearchQuery 与 UsedWebSearch。</summary>
    private enum ToolKind
    {
        /// <summary>未知工具或未真正发起网络请求。</summary>
        None,

        /// <summary>执行了联网检索（含复用缓存结果）。</summary>
        Search,

        /// <summary>执行了网页抓取。</summary>
        Fetch
    }

    /// <summary>一次工具调用的结果。</summary>
    /// <param name="Text">回传给模型的文本。</param>
    /// <param name="Items">命中的来源，供调用方展示引用（抓取成功的页面也算来源）。</param>
    /// <param name="Kind">调用类别。</param>
    /// <param name="Query">搜索关键词或抓取的 URL；未知工具或未执行时为空。</param>
    private sealed record ToolExecution(
        string Text,
        IReadOnlyList<WebSearchItem> Items,
        ToolKind Kind,
        string Query);

    /// <summary>
    /// 单次对话的执行预算：相同关键词只真正检索一次，搜索与抓取各有上限。
    /// 模型偶尔会一次要求 3-5 个并行调用，逐个执行会让延迟线性增长；
    /// 预算耗尽的调用仍会回一条说明性的 tool 消息，保证协议完整。
    /// </summary>
    private sealed class ToolExecutionBudget
    {
        private const int MaxSearches = 3;
        private const int MaxFetches = 2;

        private readonly Dictionary<string, ToolExecution> _executed = new(StringComparer.OrdinalIgnoreCase);
        // URL 的路径与查询参数区分大小写；搜索关键词的比较规则不能套在 URL 上。
        private readonly Dictionary<string, ToolExecution> _fetched = new(StringComparer.Ordinal);
        private int _searches;
        private int _fetches;

        public ToolExecution? Find(string query)
            => _executed.TryGetValue(query, out var execution) ? execution : null;

        public bool TryReserve()
        {
            if (_searches >= MaxSearches) return false;
            _searches++;
            return true;
        }

        public bool TryReserveFetch()
        {
            if (_fetches >= MaxFetches) return false;
            _fetches++;
            return true;
        }

        public void Remember(string query, ToolExecution execution) => _executed[query] = execution;

        public ToolExecution? FindFetch(string url)
            => _fetched.TryGetValue(NormalizeFetchCacheKey(url), out var execution) ? execution : null;

        public void RememberFetch(string url, ToolExecution execution)
            => _fetched[NormalizeFetchCacheKey(url)] = execution;
    }

    /// <summary>
    /// 收集本次对话真正执行过的检索关键词（不含抓取的 URL），按首次出现顺序去重，
    /// 供结果里的 <c>SearchQuery</c> 与「正在搜索」提示使用。
    /// </summary>
    private static void CollectQueries(List<string> queries, IReadOnlyList<ToolExecution> executions)
    {
        foreach (var execution in executions)
        {
            if (execution.Kind != ToolKind.Search || execution.Query.Length == 0) continue;
            if (queries.Exists(query => string.Equals(query, execution.Query, StringComparison.Ordinal))) continue;

            queries.Add(execution.Query);
        }
    }

    /// <summary>
    /// 构造「开始检索」的阶段推送，并带上模型实际使用的关键词或 URL，
    /// 让调用方能在等待期间显示「正在搜索：xxx」，而不是只显示一个笼统的搜索中。
    /// 未知工具不贡献内容；多个条目用「；」分隔，最多 3 个。
    /// </summary>
    private static AIChatStreamUpdate BuildSearchingUpdate(
        IReadOnlyList<ChatToolCall> calls, string fallbackQuery)
    {
        var entries = new List<string>();
        foreach (var call in calls)
        {
            if (string.Equals(call.Name, ToolRequestBuilder.WebSearchToolName, StringComparison.Ordinal))
            {
                var query = ExtractSearchQuery(call.Arguments, fallbackQuery);
                if (query.Length > 0) entries.Add(query);
            }
            else if (string.Equals(call.Name, ToolRequestBuilder.WebFetchToolName, StringComparison.Ordinal))
            {
                var url = ExtractFetchUrl(call.Arguments);
                if (url.Length > 0) entries.Add(url);
            }
        }

        return new AIChatStreamUpdate("", "", "", "")
        {
            Status = "searching",
            SearchQuery = string.Join("；", entries.Distinct(StringComparer.Ordinal).Take(3))
        };
    }

    /// <summary>
    /// 逐个执行模型请求的工具调用（web_search / web_fetch）。
    /// 未知工具、重复关键词、超额与失败都返回可读说明，不抛异常。
    /// 返回顺序与入参一致，调用方按顺序回传结果。
    /// </summary>
    private async Task<List<ToolExecution>> ExecuteToolCallsAsync(
        IReadOnlyList<ChatToolCall> calls,
        string fallbackQuery,
        WebSearchService search,
        ToolExecutionBudget budget,
        CancellationToken ct)
    {
        var results = new List<ToolExecution>(calls.Count);

        if (calls.Count > 1)
            Logger.Info($"模型一次请求了 {calls.Count} 个工具调用，逐个执行并回传结果");

        foreach (var call in calls)
        {
            ct.ThrowIfCancellationRequested();
            if (string.Equals(call.Name, ToolRequestBuilder.WebFetchToolName, StringComparison.Ordinal))
            {
                // 工具定义已按开关过滤；文本协议（✿FUNCTION✿）仍可能绕过工具列表，这里再拦一次
                if (!EnableWebFetch)
                {
                    results.Add(new ToolExecution(
                        "网页抓取已在设置中关闭。请基于已有资料回答。",
                        Array.Empty<WebSearchItem>(), ToolKind.None, ""));
                    continue;
                }

                var url = ExtractFetchUrl(call.Arguments);
                if (budget.FindFetch(url) is { } fetched)
                {
                    results.Add(fetched);
                    continue;
                }

                var fetchExecution = await ExecuteWebFetchAsync(call, search, budget, ct).ConfigureAwait(false);
                // 成功与实际抓取后的失败都复用，防止同一页面重复触发远端付费兜底。
                if (fetchExecution.Kind == ToolKind.Fetch) budget.RememberFetch(url, fetchExecution);
                results.Add(fetchExecution);
                continue;
            }

            if (!string.Equals(call.Name, ToolRequestBuilder.WebSearchToolName, StringComparison.Ordinal))
            {
                Logger.Info($"模型请求了未知工具 {call.Name}，已忽略");
                results.Add(new ToolExecution(
                    $"未知工具：{call.Name}。请直接回答用户问题。",
                    Array.Empty<WebSearchItem>(), ToolKind.None, ""));
                continue;
            }

            var query = ExtractSearchQuery(call.Arguments, fallbackQuery);

            // 同一轮里模型可能对同一个关键词重复调用，复用结果即可
            if (budget.Find(query) is { } cached)
            {
                Logger.Info($"本次对话已检索过「{query}」，复用结果");
                results.Add(cached);
                continue;
            }

            if (!budget.TryReserve())
            {
                Logger.Info($"单次对话检索次数已达上限，跳过「{query}」");
                results.Add(new ToolExecution(
                    "本轮检索次数已达上限，请基于已获得的资料回答用户问题。",
                    Array.Empty<WebSearchItem>(),
                    ToolKind.None,
                    ""));
                continue;
            }

            var outcome = await search.SearchAsync(query, ct).ConfigureAwait(false);

            ToolExecution execution;
            if (outcome.Success)
            {
                execution = new ToolExecution(
                    WebSearchService.FormatForModel(outcome.Items), outcome.Items, ToolKind.Search, query);
            }
            else
            {
                Logger.Info($"联网检索失败：{outcome.Error}");
                execution = new ToolExecution(
                    WebSearchService.UnavailableForModel, Array.Empty<WebSearchItem>(), ToolKind.Search, query);
            }

            budget.Remember(query, execution);
            results.Add(execution);
        }

        return results;
    }

    internal static string NormalizeFetchCacheKey(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https")
            return new UriBuilder(uri) { Fragment = "" }.Uri.AbsoluteUri;
        return url;
    }

    /// <summary>
    /// 执行一次 web_fetch：打开网页、提炼正文、按防注入边界回传。
    /// 抓取预算独立于搜索（上限 2 次）；失败如实告知模型，禁止编造页面内容。
    /// </summary>
    private async Task<ToolExecution> ExecuteWebFetchAsync(
        ChatToolCall call,
        WebSearchService search,
        ToolExecutionBudget budget,
        CancellationToken ct)
    {
        var url = ExtractFetchUrl(call.Arguments);
        if (url.Length == 0)
        {
            return new ToolExecution(
                "web_fetch 缺少有效的 url 参数（需要以 http:// 或 https:// 开头的完整链接）。请改用 web_search 检索。",
                Array.Empty<WebSearchItem>(), ToolKind.None, "");
        }

        if (!budget.TryReserveFetch())
        {
            Logger.Info($"单次对话打开网页次数已达上限，跳过 {url}");
            return new ToolExecution(
                "本轮打开网页次数已达上限（2 次），请基于已获得的资料回答。",
                Array.Empty<WebSearchItem>(), ToolKind.None, url);
        }

        var fetcher = WebFetch;
        if (fetcher == null)
        {
            return new ToolExecution(
                "网页抓取服务未初始化。请基于已获得的资料回答。",
                Array.Empty<WebSearchItem>(), ToolKind.None, url);
        }

        // 抓取超时跟随设置页的「检索超时」，WebFetchService 内部保证下限
        var config = search.CurrentConfig;
        var timeoutSeconds = Math.Max(5, config.EffectiveTimeoutSeconds);

        WebFetchResult fetched;
        try
        {
            fetched = await fetcher.FetchAsync(url, timeoutSeconds, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 兜底：抓取服务本应自行吞掉预期异常；走到这里说明是意料之外的情况，
            // 仍然只降级不抛出，避免一次抓取把整轮对话打挂。
            Logger.Error(ex, $"本地抓取网页时出现未预期异常：{url}");
            fetched = WebFetchResult.Fail($"抓取出错：{ex.Message}");
        }

        if (!fetched.Success)
        {
            var localError = fetched.Error ?? "未知错误";
            WebFetchResult? remote;
            try
            {
                remote = await TryDeepSeekFetchAsync(url, config, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"DeepSeek 抓取兜底时出现未预期异常：{url}");
                remote = WebFetchResult.Fail($"抓取出错：{ex.Message}");
            }

            if (remote is not { Success: true })
            {
                var detail = remote == null ? localError : $"本地 {localError}；DeepSeek {remote.Error}";
                Logger.Info($"打开网页失败（{url}）：{detail}");
                return new ToolExecution(
                    $"网页打开失败：{detail}。请基于已有资料回答，不要编造页面内容。",
                    Array.Empty<WebSearchItem>(), ToolKind.Fetch, url);
            }

            fetched = remote;
        }

        Logger.Info($"打开网页成功：{url} 标题=\"{fetched.Title}\" 正文 {fetched.Text.Length} 字");
        var items = new[]
        {
            new WebSearchItem(fetched.Title, fetched.Url, WebFetchService.BuildSnippet(fetched), null)
        };

        return new ToolExecution(WebFetchService.FormatForModel(fetched), items, ToolKind.Fetch, url);
    }

    /// <summary>
    /// 本地抓取失败后的兜底：只有设置里开了 DeepSeek 兜底、且接口是官方地址时才发起。
    /// 返回 null 表示没有可用的兜底通道。
    /// </summary>
    private async Task<WebFetchResult?> TryDeepSeekFetchAsync(
        string url, WebSearchConfig config, CancellationToken ct)
    {
        if (DeepSeekFetch == null || !config.EnableDeepSeekFallback) return null;
        if (!DeepSeekSearchProvider.TryBuildApiUri(config.AiEndpoint, "/v1/tools/fetch", out _)) return null;

        Logger.Info($"本地抓取失败，改用 DeepSeek 官方抓取：{url}");
        return await DeepSeekFetch.FetchAsync(url, config, ct).ConfigureAwait(false);
    }

    /// <summary>从 tool_call 的 JSON 参数里取 query；解析失败时退回用用户原始问题检索。</summary>
    internal static string ExtractSearchQuery(string arguments, string fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(arguments);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("query", out var query) &&
                query.ValueKind == JsonValueKind.String)
            {
                var value = query.GetString()?.Trim() ?? "";
                if (value.Length > 0)
                    return value.Length <= MaxQueryLength ? value : value[..MaxQueryLength];
            }
        }
        catch (JsonException)
        {
            Logger.Info("模型返回的 web_search 参数不是合法 JSON，改用原始问题检索");
        }

        var trimmed = (fallback ?? "").Trim();
        return trimmed.Length <= MaxQueryLength ? trimmed : trimmed[..MaxQueryLength];
    }

    /// <summary>从 web_fetch 的 JSON 参数里取 url；缺失或不是字符串时返回空。</summary>
    internal static string ExtractFetchUrl(string arguments)
    {
        try
        {
            using var doc = JsonDocument.Parse(arguments);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("url", out var url) &&
                url.ValueKind == JsonValueKind.String)
            {
                return url.GetString()?.Trim() ?? "";
            }
        }
        catch (JsonException)
        {
            Logger.Info("模型返回的 web_fetch 参数不是合法 JSON");
        }

        return "";
    }

    private async Task<AIChatToolResult> BuildResultAsync(
        string content,
        StringBuilder reasoning,
        bool usedWebSearch,
        List<WebSearchItem> sources,
        AiRequestSnapshot snapshot,
        bool isStreaming,
        string systemPrompt,
        string userMessage,
        string searchQuery = "")
    {
        // 去掉最终回答里可能残留的 ✿RETURN✿ / 未执行的 ✿FUNCTION✿ 标记
        var finalContent = TextToolCallParser.CleanFinalAnswer(content);
        var finalReasoning = reasoning.ToString();

        if (string.IsNullOrEmpty(finalContent) && string.IsNullOrEmpty(finalReasoning))
            finalContent = _fallback.GetErrorPhraseFromReason("AI 未返回任何内容");
        else if (string.IsNullOrEmpty(finalContent))
        {
            // 只有思考过程、没有正文：思考型模型在检索后「想继续搜」时会这样，
            // 不能当作成功返回空字符串，否则界面上什么都不会显示。
            Logger.Info("联网对话只返回了思考过程，没有正文");
            finalContent = EmptyContentNotice;
        }

        await LogInvocationAsync(
            "联网搜索",
            systemPrompt,
            userMessage,
            finalContent,
            "AI 返回",
            isStreaming,
            snapshot.Model).ConfigureAwait(false);

        return new AIChatToolResult(finalContent, finalReasoning, usedWebSearch, sources, searchQuery);
    }

    /// <summary>搜索不可用或未配置 Key 时，退回原有单轮调用。</summary>
    private async Task<AIChatToolResult> DegradeAsync(
        string systemPrompt,
        string userMessage,
        double temperature,
        CancellationToken ct,
        bool throwOnError,
        bool? enableDeepThinking,
        ThinkingEffort? thinkingEffort)
    {
        var plain = await ChatResponseAsync(
            systemPrompt, userMessage, temperature, ct, throwOnError,
            enableDeepThinking: enableDeepThinking,
            thinkingEffort: thinkingEffort).ConfigureAwait(false);

        return new AIChatToolResult(plain.Content, plain.Reasoning, false, Array.Empty<WebSearchItem>());
    }
}
