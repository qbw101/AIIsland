using ClassIsland.AISmartClass;
using ClassIsland.Core.Abstractions.Services;

namespace ClassIsland.AISmartClass.Services.Automation.Actions;

/// <summary>
/// 调用 AI 生成动作：生成学习提示（可指定场景与重点，缺省时取上下文）。
/// 结果写入日志，供 Phase 1 端到端验证。
/// </summary>
public class GenerateAiAction : IAction
{
    public string? Scene { get; init; }

    public string? Focus { get; init; }

    public async Task ExecuteAsync(RuleContext context, CancellationToken ct = default)
    {
        var ai = Plugin.GetAIService();
        if (ai == null)
        {
            return;
        }

        var scene = Scene ?? context.CurrentState.ToString();
        var focus = Focus ?? context.NextSubjectName ?? "今日课程";
        var result = await ai.GenerateLearningHintStream(scene, focus, _ => { }, ct);
        Logger.Info($"[Automation] AI 生成结果: {result}");
    }
}
