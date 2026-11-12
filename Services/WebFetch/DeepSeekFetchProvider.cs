using System.Diagnostics;
using System.Text.Json;
using ClassIsland.AISmartClass.Services.WebSearch;
using static ClassIsland.AISmartClass.Services.DeepSeekToolsClient;

namespace ClassIsland.AISmartClass.Services.WebFetch;

/// <summary>DeepSeek 官方网页抓取兜底；与搜索共用鉴权、超时、读取和错误处理。</summary>
internal sealed class DeepSeekFetchProvider
{
    private const string FetchPath = "/v1/tools/fetch";
    private readonly DeepSeekToolsClient _client;

    public DeepSeekFetchProvider(HttpClient? http = null) => _client = new DeepSeekToolsClient(http);

    public async Task<WebFetchResult> FetchAsync(string url, WebSearchConfig config, CancellationToken ct)
    {
        url = (url ?? "").Trim();
        if (url.Length == 0) return WebFetchResult.Fail("URL 为空");

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.PostAsync(FetchPath, new { url }, config, ct).ConfigureAwait(false);
        var parsed = response.Success ? Parse(response.Body, url) : WebFetchResult.Fail(response.Error!);
        Logger.Info(parsed.Success
            ? $"DeepSeek 抓取成功：正文 {parsed.Text.Length} 字，耗时 {stopwatch.ElapsedMilliseconds}ms"
            : $"DeepSeek 抓取失败：{parsed.Error}");
        return parsed;
    }

    /// <summary>解析 <c>{ title, content }</c>；正文为空视为抓取失败（官方在无内容时也这样返回）。</summary>
    internal static WebFetchResult Parse(string body, string url)
    {
        if (string.IsNullOrWhiteSpace(body))
            return WebFetchResult.Fail("返回内容为空");

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return WebFetchResult.Fail("返回内容不是 JSON 对象");

            if (!root.TryGetProperty("content", out var contentElement) ||
                contentElement.ValueKind != JsonValueKind.String)
            {
                var error = ReadError(root);
                return WebFetchResult.Fail(error.Length > 0 ? error : "返回 JSON 缺少 content 字段");
            }

            var content = contentElement.GetString()?.Trim() ?? "";
            if (content.Length == 0)
                return WebFetchResult.Fail("DeepSeek 未能提取页面内容");

            var title = root.TryGetProperty("title", out var titleElement) &&
                        titleElement.ValueKind == JsonValueKind.String
                ? titleElement.GetString()?.Trim() ?? ""
                : "";

            if (content.Length > WebFetchService.MaxTextLength)
                content = content[..WebFetchService.MaxTextLength] + "…";

            return new WebFetchResult(true, title, content, url, null);
        }
        catch (JsonException)
        {
            return WebFetchResult.Fail("返回内容不是合法 JSON");
        }
    }

}
