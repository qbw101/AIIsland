using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.AISmartClass.Services.Automation;

/// <summary>
/// 自动化规则：触发(When) + 条件列表(If) + 动作列表(Do)。
/// Phase 1 为内存模型（接口实例不可 JSON 序列化），后续 Phase 2 再做配置化与持久化。
/// </summary>
public class AutomationRule : ObservableObject
{
    private string _name = "未命名规则";
    private bool _isEnabled = true;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public IRuleTrigger? Trigger { get; set; }

    public List<IRuleCondition> Conditions { get; set; } = new();

    public List<IAction> Actions { get; set; } = new();
}
