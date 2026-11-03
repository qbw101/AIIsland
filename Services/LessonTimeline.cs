using ClassIsland.Shared.Models.Profile;

namespace ClassIsland.AISmartClass.Services;

/// <summary>课程与其实际生效的时间点。</summary>
public sealed record ScheduledClass(ClassInfo Class, TimeLayoutItem Layout);

/// <summary>
/// 课时布局解析。
/// <see cref="ClassInfo.CurrentTimeLayoutItem"/> 依赖宿主为课表挂载的
/// <see cref="ClassInfo.CurrentTimeLayout"/>，跨日期查询到的非激活课表可能尚未挂载。
/// 这里先走宿主强类型字段，失败时按同一语义从 <see cref="ClassPlan.TimeLayout"/> 解析，
/// 避免把“确实有课”误判为布局未就绪，也不修改宿主状态。
/// </summary>
public static class LessonTimeline
{
    /// <summary>按开始时间排序的已启用课程列表。</summary>
    public static IReadOnlyList<ScheduledClass> Build(ClassPlan? plan)
    {
        if (plan == null)
        {
            return Array.Empty<ScheduledClass>();
        }

        var result = new List<ScheduledClass>();
        foreach (var classInfo in plan.Classes.Where(c => c.IsEnabled))
        {
            var layout = ResolveLayoutItem(plan, classInfo);
            if (layout != null)
            {
                result.Add(new ScheduledClass(classInfo, layout));
            }
        }

        return result.OrderBy(item => item.Layout.StartTime).ToList();
    }

    /// <summary>解析单节课的时间点，解析不到返回 null。</summary>
    public static TimeLayoutItem? ResolveLayoutItem(ClassPlan plan, ClassInfo classInfo)
    {
        try
        {
            var attached = classInfo.CurrentTimeLayoutItem;
            if (IsUsable(attached))
            {
                return attached;
            }
        }
        catch
        {
            // CurrentTimeLayout 未挂载时属性会抛异常，继续走计划自身的布局解析。
        }

        try
        {
            var layout = plan.TimeLayout;
            var items = layout?.Layouts.Where(item => item.TimeType == 0).ToList();
            if (items == null || classInfo.Index < 0 || classInfo.Index >= items.Count)
            {
                return null;
            }

            return IsUsable(items[classInfo.Index]) ? items[classInfo.Index] : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>计划中是否存在已启用课程（用于区分“无课”和“布局未就绪”）。</summary>
    public static bool HasEnabledClasses(ClassPlan plan)
        => plan.Classes.Any(c => c.IsEnabled);

    private static bool IsUsable(TimeLayoutItem? item)
    {
        if (item == null) return false;

        // ClassInfo 在索引越界时返回 TimeLayoutItem.Empty（全零），不能当作有效课时。
        return item.StartTime != TimeSpan.Zero || item.EndTime != TimeSpan.Zero || item.TimeType != 0;
    }
}
