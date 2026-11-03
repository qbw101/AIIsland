using ClassIsland.Shared.Models.Profile;

namespace ClassIsland.AISmartClass.Services;

/// <summary>业务时钟下的课时阶段。</summary>
public enum LessonPhase
{
    /// <summary>课表服务或课表计划尚未就绪</summary>
    NoPlan,

    /// <summary>当天没有启用课程</summary>
    NoCourses,

    /// <summary>有计划但宿主尚未生成时间布局（启动早期）</summary>
    LayoutNotReady,

    /// <summary>第一节课开始前</summary>
    BeforeFirstClass,

    /// <summary>上课中</summary>
    OnClass,

    /// <summary>课间</summary>
    Breaking,

    /// <summary>最后一节课已结束</summary>
    AfterSchool
}

/// <summary>某一时刻的课时状态与剩余时间，供倒计时与通知组件直接渲染。</summary>
public sealed record LessonTimeState(
    LessonPhase Phase,
    ClassInfo? CurrentClass,
    ClassInfo? NextClass,
    TimeLayoutItem? Layout,
    TimeSpan Remaining,
    double ProgressPercent);

/// <summary>
/// 按单一业务时钟判断课时阶段的纯函数。
/// 阶段与剩余时间来自同一次 <c>now</c> 计算，不使用宿主事件锁定的状态，
/// 因此切换时间来源、时间回拨、跨天和启动未就绪都能在下一个 tick 自行恢复。
/// </summary>
public static class LessonClock
{
    /// <summary>按指定业务时间计算课时状态。</summary>
    /// <param name="plan">业务日期对应的课表计划。</param>
    /// <param name="businessNow">业务时间。</param>
    /// <param name="noCoursesWhenPlanMissing">
    /// 计划为空是否代表“宿主确认当天无课”。为 false 时视为服务尚未就绪。
    /// </param>
    public static LessonTimeState Evaluate(
        ClassPlan? plan,
        DateTime businessNow,
        bool noCoursesWhenPlanMissing = false)
    {
        if (plan == null)
        {
            var phase = noCoursesWhenPlanMissing ? LessonPhase.NoCourses : LessonPhase.NoPlan;
            return new LessonTimeState(phase, null, null, null, TimeSpan.Zero, 0);
        }

        if (!LessonTimeline.HasEnabledClasses(plan))
        {
            return new LessonTimeState(LessonPhase.NoCourses, null, null, null, TimeSpan.Zero, 0);
        }

        var scheduled = LessonTimeline.Build(plan);
        if (scheduled.Count == 0)
        {
            return new LessonTimeState(LessonPhase.LayoutNotReady, null, null, null, TimeSpan.Zero, 0);
        }

        var now = businessNow.TimeOfDay;

        for (var i = 0; i < scheduled.Count; i++)
        {
            var current = scheduled[i];
            if (now < current.Layout.StartTime || now >= current.Layout.EndTime)
            {
                continue;
            }

            var next = i + 1 < scheduled.Count ? scheduled[i + 1] : null;
            return CreateOnClassState(current, next, now);
        }

        for (var i = 0; i < scheduled.Count - 1; i++)
        {
            var previous = scheduled[i];
            var next = scheduled[i + 1];
            if (now < previous.Layout.EndTime || now >= next.Layout.StartTime)
            {
                continue;
            }

            var remaining = next.Layout.StartTime - now;
            return new LessonTimeState(
                LessonPhase.Breaking,
                previous.Class,
                next.Class,
                new TimeLayoutItem { StartTime = previous.Layout.EndTime, EndTime = next.Layout.StartTime },
                remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining,
                100);
        }

        var first = scheduled[0];
        if (now < first.Layout.StartTime)
        {
            return new LessonTimeState(
                LessonPhase.BeforeFirstClass,
                null,
                first.Class,
                first.Layout,
                first.Layout.StartTime - now,
                0);
        }

        return new LessonTimeState(LessonPhase.AfterSchool, null, null, null, TimeSpan.Zero, 100);
    }

    /// <summary>把剩余时间格式化为 <c>MM:SS</c>（分钟可超过两位）。</summary>
    public static string FormatCountdown(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        var totalSeconds = (long)Math.Floor(remaining.TotalSeconds);
        return $"{totalSeconds / 60:D2}:{totalSeconds % 60:D2}";
    }

    private static LessonTimeState CreateOnClassState(ScheduledClass current, ScheduledClass? next, TimeSpan now)
    {
        var total = (current.Layout.EndTime - current.Layout.StartTime).TotalSeconds;
        var remaining = current.Layout.EndTime - now;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        var progress = total > 0
            ? Math.Clamp(((now - current.Layout.StartTime).TotalSeconds / total) * 100, 0, 100)
            : 0;

        return new LessonTimeState(
            LessonPhase.OnClass,
            current.Class,
            next?.Class,
            current.Layout,
            remaining,
            progress);
    }
}
