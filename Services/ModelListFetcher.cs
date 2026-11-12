using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 从兼容 OpenAI 的接口读取可用模型列表。
/// 只做一次轻量 GET /models，不写入任何配置；选择结果由设置页写回模型名称。
/// </summary>
public static class ModelListFetcher
{
    /// <summary>模型列表读取结果；Models 为空时 Error 说明原因。</summary>
    public readonly record struct Result(IReadOnlyList<string> Models, string? Error)
    {
        public bool Success => Models.Count > 0;
    }

    /// <summary>
    /// 由 Chat Completions 地址推导模型列表地址。
    /// 支持 <c>.../chat/completions</c>、<c>.../v1</c> 和裸域名三种常见写法。
    /// </summary>
    public static Uri BuildModelsUri(string endpoint)
    {
        var text = (endpoint ?? string.Empty).Trim();
        if (text.Length == 0)
            throw new ArgumentException("API 地址为空", nameof(endpoint));

        if (!text.Contains("://", StringComparison.Ordinal))
            text = "https://" + text;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
            throw new ArgumentException("API 地址格式无效", nameof(endpoint));

        var path = uri.AbsolutePath.TrimEnd('/');
        foreach (var suffix in new[] { "/chat/completions", "/completions", "/chat" })
        {
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^suffix.Length];
                break;
            }
        }

        if (path.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
        {
            // 已经是模型列表地址，保持原样
        }
        else
        {
            path += "/models";
        }

        var builder = new UriBuilder(uri)
        {
            Path = path.Length == 0 ? "/models" : path,
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri;
    }

    /// <summary>
    /// 解析模型列表响应。兼容 OpenAI 的 <c>data[].id</c>、部分服务的
    /// <c>models[].name</c> 以及直接返回字符串数组的实现。
    /// </summary>
    public static List<string> ParseModels(string json)
    {
        var models = new List<string>();
        if (string.IsNullOrWhiteSpace(json))
            return models;

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        IEnumerable<JsonElement> items;
        if (root.ValueKind == JsonValueKind.Array)
        {
            items = root.EnumerateArray();
        }
        else if (root.ValueKind == JsonValueKind.Object &&
                 root.TryGetProperty("data", out var data))
        {
            if (data.ValueKind == JsonValueKind.Array)
            {
                items = data.EnumerateArray();
            }
            else if (data.ValueKind == JsonValueKind.Object &&
                     data.TryGetProperty("models", out var nested) &&
                     nested.ValueKind == JsonValueKind.Array)
            {
                items = nested.EnumerateArray();
            }
            else
            {
                items = Array.Empty<JsonElement>();
            }
        }
        else if (root.ValueKind == JsonValueKind.Object &&
                 root.TryGetProperty("models", out var modelsNode) &&
                 modelsNode.ValueKind == JsonValueKind.Array)
        {
            items = modelsNode.EnumerateArray();
        }
        else
        {
            items = Array.Empty<JsonElement>();
        }

        foreach (var item in items)
        {
            var id = item.ValueKind switch
            {
                JsonValueKind.String => item.GetString(),
                JsonValueKind.Object => ReadModelId(item),
                _ => null
            };

            if (string.IsNullOrWhiteSpace(id))
                continue;

            id = id.Trim();
            if (!models.Contains(id, StringComparer.OrdinalIgnoreCase))
                models.Add(id);
        }

        models.Sort(StringComparer.OrdinalIgnoreCase);
        return models;
    }

    /// <summary>读取列表接口并解析模型名称，网络或格式问题以 Error 返回，不抛异常。</summary>
    public static async Task<Result> FetchAsync(
        string endpoint,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return new Result(Array.Empty<string>(), "请填写 API Key");

        Uri uri;
        try
        {
            uri = BuildModelsUri(endpoint);
        }
        catch (ArgumentException ex)
        {
            return new Result(Array.Empty<string>(), ex.Message);
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey.Trim()}");

            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var snippet = body.Length > 200 ? body[..200] + "..." : body;
                return new Result(
                    Array.Empty<string>(),
                    $"接口返回 {(int)response.StatusCode} {response.ReasonPhrase}，该服务可能不支持模型列表，请手动填写。\n{snippet}");
            }

            var models = ParseModels(body);
            return models.Count > 0
                ? new Result(models, null)
                : new Result(Array.Empty<string>(), "接口没有返回任何模型，请手动填写模型名称。");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new Result(Array.Empty<string>(), "请求超时，请检查 API 地址或网络。");
        }
        catch (HttpRequestException ex)
        {
            return new Result(Array.Empty<string>(), $"网络错误：{ex.Message}");
        }
        catch (JsonException)
        {
            return new Result(Array.Empty<string>(), "接口返回的内容不是有效的 JSON 模型列表。");
        }
        catch (Exception ex)
        {
            return new Result(Array.Empty<string>(), $"获取模型失败：{ex.Message}");
        }
    }

    private static string? ReadModelId(JsonElement item)
    {
        foreach (var name in new[] { "id", "name", "model" })
        {
            if (item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }

        return null;
    }
}
