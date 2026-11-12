using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClassIsland.AISmartClass.Models;

namespace ClassIsland.AISmartClass.Services;

/// <summary>图片识别结论的来源，决定界面用「已确认」还是「按型号推断」来表述。</summary>
public enum VisionCapabilitySource
{
    /// <summary>没有结论。</summary>
    None = 0,

    /// <summary>按模型名称推断，可能不准。</summary>
    ModelName = 1,

    /// <summary>真实请求探测得到的结论。</summary>
    Detection = 2,

    /// <summary>真实探测过，但没能确认（接口接受图片却没有读出数字）。</summary>
    DetectionInconclusive = 3
}

/// <summary>模型图片识别能力的完整判定：结论 + 结论来源。</summary>
public readonly record struct VisionCapabilityInfo(VisionSupport Support, VisionCapabilitySource Source)
{
    public static VisionCapabilityInfo Unknown => new(VisionSupport.Unknown, VisionCapabilitySource.None);

    /// <summary>是否来自真实探测（可以当作可信结论）。</summary>
    public bool IsVerified => Source == VisionCapabilitySource.Detection;

    /// <summary>是否有结论（无论可信与否）。</summary>
    public bool IsKnown => Support != VisionSupport.Unknown;

    /// <summary>
    /// 是否允许发起图片请求。只有「已确认不支持」时才拦截；
    /// 按名称推断的不支持仍然放行，避免误伤名称不规范的视觉模型。
    /// </summary>
    public bool AllowsImageInput => !(Support == VisionSupport.Unsupported && IsVerified);
}

/// <summary>图片识别能力探测结果。Support 为 Unknown 时 Message 说明原因。</summary>
/// <param name="Support">判定结论。</param>
/// <param name="Message">可直接展示给用户的说明。</param>
/// <param name="IsConclusion">
/// 这次探测是否形成了可落盘的结论。网络错误、超时、鉴权失败等属于「没测成」，不应覆盖上次结论。
/// </param>
public readonly record struct VisionDetectionResult(
    VisionSupport Support, string Message, bool IsConclusion = false);

/// <summary>
/// 判断当前模型能否识别图片，并提供「发一张测试图片看接口是否接受」的探测能力。
/// 探测只做一次轻量请求，不写配置；结论由调用方决定是否落盘。
/// </summary>
public static class VisionCapabilityService
{
    /// <summary>
    /// 探测用提示词。要求读出图中的随机数字：纯文本模型猜不出这串数字，
    /// 只有真正解析了图片的模型才能答对，比问颜色可靠得多。
    /// </summary>
    public const string ProbePrompt = "请读出图片中的数字，只回答数字本身。";

    /// <summary>生成一张写着 <paramref name="digits"/> 的探测图片（白底黑字 PNG）。</summary>
    public static byte[] CreateProbeImage(string digits) => ProbeImageRenderer.RenderDigits(digits);

    // 名称里出现这些片段，基本可以确定是视觉/多模态模型
    private static readonly string[] VisionModelMarkers =
    {
        "vision", "-vl", "vl-", "vl2", "multimodal", "omni",
        "gpt-4o", "gpt-4.1", "gpt-4-turbo", "gpt-5",
        "claude-3", "claude-4", "claude-sonnet", "claude-opus", "claude-haiku",
        "gemini", "glm-4v", "glm-4.1v", "glm-4.5v", "glm-4.6v",
        "qvq", "janus", "llava", "pixtral", "internvl", "minicpm-v",
        "phi-3-vision", "phi-4-multimodal", "grok-vision", "grok-4",
        "llama-4", "yi-vl", "kimi-latest", "doubao-vision", "step-1v", "mimo-vl",
        // DeepSeek V4.1 Flash 系列官方标注支持图片输入
        "deepseek-flash", "deepseek-v4.1-flash", "deepseek-v4-flash"
    };

    // 明确只处理文本的模型系列，命中后可以放心提示「不支持」
    private static readonly string[] TextOnlyModelMarkers =
    {
        "deepseek-v4-pro", "deepseek-r1", "text-", "-text",
        "embedding", "rerank", "tts", "whisper", "moderation"
    };

