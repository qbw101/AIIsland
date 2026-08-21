using ClassIsland.Shared.Models.Profile;

namespace ClassIsland.AISmartClass.Services;

/// <summary>贴心提醒的时间窗口判断，保持触发逻辑可测试且与通知 UI 解耦。</summary>
public static class ThoughtfulReminderTiming
{
    public static bool IsDueBeforeSchool(TimeSpan now, TimeSpan firstClassStart)
    {
        var due = firstClassStart - TimeSpan.FromMinutes(5);
        return now >= due && now <= due + TimeSpan.FromSeconds(90);
    }

    public static ClassInfo? GetFirstClass(ClassPlan plan)
    {
        return plan.Classes
            .Where(c => c.IsEnabled && c.CurrentTimeLayoutItem != null)
            .OrderBy(c => c.CurrentTimeLayoutItem!.StartTime)
            .FirstOrDefault();
    }

    public static ClassInfo? GetLastClass(ClassPlan plan)
    {
        return plan.Classes
            .Where(c => c.IsEnabled && c.CurrentTimeLayoutItem != null)
            .OrderByDescending(c => c.CurrentTimeLayoutItem!.EndTime)
            .FirstOrDefault();
    }
}
