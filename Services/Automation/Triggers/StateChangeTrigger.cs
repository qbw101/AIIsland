using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared.Enums;

namespace ClassIsland.AISmartClass.Services.Automation.Triggers;

/// <summary>
/// 状态变化触发器：订阅课表事件，当 CurrentState 发生切换时触发一次。
/// 复用 SmartClassNotifier 已验证的事件订阅模式。
/// </summary>
public class StateChangeTrigger : IRuleTrigger
{
    public event Action<RuleContext>? Triggered;

    private readonly ILessonsService _lessons;
    private TimeState _lastState;

    public StateChangeTrigger(ILessonsService lessons)
    {
        _lessons = lessons;
        _lastState = lessons.CurrentState;
    }

    public void Start()
    {
        _lessons.OnBreakingTime += OnStateChanged;
        _lessons.OnAfterSchool += OnStateChanged;
        _lessons.OnClass += OnStateChanged;
        _lessons.PostMainTimerTicked += OnTick;
    }

    public void Stop()
    {
        _lessons.OnBreakingTime -= OnStateChanged;
        _lessons.OnAfterSchool -= OnStateChanged;
        _lessons.OnClass -= OnStateChanged;
        _lessons.PostMainTimerTicked -= OnTick;
    }

    private void OnStateChanged(object? sender, EventArgs e) => Fire();

    private void OnTick(object? sender, EventArgs e) => Fire();

    private void Fire()
    {
        var state = _lessons.CurrentState;
        if (state == _lastState)
        {
            return;
        }

        _lastState = state;
        var ctx = new RuleContext
        {
            LessonsService = _lessons,
            CurrentState = state
        };
        Triggered?.Invoke(ctx);
    }
}
