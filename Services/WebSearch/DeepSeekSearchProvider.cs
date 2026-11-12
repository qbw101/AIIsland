using System.Diagnostics;
using System.Text.Json;
using static ClassIsland.AISmartClass.Services.DeepSeekToolsClient;

namespace ClassIsland.AISmartClass.Services.WebSearch;

/// <summary>DeepSeek 官方搜索兜底；网络传输与抓取提供者共用 DeepSeekToolsClient。</summary>
internal sealed class DeepSeekSearchProvider : IWebSearchProvider
{
    private const string SearchPath = "/v1/tools/search";
    private readonly DeepSeekToolsClient _client;

    public DeepSeekSearchProvider(HttpClient? http = null) => _client = new DeepSeekToolsClient(http);

    public string Name => "DeepSeek";

    public bool IsConfigured(WebSearchConfig config) =>
        config.EnableDeepSeekFallback && !string.IsNullOrWhiteSpace(config.AiApiKey) &&
        TryBuildSearchUri(config.AiEndpoint, out _);

    public async Task<WebSearchResponse> SearchAsync(string query, WebSearchConfig config, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return WebSearchResponse.Fail("搜索关键词为空");

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.PostAsync(SearchPath, new
        {
            query,
            count = config.EffectiveMaxResults,
            need_content = false
        }, config, ct).ConfigureAwait(false);
        var parsed = response.Success
            ? Parse(response.Body, query, config.EffectiveMaxResults)
            : WebSearchResponse.Fail(response.Error!);

        Logger.Info(parsed.Success
            ? $"DeepSeek 搜索成功：有效 {parsed.Items.Count} 条，耗时 {stopwatch.ElapsedMilliseconds}ms"
            : $"DeepSeek 搜索失败：{parsed.Error}");
        return parsed;
    }

    internal static bool TryBuildSearchUri(string aiEndpoint, out Uri uri)
        => DeepSeekToolsClient.TryBuildApiUri(aiEndpoint, SearchPath, out uri);

    internal static bool TryBuildApiUri(string aiEndpoint, string path, out Uri uri)
        => DeepSeekToolsClient.TryBuildApiUri(aiEndpoint, path, out uri);

    /// <summary>
    /// 解析 DeepSeek 搜索响应。每条结果优先取 <c>summary</c>（官方给模型准备的长摘要），
    /// 没有时退回 <c>snippet</c>；同样经过广告/碎片/相关性过滤。
    /// </summary>
    internal static WebSearchResponse Parse(string body, string query, int maxResults)
    {
        if (string.IsNullOrWhiteSpace(body))
            return WebSearchResponse.Fail("返回内容为空");

        try
        {
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return WebSearchResponse.Fail("返回内容不是 JSON 对象");

            if (!doc.RootElement.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array)
            {
                var error = ReadError(doc.RootElement);
                return WebSearchResponse.Fail(
                    error.Length > 0 ? error : "返回 JSON 缺少 results 数组");
            }

            var tokens = SearchResultFilter.Tokenize(query);
            var items = new List<WebSearchItem>();
            var dropped = 0;

            foreach (var item in results.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;

                var snippet = ReadString(item, "summary");
                if (snippet.Length == 0) snippet = ReadString(item, "snippet");

                var accepted = SearchResultFilter.Accept(
                    ReadString(item, "title"),
                    ReadString(item, "url"),
                    snippet,
                    NullIfEmpty(ReadString(item, "date")),
                    tokens);

                if (accepted is null) { dropped++; continue; }

                items.Add(accepted);
                if (items.Count >= Math.Max(1, maxResults)) break;
            }

            if (dropped > 0)
                Logger.Info($"DeepSeek 搜索过滤：关键词=\"{query}\" 丢弃 {dropped} 条，保留 {items.Count} 条");

            return WebSearchResponse.Ok(items);
        }
        catch (JsonException)
        {
            return WebSearchResponse.Fail("返回内容不是合法 JSON");
        }
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
