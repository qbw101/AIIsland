using System.Net;
using System.Text;
using System.Text.Json;
using ClassIsland.AISmartClass.Services.WebSearch;

namespace ClassIsland.AISmartClass.Services;

/// <summary>DeepSeek 搜索和抓取共用的传输层；不重试付费请求，不跟随鉴权请求的重定向。</summary>
internal sealed class DeepSeekToolsClient
{
    internal const int MaxResponseBytes = 1_000_000;

    private static readonly HttpClient SharedHttp = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    private readonly HttpClient _http;

    public DeepSeekToolsClient(HttpClient? http = null) => _http = http ?? SharedHttp;

    internal sealed record Response(string Body, string? Error)
    {
        public bool Success => Error == null;
        public static Response Fail(string error) => new("", error);
    }

    public async Task<Response> PostAsync(
        string path, object payload, WebSearchConfig config, CancellationToken ct)
    {
        if (!config.EnableDeepSeekFallback)
            return Response.Fail("未启用 DeepSeek 官方联网兜底");
        if (string.IsNullOrWhiteSpace(config.AiApiKey))
            return Response.Fail("未配置 DeepSeek API Key");
        if (!TryBuildApiUri(config.AiEndpoint, path, out var uri))
            return Response.Fail("当前接口地址不是 DeepSeek 官方 HTTPS 地址");

        var timeoutSeconds = config.EffectiveTimeoutSeconds;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new("Bearer", config.AiApiKey.Trim());

            using var response = await _http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return Response.Fail(DescribeStatus(response.StatusCode, path.EndsWith("/fetch", StringComparison.Ordinal)));

            if (response.Content.Headers.ContentLength > MaxResponseBytes)
                return Response.Fail("DeepSeek 返回内容超过大小限制");

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            using var memory = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cts.Token).ConfigureAwait(false);
                if (read == 0) break;
                if (memory.Length + read > MaxResponseBytes)
                    return Response.Fail("DeepSeek 返回内容超过大小限制");
                memory.Write(buffer, 0, read);
            }

            return new Response(Encoding.UTF8.GetString(memory.GetBuffer(), 0, (int)memory.Length), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Response.Fail($"超过 {timeoutSeconds} 秒未返回");
        }
        catch (HttpRequestException ex)
        {
            return Response.Fail($"无法连接：{ex.Message}");
        }
        catch (IOException ex)
        {
            return Response.Fail($"读取响应失败：{ex.Message}");
        }
    }

    internal static bool TryBuildApiUri(string endpoint, string path, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps || baseUri.Port != 443 || baseUri.UserInfo.Length > 0 ||
            !baseUri.Host.Equals("api.deepseek.com", StringComparison.OrdinalIgnoreCase))
            return false;

        uri = new UriBuilder(baseUri) { Path = path, Query = "", Fragment = "" }.Uri;
        return true;
    }

    internal static string ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
            return "";
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim() ?? "",
            JsonValueKind.Number => value.ToString(),
            _ => ""
        };
    }

    internal static string ReadError(JsonElement root)
    {
        var message = ReadString(root, "message");
        if (message.Length > 0) return message;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("error", out var error)) return "";
        return error.ValueKind == JsonValueKind.String
            ? error.GetString()?.Trim() ?? ""
            : ReadString(error, "message");
    }

    private static string DescribeStatus(HttpStatusCode status, bool fetch) => status switch
    {
        HttpStatusCode.BadRequest => $"{(fetch ? "抓取" : "搜索")}参数被拒绝（400）",
        HttpStatusCode.Unauthorized => "DeepSeek API Key 无效（401）",
        HttpStatusCode.PaymentRequired => "DeepSeek 账户余额不足（402）",
        HttpStatusCode.Forbidden when fetch => "DeepSeek 拒绝抓取该页面（403）",
        HttpStatusCode.NotFound when fetch => "DeepSeek 未能提取页面内容（404）",
        HttpStatusCode.RequestTimeout => "DeepSeek 读取超时（408）",
        HttpStatusCode.TooManyRequests => "DeepSeek 限流或额度超限（429）",
        _ => $"HTTP {(int)status} 状态异常"
    };
}
