namespace ClassIsland.AISmartClass.Services.Automation;

/// <summary>
/// 触发器：当某个时机到达时广播 Triggered，引擎据此评估规则。
/// Phase 1 示例：状态变化触发器。
/// </summary>
public interface IRuleTrigger
{
    event Action<RuleContext>? Triggered;

    void Start();

    void Stop();
}
