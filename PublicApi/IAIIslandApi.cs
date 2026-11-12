using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClassIsland.AISmartClass.Models;

namespace ClassIsland.AISmartClass.PublicApi;

/// <summary>
/// AIIsland 对外暴露的 AI 能力接口。
/// 其他 ClassIsland 插件可通过声明插件依赖后，使用
/// <c>IAppHost.GetService&lt;IAIIslandApi&gt;()</c> 获取此接口。
/// </summary>
/// <remarks>
/// 所有方法调用前会经过授权检查：
/// <list type="bullet">
/// <item>未授权插件：弹出确认对话框，用户选择"允许"或"允许并记住"后执行。</item>
/// <item>已授权插件：直接执行，不再弹窗。</item>
/// </list>
/// 所有外部调用都会写入 AI 调用日志，记录调用方插件 ID 和方法名。
/// </remarks>
public interface IAIIslandApi
{
    /// <summary>AI 服务是否已配置（API Key 非空）。此属性不需要授权。</summary>
    bool IsConfigured { get; }

    /// <summary>当前使用的模型名称。此属性不需要授权。</summary>
    string? ModelName { get; }

    /// <summary>
    /// 当前模型是否被判定为支持图片识别（视觉输入）。此属性不需要授权。
    /// 判定可能来自真实检测，也可能来自模型名称推断，可用 <see cref="IsVisionSupportVerified"/> 区分。
    /// </summary>
    bool SupportsVision { get; }

    /// <summary>
    /// 图片识别能力的判定级别：<c>supported</c> / <c>unsupported</c> / <c>unknown</c>。
    /// 此属性不需要授权。
    /// </summary>
    string VisionSupportLevel { get; }

    /// <summary>
    /// <see cref="VisionSupportLevel"/> 是否来自真实检测（true 表示可信结论，false 表示按模型名称推断）。
    /// 此属性不需要授权。
    /// </summary>
    bool IsVisionSupportVerified { get; }

