using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// AIChatService HTTP 请求实现：非流式与流式（SSE）请求。
/// </summary>
public partial class AIChatService
{
    private async Task<string> SendRequestAsync(
        string system, string user, AiRequestSnapshot snapshot, CancellationToken ct)
    {
        var body = new
        {
            model = snapshot.Model,
            temperature = snapshot.Temperature,
            max_tokens = snapshot.MaxTokens,
            messages = new[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            }
        };

        var jsonBody = JsonSerializer.Serialize(body);
        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, snapshot.Endpoint) { Content = content };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {snapshot.ApiKey}");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(snapshot.TimeoutSeconds));

        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(responseJson);

        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString()?.Trim() ?? "";
    }

    /// <summary>
    /// 发送流式请求并逐 token 返回。兼容 OpenAI Chat Completions SSE 格式。
    /// </summary>
    private async IAsyncEnumerable<string> SendStreamRequestAsync(
        string system, string user, AiRequestSnapshot snapshot, [EnumeratorCancellation] CancellationToken ct)
    {
        var body = new
        {
            model = snapshot.Model,
            temperature = snapshot.Temperature,
            max_tokens = snapshot.MaxTokens,
            stream = true,
            messages = new[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            }
        };

        var jsonBody = JsonSerializer.Serialize(body);
        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, snapshot.Endpoint) { Content = content };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {snapshot.ApiKey}");
        request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(snapshot.TimeoutSeconds));
        var requestToken = timeoutCts.Token;

        using var response = await _http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            requestToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(requestToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var completed = false;
        while (true)
        {
            var line = await reader.ReadLineAsync(requestToken).ConfigureAwait(false);
            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            var data = line["data:".Length..].Trim();
            if (data == "[DONE]")
            {
                completed = true;
                break;
            }

            string? token = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;
                if (root.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0)
                {
                    var choice = choices[0];
                    if (choice.TryGetProperty("finish_reason", out var finishReason) &&
                        finishReason.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrEmpty(finishReason.GetString()))
                    {
                        completed = true;
                    }

                    if (choice.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("content", out var contentProp))
                    {
                        token = contentProp.GetString();
                    }
                }
            }
            catch (JsonException)
            {
                // SSE 数据行不是合法 JSON（如某些厂商的心跳），忽略。
                continue;
            }

            if (!string.IsNullOrEmpty(token))
                yield return token;
        }

        if (!completed)
            throw new IOException("AI 流式响应在完成标记前中断");
    }
}
