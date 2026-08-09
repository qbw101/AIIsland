using ClassIsland.Core.Abstractions.Services;

namespace ClassIsland.AISmartClass.Services.Automation;

/// <summary>
/// 自动化引擎：持有规则列表，订阅各规则的触发器，触发后依次评估条件，
/// 全部满足则执行动作列表。Phase 1 验证闭环用（触发→条件→动作）。
/// </summary>
public class AutomationEngine : IDisposable
{
    private readonly List<AutomationRule> _rules = new();
    private readonly IList<IRuleTrigger> _activeTriggers = new List<IRuleTrigger>();
    private readonly ILessonsService _lessons;
    private bool _started;

    public AutomationEngine(ILessonsService lessons)
    {
        _lessons = lessons;
    }

    public void AddRule(AutomationRule rule)
    {
        _rules.Add(rule);
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        foreach (var rule in _rules)
        {
            SubscribeTrigger(rule);
        }

        Logger.Info($"[Automation] 引擎已启动，规则数={_rules.Count}");
    }

    private void SubscribeTrigger(AutomationRule rule)
    {
        if (rule.Trigger == null)
        {
            return;
        }

        rule.Trigger.Triggered += ctx => OnTriggered(rule, ctx);
        rule.Trigger.Start();
        _activeTriggers.Add(rule.Trigger);
    }

    private void OnTriggered(AutomationRule rule, RuleContext ctx)
    {
        if (!rule.IsEnabled)
        {
            return;
        }

        _ = ExecuteRuleAsync(rule, ctx);
    }

    private async Task ExecuteRuleAsync(AutomationRule rule, RuleContext ctx)
    {
        try
        {
            foreach (var condition in rule.Conditions)
            {
                if (!condition.Evaluate(ctx))
                {
                    Logger.Info($"[Automation] 条件不满足，跳过规则: {rule.Name}");
                    return;
                }
            }

            foreach (var action in rule.Actions)
            {
                await action.ExecuteAsync(ctx);
            }

            Logger.Info($"[Automation] 规则执行完成: {rule.Name}");
        }
        catch (Exception ex)
        {
            Logger.Error($"[Automation] 规则执行失败: {rule.Name}, {ex.Message}");
        }
    }

    public void Stop()
    {
        foreach (var trigger in _activeTriggers)
        {
            trigger.Stop();
        }

        _activeTriggers.Clear();
        _started = false;
    }

    public void Dispose() => Stop();
}
