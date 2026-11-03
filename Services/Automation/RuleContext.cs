using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared.Enums;

namespace ClassIsland.AISmartClass.Services.Automation;

/// <summary>
/// 规则触发时传递给条件与动作的上下文。
/// 课时状态与科目来自统一业务快照（跟随所选时间来源），
/// <see cref="TriggeredAt"/> 保留真实发生时间用于记录与排查。
/// </summary>
public class RuleContext
{
    public ILessonsService LessonsService { get; init; } = null!;

    /// <summary>触发时的业务课时快照。</summary>
    public LessonSnapshot Snapshot { get; init; } = null!;

    /// <summary>真实触发时间（实际发生记录，不使用业务时间）。</summary>
    public DateTime TriggeredAt { get; init; } = DateTime.Now;

    public TimeState CurrentState => Snapshot?.ToTimeState() ?? LessonsService.CurrentState;

    // 快照存在时以其为准：放学/无课的空科目是正确结果，不能再回退到宿主当前科目。
    public string? CurrentSubjectName => Snapshot is null
        ? LessonsService.CurrentSubject?.Name
        : NullIfEmpty(Snapshot.CurrentSubjectName);

    public string? NextSubjectName => Snapshot is null
        ? LessonsService.NextClassSubject?.Name
        : NullIfEmpty(Snapshot.NextSubjectName);

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