    // 错误响应里出现这些词，说明接口是因为「图片」拒绝的
    private static readonly string[] ImageRelatedErrorMarkers =
    {
        "image", "vision", "multimodal", "modality", "image_url", "图片", "图像", "视觉"
    };

    /// <summary>模型明确表示读不了图片的说法（自带「图片」上下文，命中即判定）。</summary>
    private static readonly string[] StrongRefusalMarkers =
    {
        "无法查看图片", "无法看到图片", "看不到图片", "无法识别图片", "不能识别图片",
        "无法读取图片", "无法读取图中", "不能查看图片", "无法访问图片", "没有收到图片",
        "没有看到图片", "不支持图片", "无法处理图片", "不能处理图片", "图片无法",
        " cannot see the image", "can't see the image", "unable to view the image",
        "cannot view the image", "no image was provided", "image is not supported",
        "does not support image", "doesn't support image", "text-only model", "text only model"
    };

    /// <summary>弱拒绝说法，需要正文里同时提到「图片/图」才判定，避免把「无法识别这个字」误判。</summary>
    private static readonly string[] WeakRefusalMarkers =
    {
        "看不到", "无法查看", "无法读取", "无法识别", "不能查看", "不能读取",
        "只能处理文本", "仅支持文本", "文本模型", "cannot see", "can't see", "unable to view"
    };

    /// <summary>把接口地址与模型名归一化成签名，用于判断上次检测结论是否还对应当前配置。</summary>
    public static string Signature(string? endpoint, string? model)
    {
        static string Normalize(string? value)
        {
            var text = (value ?? "").Trim().TrimEnd('/');
            return text.ToLowerInvariant();
        }

        return $"{Normalize(endpoint)}|{Normalize(model)}";
    }

    /// <summary>按模型名称推断是否支持图片：true 支持，false 不支持，null 无法判断。</summary>
    public static bool? GuessFromModelName(string? model)
    {
        var name = (model ?? "").Trim().ToLowerInvariant();
        if (name.Length == 0) return null;

        if (VisionModelMarkers.Any(marker => name.Contains(marker, StringComparison.Ordinal)))
            return true;

        if (TextOnlyModelMarkers.Any(marker => name.Contains(marker, StringComparison.Ordinal)))
            return false;

        return null;
    }

    /// <summary>读取设置里已保存的结论；模型或地址变化后旧结论自动失效，退回按名称推断。</summary>
    public static VisionCapabilityInfo Evaluate(AISettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Evaluate(settings.Endpoint, settings.Model, settings.VisionSupport, settings.VisionSupportSignature);
    }

    /// <summary>纯函数版本，方便单元测试与在未落盘时预览结果。</summary>
    public static VisionCapabilityInfo Evaluate(
        string? endpoint, string? model, string? storedValue, string? storedSignature)
    {
        if (!string.IsNullOrWhiteSpace(storedSignature) &&
            string.Equals(storedSignature, Signature(endpoint, model), StringComparison.Ordinal))
        {
            return VisionSupportValues.FromStorageValue(storedValue) switch
            {
                VisionSupport.Supported => new VisionCapabilityInfo(
                    VisionSupport.Supported, VisionCapabilitySource.Detection),
                VisionSupport.Unsupported => new VisionCapabilityInfo(
                    VisionSupport.Unsupported, VisionCapabilitySource.Detection),
                // 检测过但没能确认：比按名称猜测更可信，如实显示「无法确认」
                _ => new VisionCapabilityInfo(
                    VisionSupport.Unknown, VisionCapabilitySource.DetectionInconclusive)
            };
        }

        return GuessFromModelName(model) switch
        {
            true => new VisionCapabilityInfo(VisionSupport.Supported, VisionCapabilitySource.ModelName),
            false => new VisionCapabilityInfo(VisionSupport.Unsupported, VisionCapabilitySource.ModelName),
            _ => VisionCapabilityInfo.Unknown
        };
    }

