using ClassIsland.Shared.Enums;

namespace ClassIsland.AISmartClass.Services.Automation.Conditions;

/// <summary>
/// 当前课表状态条件：判断触发时的状态是否等于期望状态。
/// </summary>
public class CurrentStateCondition : IRuleCondition
{
    public TimeState ExpectedState { get; init; }

    public bool Evaluate(RuleContext context)
        => context.CurrentState == ExpectedState;
}
