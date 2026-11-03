using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared.Enums;
using ClassIsland.Shared.Models.Profile;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 某一时刻统一业务课时快照：状态、当前/下一课程、布局与剩余时间。
/// 所有状态消费者（倒计时、通知、考试调度、考试页面、自动化）都应使用它，
/// 避免把宿主事件/宿主状态与业务时钟混用。
/// </summary>
public sealed record LessonSnapshot(
    DateTime Now,
    ClassPlan? Plan,
    LessonPhase Phase,
    ClassInfo? CurrentClass,
    ClassInfo? NextClass,
    TimeLayoutItem? CurrentLayout,
    TimeLayoutItem? NextLayout,
    string CurrentSubjectName,
    string NextSubjectName,
    TimeSpan Remaining,
    double ProgressPercent)
{
    public bool IsOnClass => Phase == LessonPhase.OnClass;

    public bool IsBreaking => Phase == LessonPhase.Breaking;

    /// <summary>当天课程已结束（含宿主确认今天无课）。</summary>
    public bool IsSchoolDayEnded => Phase is LessonPhase.AfterSchool or LessonPhase.NoCourses;

    /// <summary>映射为 ClassIsland 的 <see cref="TimeState"/>，供自动化条件与兼容字段使用。</summary>
    public TimeState ToTimeState() => Phase switch
    {
        LessonPhase.OnClass => TimeState.OnClass,
        LessonPhase.Breaking => TimeState.Breaking,
        LessonPhase.BeforeFirstClass => TimeState.PrepareOnClass,
        LessonPhase.AfterSchool => TimeState.AfterSchool,
        LessonPhase.NoCourses => TimeState.AfterSchool,
        _ => TimeState.None
    };
}

/// <summary>构造业务课时快照。</summary>
public static class LessonSnapshotService
{
    /// <summary>按业务时间取样。</summary>
    /// <param name="at">指定业务时间，默认取 <see cref="BusinessTime.Now"/>。</param>
    /// <param name="profileService">课表服务，默认取 <see cref="Plugin.ProfileService"/>。</param>
    public static LessonSnapshot Capture(DateTime? at = null, IProfileService? profileService = null)
    {
        var now = at ?? BusinessTime.Now;
        var profile = profileService ?? Plugin.ProfileService;
        var resolution = ScheduleQueryHelper.ResolvePlanForDate(profile, now);
        var state = LessonClock.Evaluate(resolution.Plan, now, resolution.HostAuthoritative);

        var currentName = state.CurrentClass == null
            ? ""
            : ScheduleQueryHelper.GetSubjectName(profile, state.CurrentClass.SubjectId);
        var nextName = state.NextClass == null
            ? ""
            : ScheduleQueryHelper.GetSubjectName(profile, state.NextClass.SubjectId);

        TimeLayoutItem? nextLayout = null;
        if (state.NextClass != null && resolution.Plan != null)
        {
            nextLayout = LessonTimeline.ResolveLayoutItem(resolution.Plan, state.NextClass);
        }

        return new LessonSnapshot(
            now,
            resolution.Plan,
            state.Phase,
            state.CurrentClass,
            state.NextClass,
            state.Layout,
            nextLayout,
            currentName,
            nextName,
            state.Remaining,
            state.ProgressPercent);
    }
}
