namespace ClassIsland.AISmartClass.Services.Automation;

/// <summary>
/// 动作：规则条件满足后执行的具体行为。
/// Phase 1 示例：显示通知、调用 AI 生成。
/// </summary>
public interface IAction
{
    Task ExecuteAsync(RuleContext context, CancellationToken ct = default);
}
