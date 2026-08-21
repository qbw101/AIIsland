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
