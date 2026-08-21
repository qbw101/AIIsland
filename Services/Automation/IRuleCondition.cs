namespace ClassIsland.AISmartClass.Services.Automation;

/// <summary>
/// 条件：对触发时的上下文进行判定，返回是否满足。
/// Phase 1 示例：当前课表状态条件。
/// </summary>
public interface IRuleCondition
{
    bool Evaluate(RuleContext context);
}
