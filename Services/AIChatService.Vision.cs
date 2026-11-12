using System.IO;
using ClassIsland.AISmartClass.Models;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 待识别的图片。只用原始字节 + MIME 类型描述，插件不依赖任何图像处理库，
/// 调用方可以自己解码、缩放后传入，也可以直接用 <see cref="FromFile"/> 读文件。
/// </summary>
public sealed class VisionImage
{
    /// <summary>单张图片大小上限。常见厂商限制在 20MB 以内，超过限制会在本地直接拒绝。</summary>
    public const int MaxBytes = 20 * 1024 * 1024;

    public VisionImage(byte[] data, string? mimeType = null)
    {
        if (data == null || data.Length == 0)
            throw new ArgumentException("图片内容为空", nameof(data));

        Data = data;

        var normalized = (mimeType ?? "").Trim().ToLowerInvariant();
        MimeType = normalized.StartsWith("image/", StringComparison.Ordinal)
            ? normalized
            : DetectMimeType(data);
    }

    /// <summary>图片原始字节。</summary>
    public byte[] Data { get; }

    /// <summary>图片 MIME 类型，如 image/png、image/jpeg。</summary>
    public string MimeType { get; }

    /// <summary>转成 data URI，直接放进 image_url.url，不经过任何图片托管服务。</summary>
    public string ToDataUri() => $"data:{MimeType};base64,{Convert.ToBase64String(Data)}";

    /// <summary>按文件头判断图片格式，识别不出来时按 PNG 处理。</summary>
    public static string DetectMimeType(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 8 &&
            data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            return "image/png";

        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return "image/jpeg";

        if (data.Length >= 6 && data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46)
            return "image/gif";

        if (data.Length >= 12 &&
            data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 &&
            data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50)
            return "image/webp";

        if (data.Length >= 2 && data[0] == 0x42 && data[1] == 0x4D)
            return "image/bmp";

        return "image/png";
    }

    /// <summary>读取本地图片文件。</summary>
    public static VisionImage FromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("图片路径为空", nameof(path));

        var bytes = File.ReadAllBytes(path);
        return new VisionImage(bytes);
    }
}

/// <summary>
/// AIChatService 的图片识别部分：把图片作为一条 user 消息的多模态内容发给模型。
/// 与文本对话共用重试、缓存和调用日志，只是请求体里的 content 变成数组。
/// </summary>
public partial class AIChatService
{
    /// <summary>当前模型是否允许发送图片请求（已确认不支持时返回 false）。</summary>
    public bool CanSendImages => VisionCapability.AllowsImageInput;

    /// <summary>
    /// 图片识别对话：把 <paramref name="image"/> 和 <paramref name="userMessage"/> 一起发给模型。
    /// </summary>
    /// <param name="systemPrompt">系统提示词，定义模型的身份与回答要求。</param>
    /// <param name="userMessage">与图片一起发送的文字，例如「这张作业照片里有哪些题」。</param>
    /// <param name="image">图片内容。建议先压缩到 1024px 左右，减少 token 消耗。</param>
    /// <param name="temperature">温度，-1 表示按语气风格自动取值。</param>
    /// <param name="ct">取消令牌。</param>
    /// <param name="throwOnError">true 时失败抛异常，false 时返回本地降级句子。</param>
    /// <param name="bypassCache">true 时本次不读也不写缓存。</param>
    /// <returns>模型对图片的文字回答。</returns>
    public async Task<string> ChatWithImageAsync(
        string systemPrompt,
        string userMessage,
        VisionImage image,
        double temperature = -1,
        CancellationToken ct = default,
        bool throwOnError = false,
        bool bypassCache = false,
        bool? enableDeepThinking = null,
        ThinkingEffort? thinkingEffort = null)
    {
        var response = await ChatWithImageResponseAsync(
            systemPrompt, userMessage, image, temperature, ct,
            throwOnError, bypassCache, enableDeepThinking, thinkingEffort).ConfigureAwait(false);
        return response.Content;
    }

    /// <summary>
    /// 与 <see cref="ChatWithImageAsync"/> 相同，但同时返回思考过程（<c>reasoning_content</c>）。
    /// </summary>
    public async Task<AIChatResponse> ChatWithImageResponseAsync(
        string systemPrompt,
        string userMessage,
        VisionImage image,
        double temperature = -1,
        CancellationToken ct = default,
        bool throwOnError = false,
        bool bypassCache = false,
        bool? enableDeepThinking = null,
        ThinkingEffort? thinkingEffort = null)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (image.Data.Length > VisionImage.MaxBytes)
        {
            throw new ArgumentException(
                $"图片过大（{image.Data.Length / 1024 / 1024.0:F1} MB），请压缩到 20 MB 以内", nameof(image));
        }

        // 已确认不支持图片的模型不必浪费一次请求，直接给出明确原因。
        if (!VisionCapability.AllowsImageInput)
        {
            var reason = $"当前模型「{Model}」已确认不支持图片识别，请换用支持视觉的模型（名称通常包含 vl / vision）";
            if (throwOnError)
                throw new InvalidOperationException(reason);

            await LogInvocationAsync(
                "图片识别",
                systemPrompt,
                userMessage,
                "",
                "本地降级",
                false,
                Model,
                reason).ConfigureAwait(false);
            return new AIChatResponse(_fallback.GetErrorPhraseFromReason(reason), "");
        }

        return await ExecuteChatAsync(
            systemPrompt, userMessage, image, "图片识别",
            temperature, ct, throwOnError, bypassCache,
            enableDeepThinking, thinkingEffort).ConfigureAwait(false);
    }

    /// <summary>
    /// 带图片的流式对话：模型每产出一段增量就回调一次，最终返回正文与思考过程。
    /// 已确认不支持图片的模型会直接给出降级内容，不发出请求。
    /// </summary>
    public async Task<AIChatResponse> ChatWithImageStreamAsync(
        string systemPrompt,
        string userMessage,
        Action<AIChatStreamUpdate> onUpdate,
        VisionImage image,
        double temperature = -1,
        CancellationToken ct = default,
        bool throwOnError = false,
        bool bypassCache = false,
        bool? enableDeepThinking = null,
        ThinkingEffort? thinkingEffort = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(onUpdate);

        if (image.Data.Length > VisionImage.MaxBytes)
        {
            throw new ArgumentException(
                $"图片过大（{image.Data.Length / 1024 / 1024.0:F1} MB），请压缩到 20 MB 以内", nameof(image));
        }

        // 已确认不支持图片的模型不必浪费一次请求，直接给出明确原因。
        if (!VisionCapability.AllowsImageInput)
        {
            var reason = $"当前模型「{Model}」已确认不支持图片识别，请换用支持视觉的模型（名称通常包含 vl / vision）";
            if (throwOnError)
                throw new InvalidOperationException(reason);

            var fallback = _fallback.GetErrorPhraseFromReason(reason);
            onUpdate(new AIChatStreamUpdate(fallback, "", fallback, ""));
            await LogInvocationAsync(
                "图片流式识别",
                systemPrompt,
                userMessage,
                fallback,
                "本地降级",
                true,
                Model,
                reason).ConfigureAwait(false);
            return new AIChatResponse(fallback, "");
        }

        return await ChatStreamResponseAsync(
            systemPrompt, userMessage, onUpdate, temperature, ct,
            enableDeepThinking, thinkingEffort, image).ConfigureAwait(false);
    }
}