    /// <summary>
    /// 基础 AI 对话。调用方提供系统提示词和用户消息，返回 AI 生成内容。
    /// </summary>
    /// <param name="systemPrompt">系统提示词，定义 AI 角色和行为。</param>
    /// <param name="userMessage">用户消息内容。</param>
    /// <param name="options">可选参数：温度、用途描述和缓存行为。MaxTokens 当前为预留字段。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>AI 对话结果，包含文本内容和是否成功标志。</returns>
    Task<AIIslandChatResult> ChatAsync(
        string systemPrompt,
        string userMessage,
        AIIslandChatOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// 流式 AI 对话：模型每产出一段增量就回调一次，最后返回完整结果（含思考过程）。
    /// </summary>
    /// <remarks>
    /// 回调在线程池线程上执行，需要更新 UI 时请自行切回 UI 线程；回调内抛出的异常会中断整次调用。
    /// 缓存命中时会按字符回放，同样走回调。取消令牌被取消时抛出 <see cref="OperationCanceledException"/>。
    /// </remarks>
    /// <param name="systemPrompt">系统提示词，定义 AI 角色和行为。</param>
    /// <param name="userMessage">用户消息内容。</param>
    /// <param name="onUpdate">增量回调，参数包含本次增量与截至目前的完整快照。</param>
    /// <param name="options">可选参数：温度、深度思考、思考强度和缓存行为。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>最终完整结果，<c>Content</c> 与 <c>Reasoning</c> 分别是正文与思考过程。</returns>
    Task<AIIslandChatResult> ChatStreamAsync(
        string systemPrompt,
        string userMessage,
        Action<AIIslandStreamUpdate> onUpdate,
        AIIslandChatOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// 带图片的 AI 对话（图片识别）。图片以原始字节传入，可直接来自截图、相册或网络下载。
    /// </summary>
    /// <remarks>
    /// 图片以 data URI 内联到请求体中，不经过任何图片托管服务。
    /// 当前模型已确认不支持图片识别时，会直接返回失败并说明原因；
    /// 调用前可用 <see cref="SupportsVision"/> 判断，避免无谓的等待。
    /// </remarks>
    /// <param name="systemPrompt">系统提示词，定义 AI 角色和行为。</param>
    /// <param name="userMessage">与图片一起发送的文字，例如「这张图里有什么」。</param>
    /// <param name="imageData">图片原始字节，支持 PNG / JPEG / GIF / WebP / BMP，单张不超过 20 MB。</param>
    /// <param name="imageMimeType">图片 MIME 类型；为空时按文件头自动识别。</param>
    /// <param name="options">可选参数：温度、用途描述和缓存行为。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>AI 对图片的回答，包含文本内容和是否成功标志。</returns>
    Task<AIIslandChatResult> ChatWithImageAsync(
        string systemPrompt,
        string userMessage,
        byte[] imageData,
        string? imageMimeType = null,
        AIIslandChatOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// 带图片的流式对话：模型每产出一段增量就回调一次，最后返回完整结果（含思考过程）。
    /// </summary>
    /// <remarks>
    /// 回调在线程池线程上执行，需要更新 UI 时请自行切回 UI 线程；回调内抛出的异常会中断整次调用。
    /// 图片以 data URI 内联到请求体中，不经过任何图片托管服务。
    /// 当前模型已确认不支持图片识别时，回调会直接收到一段本地降级内容，返回结果 <c>Success=true</c> 且
    /// <c>IsFallback=true</c>；调用前可用 <see cref="SupportsVision"/> 判断，避免无谓的等待。
    /// </remarks>
    /// <param name="systemPrompt">系统提示词，定义 AI 角色和行为。</param>
    /// <param name="userMessage">与图片一起发送的文字。</param>
    /// <param name="onUpdate">增量回调，参数包含本次增量与截至目前的完整快照。</param>
    /// <param name="imageData">图片原始字节，支持 PNG / JPEG / GIF / WebP / BMP，单张不超过 20 MB。</param>
    /// <param name="imageMimeType">图片 MIME 类型；为空时按文件头自动识别。</param>
    /// <param name="options">可选参数：温度、深度思考、思考强度和缓存行为。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>最终完整结果，<c>Content</c> 与 <c>Reasoning</c> 分别是正文与思考过程。</returns>
    Task<AIIslandChatResult> ChatWithImageStreamAsync(
        string systemPrompt,
        string userMessage,
        Action<AIIslandStreamUpdate> onUpdate,
        byte[] imageData,
        string? imageMimeType = null,
        AIIslandChatOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// 解析自然语言作业描述为结构化作业条目。
    /// AI 不可用时自动回退到本地规则引擎。
    /// </summary>
    /// <param name="input">用户输入的作业描述文本。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>解析结果，包含科目、内容、截止日期、类型和预计耗时。</returns>
    Task<HomeworkParseResult> ParseHomeworkAsync(
        string input,
        CancellationToken ct = default);

    /// <summary>
    /// 解析自然语言提醒描述为结构化提醒。
    /// </summary>
    /// <param name="input">用户输入的提醒描述文本。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>解析结果，包含提醒类型、时间、科目和内容。</returns>
    Task<ReminderParseResult> ParseReminderAsync(
        string input,
        CancellationToken ct = default);

    /// <summary>
    /// 生成今日课表总结。
    /// </summary>
    /// <param name="subjects">今日课程科目列表。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>AI 生成的课表总结文本。</returns>
    Task<string> SummarizeTodayAsync(
        List<string> subjects,
        CancellationToken ct = default);

    /// <summary>
    /// 生成当前学习提示。
    /// </summary>
    /// <param name="subjects">兼容参数。当前实现将列表连接后作为“当前状态”文本。</param>
    /// <param name="focusSubject">学习重点科目；为空时使用“自主学习”。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>AI 生成的学习提示文本。</returns>
    Task<string> GenerateLearningHintAsync(
        List<string> subjects,
        string? focusSubject = null,
        CancellationToken ct = default);

    /// <summary>
    /// 估算今日作业量。
    /// </summary>
    /// <param name="subjects">今日课程科目列表。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>AI 生成的作业量估算文本。</returns>
    Task<string> EstimateHomeworkLoadAsync(
        List<string> subjects,
        CancellationToken ct = default);

    /// <summary>立即生成智能每日简报并显示通知。方法名为兼容旧版保留。</summary>
    Task<string> TriggerBeforeSchoolReminderAsync(CancellationToken ct = default);

    /// <summary>触发课间贴心提醒并显示通知。</summary>
    Task<string> TriggerBreakReminderAsync(CancellationToken ct = default);

    /// <summary>触发放学贴心总结并显示通知。</summary>
    Task<string> TriggerAfterSchoolSummaryAsync(CancellationToken ct = default);
}
