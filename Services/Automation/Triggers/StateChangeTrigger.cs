using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared.Enums;

namespace ClassIsland.AISmartClass.Services.Automation.Triggers;

/// <summary>
/// 状态变化触发器：按统一业务时钟检测课时阶段切换。
/// 不使用宿主 OnClass/OnBreakingTime/OnAfterSchool 事件，
/// 因为时间偏移时宿主事件按真实时钟触发，会与其他业务状态不一致。
/// </summary>
public class StateChangeTrigger : IRuleTrigger
{
    public event Action<RuleContext>? Triggered;

    private readonly ILessonsService _lessons;
    private TimeState _lastState;
    private bool _initialized;

    public StateChangeTrigger(ILessonsService lessons)
    {
        _lessons = lessons;
    }

    public void Start()
    {
        _lessons.PostMainTimerTicked += OnTick;
    }

    public void Stop()
    {
        _lessons.PostMainTimerTicked -= OnTick;
    }

    private void OnTick(object? sender, EventArgs e) => Fire();

    private void Fire()
    {
        var snapshot = LessonSnapshotService.Capture();
        var state = snapshot.ToTimeState();

        if (!_initialized)
        {
            // 首次取样只建立基线，不补发状态变化。
            _initialized = true;
            _lastState = state;
            return;
        }

        if (state == _lastState)
        {
            return;
        }

        _lastState = state;
        var ctx = new RuleContext
        {
            LessonsService = _lessons,
            Snapshot = snapshot
        };
        Triggered?.Invoke(ctx);
    }
}
