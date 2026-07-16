using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared.Models.Profile;
using System.Reflection;

namespace ClassIsland.AISmartClass.Services;

/// <summary>ProfileService 就绪状态</summary>
public enum ProfileReadiness
{
    /// <summary>ProfileService 尚未初始化</summary>
    ServiceNotReady,
    /// <summary>当天没有已激活且启用的课表计划</summary>
    NoActivePlan,
    /// <summary>有计划但尚未生成时间布局（启动早期）</summary>
    LayoutNotReady,
    /// <summary>课表已就绪，并确认当天没有课程</summary>
    ReadyWithoutCourses,
    /// <summary>成功读取到课程</summary>
    Ready
}

/// <summary>等待课表后的科目查询结果，保留“未就绪”和“确认无课”的区别。</summary>
public sealed record TodaySubjectsResult(List<string> Subjects, ProfileReadiness Readiness);

/// <summary>学习提示所需的当前时段、重点科目和缓存标识。</summary>
public sealed record LearningHintContext(string Scene, string Focus, string CacheKey);

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
        return GetTodaySubjectNames(profileService, distinct: true);
    }

    /// <summary>
    /// 获取今日所有科目名称。
    /// 优先读取 ClassIsland 当天实际课表；如果主程序尚未生成 CurrentTimeLayoutItem，
    /// 再兜底读取已启用课程，避免组件刚加载或多个 AI 组件同时初始化时误判为空课表。
    /// </summary>
    public static List<string> GetTodaySubjectNames(IProfileService profileService, bool distinct)
    {
        var readiness = ProfileReadiness.ServiceNotReady;
        var result = TryGetTodaySubjectNames(profileService, distinct, out readiness);
        return result;
    }

    /// <summary>
    /// 非阻塞尝试获取今日科目名称，并返回当前就绪状态。
    /// </summary>
    private static List<string> TryGetTodaySubjectNames(IProfileService profileService, bool distinct, out ProfileReadiness readiness)
    {
        readiness = ProfileReadiness.ServiceNotReady;
        try
        {
            if (profileService?.Profile == null)
                return new List<string>();

            var activePlan = GetActivePlan(profileService);
            if (activePlan == null)
            {
                readiness = ProfileReadiness.NoActivePlan;
                return new List<string>();
            }

            var classes = activePlan.Classes
                .Where(c => c.IsEnabled)
                .OrderBy(c => c.CurrentTimeLayoutItem?.StartTime ?? TimeSpan.MaxValue)
                .ToList();

            if (classes.Count == 0)
            {
                readiness = ProfileReadiness.ReadyWithoutCourses;
                return new List<string>();
            }

            var todayClasses = classes
                .Where(c => c.CurrentTimeLayoutItem != null)
                .ToList();

            // CurrentTimeLayoutItem 在 ClassIsland 启动早期可能尚未填充，不能因此判定今天无课。
            if (todayClasses.Count == 0)
                todayClasses = classes;

            var names = new List<string>();
            foreach (var cls in todayClasses)
            {
                var name = GetSubjectName(profileService, cls.SubjectId);
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name.Trim());
            }

            readiness = names.Count > 0 ? ProfileReadiness.Ready : ProfileReadiness.LayoutNotReady;
            return distinct
                ? names.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : names;
        }
        catch (Exception ex)
        {
            Logger.Info($"获取今日课程失败: {ex.Message}");
            readiness = ProfileReadiness.ServiceNotReady;
            return new List<string>();
        }
    }

    /// <summary>
    /// 等待 ProfileService 就绪并获取今日科目名称。
    /// ClassIsland 启动早期组件可能先于 IHostedService 加载，ProfileService 为 null
    /// 或课表计划尚未激活，此时应轮询等待，而不是直接判定为空课表。
    /// </summary>
    /// <param name="getter">获取 ProfileService 的委托（通常为 () => Plugin.ProfileService）</param>
    /// <param name="distinct">是否去重</param>
    /// <param name="maxAttempts">最大轮询次数</param>
    /// <param name="delayMs">每次轮询间隔毫秒</param>
    public static async Task<List<string>> GetTodaySubjectNamesWhenReadyAsync(
        Func<IProfileService?> getter,
        bool distinct = true,
        int maxAttempts = 20,
        int delayMs = 500,
        CancellationToken ct = default)
    {
        var result = await GetTodaySubjectsWhenReadyAsync(
            getter, distinct, maxAttempts, delayMs, ct).ConfigureAwait(false);
        return result.Subjects;
    }

    /// <summary>
    /// 等待当天课表并保留最终就绪状态。只有 ReadyWithoutCourses 才能安全显示“今日无课”。
    /// </summary>
    public static async Task<TodaySubjectsResult> GetTodaySubjectsWhenReadyAsync(
        Func<IProfileService?> getter,
        bool distinct = true,
        int maxAttempts = 20,
        int delayMs = 500,
        CancellationToken ct = default)
    {
        var lastReadiness = ProfileReadiness.ServiceNotReady;
        var lastNames = new List<string>();

        for (int i = 0; i < maxAttempts; i++)
        {
            ct.ThrowIfCancellationRequested();
            var ps = getter();
            if (ps != null)
            {
                lastNames = TryGetTodaySubjectNames(ps, distinct, out lastReadiness);
                if (lastReadiness == ProfileReadiness.Ready)
                    return new TodaySubjectsResult(lastNames, lastReadiness);

                // 启动早期 ClassPlan.Classes 可能短暂为空，不能在第一次空集合时立即判定无课。
                // ReadyWithoutCourses 必须持续到等待窗口结束后，才视为“确认无课”。
            }

            if (i < maxAttempts - 1)
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
        }

        return new TodaySubjectsResult(lastNames, lastReadiness);
    }

    /// <summary>根据当前时间生成学习提示上下文，覆盖上课前、上课中、课间、放学和无课日。</summary>
    public static LearningHintContext GetLearningHintContext(
        IProfileService profileService,
        IReadOnlyList<string>? todaySubjects = null)
    {
        try
        {
            var plan = GetActivePlan(profileService);
            if (plan == null)
                return new LearningHintContext("今天没有课程", "自主学习", "no-plan");

            var now = DateTime.Now.TimeOfDay;
            var current = GetCurrentClass(plan, now);
            if (current != null)
            {
                var subject = GetSubjectName(profileService, current.SubjectId);
                if (string.IsNullOrWhiteSpace(subject)) subject = "当前课程";
                return new LearningHintContext("正在上课", subject, $"class:{current.SubjectId}");
            }

            var next = GetNextClass(plan, now);
            var enabled = plan.Classes
                .Where(c => c.IsEnabled && c.CurrentTimeLayoutItem != null)
                .OrderBy(c => c.CurrentTimeLayoutItem!.StartTime)
                .ToList();

            if (next != null)
            {
                var subject = GetSubjectName(profileService, next.SubjectId);
                if (string.IsNullOrWhiteSpace(subject)) subject = "下一节课";
                var hasPrevious = enabled.Any(c => c.CurrentTimeLayoutItem!.EndTime < now);
                var scene = hasPrevious ? "课间，下一节课前" : "上课前";
                return new LearningHintContext(scene, subject, $"next:{next.SubjectId}:{scene}");
            }

            var focus = todaySubjects != null && todaySubjects.Count > 0
                ? string.Join("、", todaySubjects)
                : "今日所学";
            return new LearningHintContext("今天课程已结束，已经放学", focus, "after-school");
        }
        catch (Exception ex)
        {
            Logger.Info($"获取学习提示上下文失败: {ex.Message}");
            return new LearningHintContext("当前没有正在进行的课程", "自主学习", "fallback");
        }
    }

    /// <summary>
    /// 等待 ProfileService 就绪，直到 Profile 已加载且当天存在已激活的课表计划。
    /// 供 CurrentHint 等只需要单次查询当前课程的组件使用。
    /// </summary>
    public static async Task<IProfileService?> WaitForProfileReadyAsync(
        Func<IProfileService?> getter,
        int maxAttempts = 20,
        int delayMs = 500,
        CancellationToken ct = default)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            ct.ThrowIfCancellationRequested();
            var ps = getter();
            var plan = ps == null ? null : GetActivePlan(ps);
            if (ps?.Profile != null && plan != null)
            {
                var enabledClasses = plan.Classes.Where(c => c.IsEnabled).ToList();
                if (enabledClasses.Any(c => c.CurrentTimeLayoutItem != null))
                    return ps;

                // 启动早期 Classes 可能暂时为空。只有等待窗口结束后仍为空，
                // 才把它作为“已就绪但无课程”交给调用方处理。
                if (enabledClasses.Count == 0 && i == maxAttempts - 1)
                    return ps;
            }

            if (i < maxAttempts - 1)
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
        }

        return null;
    }
}
