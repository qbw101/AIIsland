using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared.Enums;

namespace ClassIsland.AISmartClass.Services.Automation;

/// <summary>
/// 规则触发时传递给条件与动作的上下文，封装当前课表状态。
/// </summary>
public class RuleContext
{
    public ILessonsService LessonsService { get; init; } = null!;

    public TimeState CurrentState { get; init; }

    public DateTime TriggeredAt { get; init; } = DateTime.Now;

    public string? CurrentSubjectName => LessonsService.CurrentSubject?.Name;

    public string? NextSubjectName => LessonsService.NextClassSubject?.Name;
}
