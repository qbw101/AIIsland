using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 通用的 API 连接测试工具。供设置页和欢迎向导复用同一套测试逻辑。
/// </summary>
public static class ApiConnectionTester
{
    /// <summary>测试结果</summary>
    public readonly record struct Result(bool Success, string Message);

    /// <summary>仅检查字段是否填写完整</summary>
    public static Result ValidateFields(string endpoint, string apiKey, string model)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return new Result(false, "请填写 API 地址");
        if (string.IsNullOrWhiteSpace(apiKey))
            return new Result(false, "请填写 API Key");
        if (string.IsNullOrWhiteSpace(model))
            return new Result(false, "请填写模型名称");
        return new Result(true, "");
    }

    /// <summary>发送真实 API 请求测试连通性</summary>
    public static async Task<Result> TestAsync(string endpoint, string apiKey, string model)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            var body = new
            {
                model,
                messages = new[] { new { role = "user", content = "你好，请用一句话回复。" } },
                max_tokens = 50,
                temperature = 0.1
            };

            var json = JsonSerializer.Serialize(body);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");

            var response = await http.SendAsync(request);
            var responseText = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
                return new Result(true, $"连接成功！\n模型: {model}\nAPI 返回正常");

            var shortText = responseText.Length > 300 ? responseText[..300] + "..." : responseText;
            return new Result(false, $"API 返回错误 ({response.StatusCode})\n{shortText}");
        }
        catch (TaskCanceledException)
        {
            return new Result(false, "连接超时，请检查 API 地址是否正确");
        }
        catch (HttpRequestException ex)
        {
            return new Result(false, $"网络错误: {ex.Message}\n请检查 API 地址格式是否正确");
        }
        catch (Exception ex)
        {
            return new Result(false, $"未知错误: {ex.Message}");
        }
    }

    /// <summary>完整流程：校验 + 测试</summary>
    public static async Task<Result> FullTestAsync(string endpoint, string apiKey, string model)
    {
        var validation = ValidateFields(endpoint, apiKey, model);
        if (!validation.Success) return validation;
        return await TestAsync(endpoint, apiKey, model);
    }
}
