using System;
using System.Collections.Generic;
using ClassIsland.AISmartClass.Models;

namespace ClassIsland.AISmartClass.Services;

/// <summary>接口在「思考参数」上的写法差异。</summary>
public enum ThinkingApiFamily
{
    /// <summary>DeepSeek：<c>thinking.type</c> + <c>reasoning_effort</c>。</summary>
    DeepSeek,

    /// <summary>百炼 / Qwen：<c>enable_thinking</c> + <c>reasoning_effort</c>。</summary>
    Qwen,

    /// <summary>OpenAI 官方：只认 <c>reasoning_effort</c>，发 <c>thinking</c> 会被 400 拒绝。</summary>
    OpenAi,

    /// <summary>Gemini：<c>reasoning_effort</c>，部分模型无法关闭思考。</summary>
    Gemini,

    /// <summary>智谱 / Kimi / MiniMax / 豆包：认 <c>thinking.type</c> + <c>reasoning_effort</c>。</summary>
    ThinkingObject,

    /// <summary>其它 OpenAI 兼容接口：只用最通用的 <c>reasoning_effort</c>，不猜厂商字段。</summary>
    Generic
}

/// <summary>
/// 把「深度思考 + 思考强度」翻译成各家接口真正认识的请求字段。
/// <para>
/// 不同厂商的写法并不统一，乱塞字段会被 400 拒绝，所以按型号/地址分族处理：
/// DeepSeek 用 <c>thinking</c> 开关加 <c>reasoning_effort</c>；百炼/Qwen 用 <c>enable_thinking</c>；
/// OpenAI 官方只接受 <c>reasoning_effort</c>（关闭用 "none"）；
/// 其它兼容接口才用最通用的 <c>reasoning_effort</c>，不会额外塞 <c>thinking</c>。
/// </para>
/// </summary>
public static class ThinkingRequestBuilder
{
    /// <summary>按模型名优先、接口地址兜底判断接口族。</summary>
    public static ThinkingApiFamily DetectFamily(string? endpoint, string? model)
    {
        var name = (model ?? "").Trim().ToLowerInvariant();
        var url = (endpoint ?? "").Trim().ToLowerInvariant();

        if (name.StartsWith("deepseek-") || name == "deepseek" || url.Contains("deepseek"))
            return ThinkingApiFamily.DeepSeek;

        if (name.StartsWith("qwen") || name.Contains("qwen") || name.StartsWith("qvq") ||
            url.Contains("dashscope") || url.Contains("aliyuncs"))
            return ThinkingApiFamily.Qwen;

        if (name.Contains("gemini") || url.Contains("generativelanguage") || url.Contains("googleapis"))
            return ThinkingApiFamily.Gemini;

        if (name.StartsWith("gpt-") || name.StartsWith("chatgpt") ||
            name.StartsWith("o1") || name.StartsWith("o3") || name.StartsWith("o4") || name.StartsWith("o5") ||
            url.Contains("api.openai.com") || url.Contains("openai.azure.com"))
            return ThinkingApiFamily.OpenAi;

        if (name.Contains("glm") || name.Contains("kimi") || name.Contains("moonshot") ||
            name.Contains("minimax") || name.Contains("doubao") || name.Contains("seed-") ||
            url.Contains("bigmodel") || url.Contains("moonshot") ||
            url.Contains("minimax") || url.Contains("volces"))
            return ThinkingApiFamily.ThinkingObject;

        return ThinkingApiFamily.Generic;
    }

    /// <summary>
    /// 把思考相关字段写进请求体。<paramref name="enableThinking"/> 为 false 时尽量下发「关闭」写法，
    /// 型号确实不支持关闭（OpenAI/Gemini 以外）就不下发，避免 400。
    /// </summary>
    public static void Apply(
        IDictionary<string, object?> body,
        string? endpoint,
        string? model,
        bool enableThinking,
        ThinkingEffort effort)
    {
        ArgumentNullException.ThrowIfNull(body);

        var family = DetectFamily(endpoint, model);
        var effortValue = enableThinking ? effort.ToRequestValue() : null;

        switch (family)
        {
            case ThinkingApiFamily.DeepSeek:
            case ThinkingApiFamily.ThinkingObject:
                body["thinking"] = new { type = enableThinking ? "enabled" : "disabled" };
                if (effortValue != null) body["reasoning_effort"] = effortValue;
                break;

            case ThinkingApiFamily.Qwen:
                // 只有 qwen3 起的混合推理型号认 enable_thinking；老型号塞了会报参数不支持
                if (IsHybridQwen(model))
                {
                    body["enable_thinking"] = enableThinking;
                    if (effortValue != null) body["reasoning_effort"] = effortValue;
                }
                else if (enableThinking && effortValue != null)
                {
                    body["reasoning_effort"] = effortValue;
                }

                break;

            case ThinkingApiFamily.OpenAi:
                // OpenAI 没有 thinking 开关，只能靠 reasoning_effort 控制；关闭用 none。
                // gpt-4o / gpt-4.1 这类不支持推理参数的型号一律不下发，避免 400。
                if (!SupportsReasoningEffort(model)) break;

                if (!enableThinking)
                    body["reasoning_effort"] = "none";
                else if (effortValue != null)
                    body["reasoning_effort"] = effortValue;
                break;

            case ThinkingApiFamily.Gemini:
            case ThinkingApiFamily.Generic:
                // 这两类只下发通用的 reasoning_effort；关闭时不下发（部分模型无法关闭思考）
                if (effortValue != null) body["reasoning_effort"] = effortValue;
                break;
        }
    }

    /// <summary>开启深度思考时，为思考过程额外预留的输出 token。</summary>
    public static int GetReservedTokens(bool enableThinking, ThinkingEffort effort) =>
        enableThinking ? effort.GetThinkingReserveTokens() : 0;

    /// <summary>
    /// 计算请求实际使用的输出上限。<paramref name="maxTokens"/> 为 0 表示不限制：
    /// 不预留、不下发 max_tokens，由模型/接口的默认上限决定。
    /// </summary>
    public static int GetEffectiveMaxTokens(int maxTokens, bool enableThinking, ThinkingEffort effort) =>
        maxTokens <= 0 ? 0 : maxTokens + GetReservedTokens(enableThinking, effort);

    /// <summary>qwen3 起的混合推理型号才支持 <c>enable_thinking</c>。</summary>
    private static bool IsHybridQwen(string? model) =>
        (model ?? "").Contains("qwen3", StringComparison.OrdinalIgnoreCase);

    /// <summary>只有推理型号才认 <c>reasoning_effort</c>；gpt-4o / gpt-4.1 等不支持。</summary>
    private static bool SupportsReasoningEffort(string? model)
    {
        var name = (model ?? "").Trim().ToLowerInvariant();
        return name.StartsWith("gpt-5") || name.StartsWith("gpt-6") ||
               name.StartsWith("o1") || name.StartsWith("o3") ||
               name.StartsWith("o4") || name.StartsWith("o5");
    }
}
