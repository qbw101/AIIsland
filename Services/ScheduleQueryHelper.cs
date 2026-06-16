using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared.Models.Profile;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 课表时间查询工具。
/// 统一封装"根据当前时间找到当前课程/下一节课"的逻辑，
/// 供 SmartClassPanel 和 SmartClassNotifier 共享使用。
/// </summary>
public static class ScheduleQueryHelper
{
    /// <summary>
    /// 获取当前时刻正在进行的课程（上课状态）。
    /// 如果当前时间不在任何课程区间内，返回 null。
    /// </summary>
    public static ClassInfo? GetCurrentClass(ClassPlan plan, TimeSpan now)
    {
        if (plan == null) return null;

        foreach (var cls in plan.Classes)
        {
            if (!cls.IsEnabled) continue;
            var layout = cls.CurrentTimeLayoutItem;
            if (layout == null) continue;

            if (now >= layout.StartTime && now <= layout.EndTime)
                return cls;
        }

        return null;
    }

    /// <summary>
    /// 获取当前时刻所在的课间时段（上一节课结束 → 下一节课开始之间）。
    /// 返回上一节课的 ClassInfo；如果不在任何课间返回 null。
    /// </summary>
    public static ClassInfo? GetCurrentBreak(ClassPlan plan, TimeSpan now)
    {
        if (plan == null) return null;

        var classes = plan.Classes.Where(c => c.IsEnabled).ToList();

        for (int i = 0; i < classes.Count - 1; i++)
        {
            var prevLayout = classes[i].CurrentTimeLayoutItem;
            var nextLayout = classes[i + 1].CurrentTimeLayoutItem;
            if (prevLayout == null || nextLayout == null) continue;

            if (now > prevLayout.EndTime && now < nextLayout.StartTime)
                return classes[i]; // 上一节课的信息
        }

        return null;
    }

    /// <summary>
    /// 获取当前时刻即将开始的下一节课（课间时段）。
    /// </summary>
    public static ClassInfo? GetNextClass(ClassPlan plan, TimeSpan now)
    {
        if (plan == null) return null;

        foreach (var cls in plan.Classes)
        {
            if (!cls.IsEnabled) continue;
            var layout = cls.CurrentTimeLayoutItem;
            if (layout == null) continue;

            if (now < layout.StartTime)
                return cls;
        }

        return null;
    }

    /// <summary>
    /// 根据时间获取当前所在课时段（TimeLayoutItem），用于计算进度和倒计时。
    /// 上课中返回上课区间，课间返回课间区间。
    /// </summary>
    public static TimeLayoutItem? GetCurrentLayoutItem(IProfileService profileService)
    {
        try
        {
            var plan = GetActivePlan(profileService);
            if (plan == null) return null;

            var now = TimeSpan.FromTicks(DateTime.Now.TimeOfDay.Ticks);

            foreach (var cls in plan.Classes)
            {
                if (!cls.IsEnabled) continue;
                var layout = cls.CurrentTimeLayoutItem;
                if (layout == null) continue;

                // 上课区间
                if (now >= layout.StartTime && now <= layout.EndTime)
                    return layout;
            }

            // 课间区间：找相邻两节课之间的空隙
            var enabledClasses = plan.Classes.Where(c => c.IsEnabled).ToList();
            for (int i = 0; i < enabledClasses.Count - 1; i++)
            {
                var prevLayout = enabledClasses[i].CurrentTimeLayoutItem;
                var nextLayout = enabledClasses[i + 1].CurrentTimeLayoutItem;
                if (prevLayout == null || nextLayout == null) continue;

                if (now > prevLayout.EndTime && now < nextLayout.StartTime)
                {
                    // 返回一个"虚拟"的课间区间
                    return new TimeLayoutItem
                    {
                        StartTime = prevLayout.EndTime,
                        EndTime = nextLayout.StartTime
                    };
                }
            }

            return null;
        }
        catch { return null; }
    }

    /// <summary>
    /// 获取当前时间所在的课程（上课状态下），返回 ClassInfo 用于显示科目名称。
    /// </summary>
    public static ClassInfo? GetClassAtTime(ClassPlan plan, TimeSpan now)
    {
        return GetCurrentClass(plan, now);
    }

    /// <summary>获取当前激活的课表计划</summary>
    public static ClassPlan? GetActivePlan(IProfileService profileService)
    {
        try
        {
            var profile = profileService?.Profile;
            if (profile == null) return null;
            return profile.ClassPlans.Values.FirstOrDefault(p => p.IsActivated && p.IsEnabled);
        }
        catch { return null; }
    }

    /// <summary>根据 ClassInfo 获取科目名称</summary>
    public static string GetSubjectName(IProfileService profileService, Guid subjectId)
    {
        try
        {
            var profile = profileService?.Profile;
            if (profile == null) return "";
            return profile.Subjects.TryGetValue(subjectId, out var subject)
                ? subject.Name ?? ""
                : "";
        }
        catch { return ""; }
    }

    /// <summary>获取今日所有科目名称（去重），供各组件共用</summary>
    public static List<string> GetTodaySubjectNames(IProfileService profileService)
    {
        try
        {
            var profile = profileService?.Profile;
            if (profile == null) return new List<string>();

            var activePlan = profile.ClassPlans.Values.FirstOrDefault(p => p.IsActivated && p.IsEnabled);
            if (activePlan == null) return new List<string>();

            var names = new List<string>();
            foreach (var cls in activePlan.Classes.Where(c => c.IsEnabled))
            {
                if (profile.Subjects.TryGetValue(cls.SubjectId, out var subject) && !string.IsNullOrEmpty(subject.Name))
                    names.Add(subject.Name);
            }
            return names.Distinct().ToList();
        }
        catch { return new List<string>(); }
    }
}