    /// <summary>把探测结论写回设置（只写内存对象，落盘由调用方负责）。</summary>
    public static void ApplyDetection(AISettings settings, VisionSupport support, string endpoint, string model)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.VisionSupport = VisionSupportValues.ToStorageValue(support);
        settings.VisionSupportSignature = Signature(endpoint, model);
        settings.VisionSupportCheckedAt = DateTime.Now.ToString("O");
    }

    /// <summary>界面上的短标签，例如「支持」「可能不支持」「未检测」。</summary>
    public static string DescribeSupportLabel(VisionCapabilityInfo info)
    {
        return (info.Support, info.Source) switch
        {
            (VisionSupport.Supported, VisionCapabilitySource.Detection) => "支持",
            (VisionSupport.Unsupported, VisionCapabilitySource.Detection) => "不支持",
            (VisionSupport.Supported, VisionCapabilitySource.ModelName) => "可能支持",
            (VisionSupport.Unsupported, VisionCapabilitySource.ModelName) => "可能不支持",
            (VisionSupport.Unknown, VisionCapabilitySource.DetectionInconclusive) => "无法确认",
            _ => "未检测"
        };
    }

    /// <summary>界面上的详细说明。</summary>
    public static string DescribeSupportDetail(VisionCapabilityInfo info)
    {
        return (info.Support, info.Source) switch
        {
            (VisionSupport.Supported, VisionCapabilitySource.Detection) =>
                "已确认该模型能读取图片，插件与外部插件都可以直接调用图片识别。",
            (VisionSupport.Unsupported, VisionCapabilitySource.Detection) =>
                "已确认该模型只处理文字，图片识别调用会直接返回失败。",
            (VisionSupport.Supported, VisionCapabilitySource.ModelName) =>
                "按模型名称推断支持图片，点「检测」发一张测试图片即可确认。",
            (VisionSupport.Unsupported, VisionCapabilitySource.ModelName) =>
                "按模型名称推断不支持图片，点「检测」可以确认。",
            (VisionSupport.Unknown, VisionCapabilitySource.DetectionInconclusive) =>
                "上次检测没能确认（接口接受了图片却没读出数字）。可以再点一次「检测」，或换用支持视觉的模型。",
            _ => "点「检测」向接口发送一张测试图片，确认该模型是否支持图片识别。"
        };
    }

    /// <summary>空间有限时用的单行短说明（欢迎向导）。</summary>
    public static string DescribeSupportShort(VisionCapabilityInfo info)
    {
        return (info.Support, info.Source) switch
        {
            (VisionSupport.Supported, VisionCapabilitySource.Detection) => "已确认可读图",
            (VisionSupport.Unsupported, VisionCapabilitySource.Detection) => "已确认不可读图",
            (VisionSupport.Supported, VisionCapabilitySource.ModelName) => "按名称推断可读图",
            (VisionSupport.Unsupported, VisionCapabilitySource.ModelName) => "按名称推断不可读图",
            (VisionSupport.Unknown, VisionCapabilitySource.DetectionInconclusive) => "上次未能确认",
            _ => "点「检测」确认能否读图"
        };
    }

    /// <summary>
    /// 真实探测：现场生成一张写着随机数字的 PNG，要求模型把数字读出来。
    /// 读对 → 确认支持；明确表示看不到图片 → 不支持；读错或没读 → 结论不明。
    /// 网络异常或结论不明确时返回 Unknown，由界面提示用户重试。
    /// </summary>
    public static async Task<VisionDetectionResult> DetectAsync(
        string? endpoint,
        string? apiKey,
        string? model,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return new VisionDetectionResult(VisionSupport.Unknown, "请先填写 API 地址");
        if (string.IsNullOrWhiteSpace(apiKey))
            return new VisionDetectionResult(VisionSupport.Unknown, "请先填写 API Key");
        if (string.IsNullOrWhiteSpace(model))
            return new VisionDetectionResult(VisionSupport.Unknown, "请先填写模型名称");

        // 每次随机一串数字：纯文本模型猜不中，只有真的解析了图片才能答对
        var expectedDigits = Random.Shared.Next(1000, 10000).ToString(CultureInfo.InvariantCulture);
        var imageBase64 = Convert.ToBase64String(ProbeImageRenderer.RenderDigits(expectedDigits));

        var body = new Dictionary<string, object?>
        {
            ["model"] = model.Trim(),
            ["messages"] = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = ProbePrompt },
                        new
                        {
                            type = "image_url",
                            image_url = new { url = $"data:image/png;base64,{imageBase64}" }
                        }
                    }
                }
            },
            ["max_tokens"] = 64,
            ["temperature"] = 0.0,
            ["stream"] = false
        };

        // 探测固定关闭思考：回复短、快、可判定；万一某家不认这个字段，也会落到「未确认」而不是误判
        ThinkingRequestBuilder.Apply(body, endpoint, model, enableThinking: false, ThinkingEffort.Auto);

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            using var content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Trim()) { Content = content };
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey.Trim()}");

            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            var result = InterpretResponse((int)response.StatusCode, responseBody, expectedDigits);

            // 没得出结论时把响应片段写进日志，方便用户反馈时定位是哪种厂商格式
            if (result.Support == VisionSupport.Unknown)
                Logger.Info($"[图片识别] 探测未确认（{model}）: {result.Message}");

            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new VisionDetectionResult(VisionSupport.Unknown, "检测超时，请稍后重试或换用更快的模型。");
        }
        catch (HttpRequestException ex)
        {
            return new VisionDetectionResult(VisionSupport.Unknown, $"网络错误：{ex.Message}");
        }
        catch (Exception ex)
        {
            Logger.Info($"[图片识别] 能力检测失败: {ex.Message}");
            return new VisionDetectionResult(VisionSupport.Unknown, $"检测失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 解析探测请求的响应：成功响应看模型有没有读出图中的随机数字，失败响应看是否因图片被拒。
    /// 独立成纯函数，便于单元测试覆盖各种厂商返回。
    /// </summary>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="responseBody">响应正文。</param>
    /// <param name="expectedDigits">本次探测图片里的数字（由调用方生成）。</param>
    public static VisionDetectionResult InterpretResponse(
        int statusCode, string? responseBody, string expectedDigits)
    {
        if (statusCode is < 200 or >= 300)
        {
            var snippet = Clip(responseBody, 220);
            return ClassifyErrorResponse(statusCode, responseBody) == VisionSupport.Unsupported
                ? new VisionDetectionResult(
                    VisionSupport.Unsupported,
                    $"接口拒绝接收图片（{statusCode}）：{snippet}",
                    IsConclusion: true)
                : new VisionDetectionResult(
                    VisionSupport.Unknown,
                    $"无法判定（{statusCode}）：{snippet}");
        }

        var answer = ExtractAnswer(responseBody);

        // 部分网关失败也回 200，只在正文里塞 error；先把这种错误如实报出来
        var errorText = TryExtractError(responseBody);
        if (!string.IsNullOrWhiteSpace(errorText))
        {
            return ClassifyErrorResponse(400, errorText) == VisionSupport.Unsupported
                ? new VisionDetectionResult(
                    VisionSupport.Unsupported,
                    $"接口拒绝接收图片：{Clip(errorText, 200)}",
                    IsConclusion: true)
                : new VisionDetectionResult(
                    VisionSupport.Unknown,
                    $"接口返回错误：{Clip(errorText, 200)}");
        }

        // 读出随机数字 → 确凿证据，比问颜色可靠（颜色是可以猜的）。
        // 思考型模型可能把答案写进 reasoning_content，也要一并检查。
        var digits = ExtractDigits(answer.Text);
        var reasoningDigits = ExtractDigits(answer.Reasoning);
        var expectedFound =
            (!string.IsNullOrEmpty(expectedDigits) &&
             digits.Contains(expectedDigits, StringComparison.Ordinal)) ||
            (!string.IsNullOrEmpty(expectedDigits) &&
             reasoningDigits.Contains(expectedDigits, StringComparison.Ordinal));

        if (expectedFound)
        {
            return new VisionDetectionResult(
                VisionSupport.Supported,
                $"已确认支持图片识别（模型读出数字：{expectedDigits}）",
                IsConclusion: true);
        }

        var visible = answer.Text.Length > 0 ? answer.Text : answer.Reasoning;

        if (answer.Text.Length > 0 && LooksLikeImageRefusal(answer.Text))
        {
            return new VisionDetectionResult(
                VisionSupport.Unsupported,
                $"模型表示无法读取图片：{Clip(answer.Text, 120)}",
                IsConclusion: true);
        }

        // 数字对不上：可能读错，也可能根本没看图。既不能算支持，也不能断言不支持
        if (digits.Length > 0)
        {
            return new VisionDetectionResult(
                VisionSupport.Unknown,
                $"模型能接收图片，但读出的数字与测试图不符（回答：{Clip(answer.Text, 40)}），" +
                "可能没有真正解析图片，可以重试或换模型。",
                IsConclusion: true);
        }

        // 只有思考内容、没有正文：多半是思考模式把输出长度吃光了
        if (reasoningDigits.Length > 0 || (answer.Text.Length == 0 && answer.Reasoning.Length > 0))
        {
            return new VisionDetectionResult(
                VisionSupport.Unknown,
                "接口只返回了思考内容，没有正文，无法确认是否读到图片。可以换用非思考模型，或调大最大输出长度后重试。",
                IsConclusion: true);
        }

        if (visible.Length == 0)
        {
            var snippet = Clip(responseBody, 160);
            return new VisionDetectionResult(
                VisionSupport.Unknown,
                snippet.Length == 0
                    ? "接口返回成功，但响应里没有任何内容，无法确认是否读到图片。"
                    : $"接口返回成功，但没有解析到回复内容（响应片段：{snippet}），无法确认是否读到图片。");
        }

        return new VisionDetectionResult(
            VisionSupport.Unknown,
            $"接口接受了图片，但回答里没有数字（回答：{Clip(visible, 60)}），无法确认是否真的读到图片。",
            IsConclusion: true);
    }

    /// <summary>
    /// 按错误响应判断接口是否因为「不接受图片」而失败。
    /// 只有错误信息明确提到图片/多模态，并且返回 4xx 时才判定为不支持。
    /// </summary>
    public static VisionSupport ClassifyErrorResponse(int statusCode, string? responseBody)
    {
        if (statusCode is < 400 or >= 500) return VisionSupport.Unknown;
        if (string.IsNullOrWhiteSpace(responseBody)) return VisionSupport.Unknown;

        var text = responseBody.ToLowerInvariant();
        var mentionsImage = ImageRelatedErrorMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));
        return mentionsImage ? VisionSupport.Unsupported : VisionSupport.Unknown;
    }

    /// <summary>部分网关即使失败也回 HTTP 200，只在正文里放 error。把这类错误暴露出来，避免误报「无法解析」。</summary>
    private static string? TryExtractError(string? responseBody)
    {
        var body = (responseBody ?? "").Trim();
        if (body.Length == 0 || !body.StartsWith('{')) return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("choices", out _)) return null; // 正常响应，不是错误

            if (root.TryGetProperty("error", out var error))
            {
                return error.ValueKind switch
                {
                    JsonValueKind.String => error.GetString(),
                    JsonValueKind.Object when error.TryGetProperty("message", out var message) &&
                                               message.ValueKind == JsonValueKind.String
                        => message.GetString(),
                    _ => null
                };
            }

            // 有正文容器时不要把 message 当错误（少数厂商用 message 承载正文）
            if (root.TryGetProperty("output", out _) ||
                root.TryGetProperty("reply", out _) ||
                root.TryGetProperty("data", out _))
            {
                return null;
            }

            if (root.TryGetProperty("message", out var rootMessage) &&
                rootMessage.ValueKind == JsonValueKind.String)
            {
                return rootMessage.GetString();
            }
        }
        catch (JsonException)
        {
            // 不是 JSON，交给正常解析流程
        }

        return null;
    }

    private readonly record struct ProbeAnswer(string Text, string Reasoning);

    /// <summary>
    /// 从各种可能的响应形态里取正文与思考内容：OpenAI choices[].message/delta、SSE 流、
    /// output.text、reply 等。厂商写法差异很大，这里宁可多认几种，也不要让用户看到「无法解析」。
    /// </summary>
    private static ProbeAnswer ExtractAnswer(string? responseBody)
    {
        var text = new StringBuilder();
        var reasoning = new StringBuilder();

        foreach (var payload in SplitPayloads(responseBody))
        {
            try
            {
                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;
                AppendKnownField(root, "content", text);
                AppendKnownField(root, "text", text);
                AppendKnownField(root, "output_text", text);
                AppendKnownField(root, "reply", text);
                AppendKnownField(root, "reasoning_content", reasoning);
                AppendKnownField(root, "reasoning", reasoning);
            }
            catch (JsonException)
            {
                // 不是 JSON 的片段（心跳、注释）直接跳过
            }
        }

        return new ProbeAnswer(text.ToString().Trim(), reasoning.ToString().Trim());
    }

    /// <summary>把响应切成 JSON 负载：普通响应整段返回，SSE 响应逐个 data: 行返回。</summary>
    private static IReadOnlyList<string> SplitPayloads(string? responseBody)
    {
        var body = (responseBody ?? "").Trim();
        if (body.Length == 0) return Array.Empty<string>();

        var looksLikeSse =
            body.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("\ndata:", StringComparison.OrdinalIgnoreCase);
        if (!looksLikeSse)
            return new[] { body };

        var payloads = new List<string>();
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;

            var payload = trimmed["data:".Length..].Trim();
            if (payload.Length == 0 || payload == "[DONE]") continue;
            payloads.Add(payload);
        }

        return payloads.Count > 0 ? payloads : new[] { body };
    }

    /// <summary>在顶层、choices[].message/delta、output、data、result 等常见容器里查找同名字段。</summary>
    private static void AppendKnownField(JsonElement root, string name, StringBuilder target)
    {
        if (root.ValueKind != JsonValueKind.Object) return;

        AppendFieldValue(root, name, target);

        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
        {
            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.ValueKind != JsonValueKind.Object) continue;
                if (choice.TryGetProperty("message", out var message))
                    AppendFieldValue(message, name, target);
                if (choice.TryGetProperty("delta", out var delta))
                    AppendFieldValue(delta, name, target);
            }
        }

        foreach (var containerName in new[] { "output", "data", "result" })
        {
            if (root.TryGetProperty(containerName, out var container) &&
                container.ValueKind == JsonValueKind.Object)
            {
                AppendFieldValue(container, name, target);
            }
        }
    }

    /// <summary>取字段值：字符串直接拼，分段数组（多模态返回）拼接各段 text。</summary>
    private static void AppendFieldValue(JsonElement element, string name, StringBuilder target)
    {
        if (element.ValueKind != JsonValueKind.Object) return;
        if (!element.TryGetProperty(name, out var value)) return;

        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                target.Append(value.GetString());
                break;

            case JsonValueKind.Array:
                foreach (var part in value.EnumerateArray())
                {
                    if (part.ValueKind == JsonValueKind.String)
                        target.Append(part.GetString());
                    else if (part.ValueKind == JsonValueKind.Object &&
                             part.TryGetProperty("text", out var partText) &&
                             partText.ValueKind == JsonValueKind.String)
                        target.Append(partText.GetString());
                }

                break;
        }
    }

    /// <summary>抽出回答里的所有数字，容忍「4 8 2 1」「4821。」等写法。</summary>
    private static string ExtractDigits(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch is >= '0' and <= '9')
                builder.Append(ch);
        }

        return builder.ToString();
    }

    /// <summary>判断模型是不是在说「我看不到图片」，而不是在回答里提到这些词。</summary>
    private static bool LooksLikeImageRefusal(string text)
    {
        if (ContainsAny(text, StrongRefusalMarkers)) return true;

        if (!ContainsAny(text, WeakRefusalMarkers)) return false;

        // 弱说法必须同时有图片上下文，避免把「无法识别这个字」误判成不支持图片
        return text.Contains("图片", StringComparison.Ordinal) ||
               text.Contains("图像", StringComparison.Ordinal) ||
               text.Contains("图中", StringComparison.Ordinal) ||
               text.Contains("这张图", StringComparison.Ordinal) ||
               text.Contains("image", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("picture", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAny(string text, IEnumerable<string> markers) =>
        markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static string Clip(string? text, int maxLength)
    {
        var value = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        if (value.Length <= maxLength) return value;
        return value[..maxLength] + "…";
    }
}
