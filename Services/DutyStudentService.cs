using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Services.Registry;
using ClassIsland.Shared.Helpers;
using ClassIsland.Shared;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 值日生数据读取服务，支持读取多个值日插件的数据
/// </summary>
public class DutyStudentService
{
    /// <summary>
    /// 插件配置根目录（支持便携模式和标准模式）
    /// </summary>
    private static string PluginConfigPath => Path.Combine(CommonDirectories.AppConfigPath, "Plugins");

    private const string DutyListComponentGuid = "68F4A3B2-C1D5-4E7A-9F0B-2A6E3D8C1B45";
    private const string ExtraIslandComponentGuid = "B977ECCC-1A59-4C71-A4EB-67780E16E926";
    private const string DutyIslandComponentGuid = "00318064-DACC-419F-8228-79F3413CAB54";

    /// <summary>
    /// 获取当前值日生信息。优先读取当前布局中已启用的值日组件，
    /// 再回退到插件公开服务或插件自身配置，兼容组件未放入布局的情况。
    /// </summary>
    public static DutyStudentInfo? GetCurrentDutyStudents(IReadOnlySet<string>? allowedPluginIds = null)
    {
        if (allowedPluginIds != null && allowedPluginIds.Count == 0) return null;
        // 组件读取是首选：组件 GUID 与组件注册中心保持一致。
        var componentData = TryReadFromRegisteredComponents(allowedPluginIds);
        if (componentData != null) return componentData;

        // 组件未放入当前布局时，使用公开服务或配置作为兼容回退。
        var dutyListData = CanRead(allowedPluginIds, PluginIntegrationService.DutyListId)
            ? TryReadDutyList() : null;
        if (dutyListData != null)
        {
            Logger.Info($"[DutyStudent] 从 DutyList 读取到 {dutyListData.Students.Count} 名值日生");
            return dutyListData;
        }

        // 2. 尝试读取 ExtraIsland (LiPolymer)
        var extraIslandData = CanRead(allowedPluginIds, PluginIntegrationService.ExtraIslandId)
            ? TryReadExtraIsland() : null;
        if (extraIslandData != null)
        {
            Logger.Info($"[DutyStudent] 从 ExtraIsland 读取到 {extraIslandData.Students.Count} 名值日生");
            return extraIslandData;
        }

        // 3. 尝试读取 DutyIsland (lrsgzs)
        var dutyIslandData = CanRead(allowedPluginIds, PluginIntegrationService.DutyIslandId)
            ? TryReadDutyIsland() : null;
        if (dutyIslandData != null)
        {
            Logger.Info($"[DutyStudent] 从 DutyIsland 读取到 {dutyIslandData.Students.Count} 名值日生");
            return dutyIslandData;
        }

        Logger.Info("[DutyStudent] 未检测到已安装的值日插件或配置为空");
        return null;
    }

    /// <summary>
    /// 从当前布局中定位值日组件，再读取其运行时数据。
    /// 组件不存在时返回 null，后续逻辑才使用配置文件回退。
    /// </summary>
    private static DutyStudentInfo? TryReadFromRegisteredComponents(IReadOnlySet<string>? allowedPluginIds)
    {
        var registered = ComponentDataAccessService.GetAllRegisteredComponents();
        var results = new List<DutyStudentInfo>();

        if (CanRead(allowedPluginIds, PluginIntegrationService.DutyListId))
            TryAddComponentResult(results, registered, DutyListComponentGuid, "DutyList", TryReadDutyListComponent);
        if (CanRead(allowedPluginIds, PluginIntegrationService.ExtraIslandId))
            TryAddComponentResult(results, registered, ExtraIslandComponentGuid, "ExtraIsland", TryReadExtraIslandComponent);
        if (CanRead(allowedPluginIds, PluginIntegrationService.DutyIslandId))
            TryAddComponentResult(results, registered, DutyIslandComponentGuid, "DutyIsland", TryReadDutyIslandComponent);

        if (results.Count == 0) return null;
        if (results.Count == 1) return results[0];

        var sources = string.Join("、", results.Select(x => x.Source));
        var merged = new DutyStudentInfo
        {
            Source = sources,
            Students = results.SelectMany(x => x.Students).Distinct().ToList(),
            Projects = results.SelectMany(x => x.Projects).Distinct().ToList(),
            RawData = results
        };
        Logger.Info($"[DutyStudent] 已合并当前布局组件数据: {sources}，共 {merged.Students.Count} 名值日生");
        return merged;
    }

    private static bool CanRead(IReadOnlySet<string>? allowedPluginIds, string pluginId) =>
        allowedPluginIds == null || allowedPluginIds.Contains(pluginId);

    private static void TryAddComponentResult(
        List<DutyStudentInfo> results,
        IReadOnlyDictionary<string, ComponentDataAccessService.ComponentMetadata> registered,
        string componentGuid,
        string source,
        Func<DutyStudentInfo?> reader)
    {
        if (!registered.ContainsKey(componentGuid) ||
            ComponentDataAccessService.FindCurrentComponentSettings(componentGuid) == null) return;

        try
        {
            var data = reader();
            if (data == null)
            {
                Logger.Info($"[DutyStudent] {source} 组件已在当前布局，但未读取到人员");
                return;
            }

            results.Add(data);
            Logger.Info($"[DutyStudent] 从 {source} 组件读取到 {data.Students.Count} 名值日生");
        }
        catch (Exception ex)
        {
            // 单个插件不应阻断其余值日组件的读取。
            Logger.Info($"[DutyStudent] {source} 组件读取失败: {ex.Message}");
        }
    }

    private static DutyStudentInfo? TryReadDutyListComponent()
    {
        var pluginType = FindType("DutyListPlugin.Plugin");
        var config = pluginType?.GetProperty("Config", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        if (config == null) return null;

        var currentMethod = config.GetType().GetMethod("GetCurrentGroupAndDay", BindingFlags.Public | BindingFlags.Instance);
        if (currentMethod?.Invoke(config, null) is not ITuple current || current.Length < 2) return null;
        var group = current[0];
        var dayIndex = Convert.ToInt32(current[1]);
        if (group == null || dayIndex == 0) return null;

        var dayConfig = group.GetType().GetProperty("DayConfig")?.GetValue(group) as System.Collections.IDictionary;
        var timeSlots = dayConfig?[dayIndex] as System.Collections.IEnumerable;
        if (timeSlots == null) return null;

        var students = new List<string>();
        var projects = new List<string>();
        foreach (var slot in timeSlots)
        {
            var items = slot?.GetType().GetProperty("Items")?.GetValue(slot) as System.Collections.IEnumerable;
            if (items == null) continue;
            foreach (var item in items)
            {
                if (item == null) continue;
                AddNames(students, item, "Person1", "Person2", "Person3");
                AddValue(projects, item, "Project");
            }
        }

        return CreateInfo("DutyList", students, projects, config);
    }

    private static DutyStudentInfo? TryReadExtraIslandComponent()
    {
        var instance = ComponentDataAccessService.GetComponentInstance(ExtraIslandComponentGuid);
        var persisted = instance?.GetType().GetProperty("PersistedSettings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);
        var people = persisted?.GetType().GetProperty("PeoplesOnDuty")?.GetValue(persisted) as System.Collections.IEnumerable;
        if (people == null) return null;

        var students = new List<string>();
        foreach (var person in people) AddValue(students, person, "Name");
        return persisted == null ? null : CreateInfo("ExtraIsland", students, null, persisted);
    }

    private static DutyStudentInfo? TryReadDutyIslandComponent()
    {
        var settings = ComponentDataAccessService.GetComponentSettings(DutyIslandComponentGuid);
        var selectedJobValue = settings?.GetType().GetProperty("JobGuid")?.GetValue(settings);
        var selectedJob = selectedJobValue is Guid guid ? guid : (Guid?)null;

        // DutyIsland 运行时组件自身持有由其插件 DI 容器解析出的真实服务实例。
        // 优先从组件读取，避免跨插件 AssemblyLoadContext 中同名接口 Type 不一致，
        // 导致按反射 Type 查询 DI 时拿不到已注册的服务。
        var component = ComponentDataAccessService.GetComponentInstance(DutyIslandComponentGuid);
        if (component == null)
        {
            Logger.Info("[DutyStudent] DutyIsland 运行时组件实例为空");
        }
        else
        {
            Logger.Info($"[DutyStudent] DutyIsland 运行时组件类型: {component.GetType().FullName}");
        }

        var service = component?.GetType()
            .GetProperty(
                "DutyPlanService",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(component);

        if (service != null)
        {
            Logger.Info($"[DutyStudent] 已从 DutyIsland 运行时组件获取服务: {service.GetType().FullName}");
            return TryReadDutyIslandService(selectedJob, service);
        }

        Logger.Info("[DutyStudent] DutyIsland 运行时组件中未找到 DutyPlanService，尝试 DI 回退");
        return TryReadDutyIslandService(selectedJob, null);
    }

    private static Type? FindType(string fullName) => AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(GetTypesSafe)
        .FirstOrDefault(t => string.Equals(t.FullName, fullName, StringComparison.Ordinal));

    private static object? TryGetService(Type? serviceType)
    {
        if (serviceType == null) return null;

        // IAppHost.TryGetService<T>() 是静态接口成员，在当前运行时中通过反射调用时
        // 可能被视为非静态方法并抛出 "Non-static method requires a target"。
        // 直接从公开的 Host.Services 按 Type 取服务更稳定，也正好支持运行时发现的插件接口。
        return IAppHost.Host?.Services.GetService(serviceType);
    }

    private static IEnumerable<Type> GetTypesSafe(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null)!; }
        catch { return Array.Empty<Type>(); }
    }

    private static void AddNames(List<string> target, object source, params string[] properties)
    {
        foreach (var property in properties) AddValue(target, source, property);
    }

    private static void AddValue(List<string> target, object? source, string property)
    {
        var value = source?.GetType().GetProperty(property)?.GetValue(source)?.ToString();
        if (!string.IsNullOrWhiteSpace(value) && !target.Contains(value)) target.Add(value);
    }

    private static DutyStudentInfo? CreateInfo(string source, List<string> students, List<string>? projects, object rawData)
    {
        if (students.Count == 0) return null;
        return new DutyStudentInfo
        {
            Source = source,
            Students = students.Distinct().ToList(),
            Projects = projects?.Distinct().ToList() ?? new List<string>(),
            RawData = rawData
        };
    }

    /// <summary>
    /// 读取 DutyList 插件的值日生数据（组件未放入当前布局时的回退）
    /// </summary>
    private static DutyStudentInfo? TryReadDutyList()
    {
        try
        {
            var configPath = Path.Combine(PluginConfigPath, "dutylist.jimmyxiao", "duty.json");
            Logger.Info($"[DutyStudent] 尝试读取 DutyList: {configPath}");
            
            if (!File.Exists(configPath))
            {
                Logger.Info("[DutyStudent] DutyList 配置文件不存在");
                return null;
            }

            var json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<DutyListConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (config == null || config.Groups == null || config.Groups.Count == 0)
            {
                Logger.Info("[DutyStudent] DutyList 配置为空或无批次数据");
                return null;
            }

            // 计算当前批次和天数
            var (group, dayIndex) = GetCurrentGroupAndDay(config);
            if (group == null || dayIndex == 0) // dayIndex = 0 表示今天是跳过日
            {
                Logger.Info($"[DutyStudent] DutyList 今天无值日（跳过日或无匹配批次）");
                return null;
            }

            // 获取今天的值日配置
            if (!group.DayConfig.TryGetValue(dayIndex, out var timeSlots) || timeSlots == null || timeSlots.Count == 0)
            {
                Logger.Info($"[DutyStudent] DutyList 第 {dayIndex} 天无值日配置");
                return null;
            }

            // 汇总所有时间段的值日生
            var allStudents = new List<DutyItem>();
            foreach (var slot in timeSlots)
            {
                if (slot.Items != null)
                    allStudents.AddRange(slot.Items);
            }

            if (allStudents.Count == 0)
            {
                Logger.Info("[DutyStudent] DutyList 值日项为空");
                return null;
            }

            return new DutyStudentInfo
            {
                Source = "DutyList",
                Students = allStudents.SelectMany(item => new[]
                {
                    item.Person1,
                    item.Person2,
                    item.Person3
                }.Where(p => !string.IsNullOrWhiteSpace(p))).Distinct().ToList(),
                Projects = allStudents.Select(item => item.Project).Where(p => !string.IsNullOrWhiteSpace(p)).ToList(),
                RawData = allStudents
            };
        }
        catch (Exception ex)
        {
            Logger.Info($"[DutyStudent] DutyList 读取失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 计算当前批次和批次内天数（从 DutyList 移植的逻辑）
    /// </summary>
    private static (RotationGroup? Group, int DayIndex) GetCurrentGroupAndDay(DutyListConfig config)
    {
        if (config.Groups == null || config.Groups.Count == 0)
            return (null, 0);

        var today = DateTime.Today;
        var daysPassed = Math.Max(0, (int)(today - config.RotationStartDate).TotalDays);
        var batchNo = daysPassed / config.RotationPeriodDays;
        var idx = batchNo % config.Groups.Count;
        var group = config.Groups[idx];
        var batchStart = config.RotationStartDate.AddDays(batchNo * config.RotationPeriodDays);

        int dayIndex;
        if (group.SkipDays != null && group.SkipDays.Count > 0)
        {
            // 今天是跳过日 → 不显示值日
            if (group.SkipDays.Contains(today.DayOfWeek))
                return (group, 0);

            // 从批次起始日到今天（含），累计非跳过天数
            int counted = 0;
            for (var d = batchStart; d <= today; d = d.AddDays(1))
            {
                if (!group.SkipDays.Contains(d.DayOfWeek))
                    counted++;
            }
            dayIndex = Math.Max(1, counted);
        }
        else
        {
            dayIndex = (daysPassed % config.RotationPeriodDays) + 1;
        }

        return (group, dayIndex);
    }

    /// <summary>
    /// 读取 ExtraIsland 插件的值日生数据
    /// </summary>
    private static DutyStudentInfo? TryReadExtraIsland()
    {
        try
        {
            var configPath = new[]
                {
                    Path.Combine(PluginConfigPath, "ink.lipoly.ext.extraisland", "Persisted", "OnDuty.json"),
                    Path.Combine(PluginConfigPath, "ExtraIsland", "Persisted", "OnDuty.json")
                }
                .FirstOrDefault(File.Exists);
            Logger.Info($"[DutyStudent] 尝试读取 ExtraIsland: {configPath ?? "未找到"}");

            if (configPath == null)
            {
                Logger.Info("[DutyStudent] ExtraIsland 配置文件不存在");
                return null;
            }

            var json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<ExtraIslandOnDutyConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (config == null || config.Peoples == null || config.Peoples.Count == 0)
            {
                Logger.Info("[DutyStudent] ExtraIsland 配置为空或无人员数据");
                return null;
            }

            // 获取当前值日生
            var currentStudents = GetExtraIslandCurrentDuty(config);
            if (currentStudents == null || currentStudents.Count == 0)
            {
                Logger.Info("[DutyStudent] ExtraIsland 当前无值日生");
                return null;
            }

            return new DutyStudentInfo
            {
                Source = "ExtraIsland",
                Students = currentStudents.Select(p => p.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList(),
                Projects = new List<string>(), // ExtraIsland 不区分项目
                RawData = currentStudents
            };
        }
        catch (Exception ex)
        {
            Logger.Info($"[DutyStudent] ExtraIsland 读取失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 获取 ExtraIsland 当前值日生（移植自 OnDutyPersistedConfigData.GetWhoOnDuty）
    /// </summary>
    private static List<ExtraIslandPeopleItem> GetExtraIslandCurrentDuty(ExtraIslandOnDutyConfig config)
    {
        if (config.DutyState == "Grouped")
        {
            // N人值日模式
            var result = new List<ExtraIslandPeopleItem>();
            for (int k = 0; k < config.NumberOfPeoples; k++)
            {
                var person = GetExtraIslandPersonByIndex(config, config.CurrentPeopleIndex + k);
                if (person != null)
                    result.Add(person);
            }
            return result;
        }
        else if (config.DutyState == "InOut")
        {
            // 内/外双人轮换模式
            bool isOdd = config.CurrentPeopleIndex % 2 == 1;
            if (isOdd)
            {
                return new List<ExtraIslandPeopleItem>
                {
                    GetExtraIslandPersonByIndex(config, config.CurrentPeopleIndex) ?? new ExtraIslandPeopleItem(),
                    GetExtraIslandPersonByIndex(config, config.CurrentPeopleIndex - 1) ?? new ExtraIslandPeopleItem()
                };
            }
            else
            {
                return new List<ExtraIslandPeopleItem>
                {
                    GetExtraIslandPersonByIndex(config, config.CurrentPeopleIndex) ?? new ExtraIslandPeopleItem(),
                    GetExtraIslandPersonByIndex(config, config.CurrentPeopleIndex + 1) ?? new ExtraIslandPeopleItem()
                };
            }
        }

        return new List<ExtraIslandPeopleItem>();
    }

    private static ExtraIslandPeopleItem? GetExtraIslandPersonByIndex(ExtraIslandOnDutyConfig config, int index)
    {
        return config.Peoples?.FirstOrDefault(p => p.Index == index);
    }

    /// <summary>
    /// 读取 DutyIsland 公共服务。若当前布局包含 DutyIsland 组件，则只读取该组件选中的任务；
    /// 否则汇总当前值日表全部任务的人员。
    /// </summary>
    private static DutyStudentInfo? TryReadDutyIsland()
    {
        return TryReadDutyIslandService(null, null);
    }

    private static DutyStudentInfo? TryReadDutyIslandService(Guid? selectedJob, object? preferredService)
    {
        try
        {
            Logger.Info($"[DutyStudent] 尝试读取 DutyIsland 服务，任务: {selectedJob?.ToString() ?? "全部"}");

            var service = preferredService;
            if (service == null)
            {
                // DutyIsland 实际只注册 IDutyPlanService；该接口继承 IPublicDutyPlanService。
                // 直接按 IPublicDutyPlanService 获取会得到 null。
                var serviceType = FindType("DutyIsland.Interface.Services.IDutyPlanService")
                                  ?? FindType("DutyIsland.Interface.Services.IPublicDutyPlanService");
                Logger.Info($"[DutyStudent] DutyIsland 服务接口类型: {serviceType?.AssemblyQualifiedName ?? "未找到"}");
                service = TryGetService(serviceType);
                Logger.Info(service == null
                    ? "[DutyStudent] DutyIsland DI 服务实例为空"
                    : $"[DutyStudent] 已通过 DI 获取 DutyIsland 服务: {service.GetType().FullName}");
            }

            if (service == null)
            {
                Logger.Info("[DutyStudent] DutyIsland 服务实例不可用");
                return null;
            }

            // 使用实际服务实现类型读取属性，避免接口 Type 来自不同加载上下文。
            var plan = service.GetType()
                .GetProperty(
                    "CurrentDutyPlan",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(service);
            Logger.Info(plan == null
                ? "[DutyStudent] DutyIsland CurrentDutyPlan 为空"
                : $"[DutyStudent] 已获取 DutyIsland CurrentDutyPlan: {plan.GetType().FullName}");
            if (plan == null)
            {
                return null;
            }

            var dictionary = plan.GetType().GetProperty("WorkerDictionary")?.GetValue(plan) as System.Collections.IEnumerable;
            if (dictionary == null)
            {
                Logger.Info("[DutyStudent] DutyIsland WorkerDictionary 不可枚举");
                return null;
            }

            var students = new List<string>();
            foreach (var pair in dictionary)
            {
                if (pair == null) continue;
                var key = pair.GetType().GetProperty("Key")?.GetValue(pair);
                var value = pair.GetType().GetProperty("Value")?.GetValue(pair);
                if (selectedJob is Guid selected && selected != Guid.Empty && !Equals(key, selected)) continue;

                var workers = value?.GetType().GetProperty("Workers")?.GetValue(value) as System.Collections.IEnumerable;
                if (workers == null) continue;
                foreach (var worker in workers) AddValue(students, worker, "Name");
                if (students.Count > 0 && selectedJob is Guid selectedGuid && selectedGuid != Guid.Empty) break;
            }

            return CreateInfo("DutyIsland", students, null, plan);
        }
        catch (Exception ex)
        {
            Logger.Info($"[DutyStudent] DutyIsland 读取失败: {ex.Message}");
            return null;
        }
    }
}

#region 数据模型

/// <summary>
/// 值日生信息统一输出格式
/// </summary>
public class DutyStudentInfo
{
    /// <summary>数据来源插件名称</summary>
    public string Source { get; set; } = "";

    /// <summary>值日生姓名列表</summary>
    public List<string> Students { get; set; } = new();

    /// <summary>值日项目列表</summary>
    public List<string> Projects { get; set; } = new();

    /// <summary>原始数据（用于调试）</summary>
    public object? RawData { get; set; }

    /// <summary>
    /// 生成友好的文本描述
    /// </summary>
    public string ToFriendlyString()
    {
        if (Students.Count == 0)
            return "今天没有值日生";

        var studentText = string.Join("、", Students);
        
        if (Projects.Count > 0)
        {
            var projectText = string.Join("、", Projects);
            return $"今天的值日生是：{studentText}。值日项目：{projectText}";
        }

        return $"今天的值日生是：{studentText}";
    }
}

#endregion

#region DutyList 数据模型

public class DutyListConfig
{
    public List<RotationGroup> Groups { get; set; } = new();
    public DateTime RotationStartDate { get; set; } = DateTime.Today;
    public int RotationPeriodDays { get; set; } = 7;
}

public class RotationGroup
{
    public string Name { get; set; } = "";
    public bool EnableReminder { get; set; } = true;
    public List<DayOfWeek> SkipDays { get; set; } = new();
    public Dictionary<int, List<DutyTimeSlot>> DayConfig { get; set; } = new();
}

public class DutyTimeSlot
{
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
    public List<DutyItem> Items { get; set; } = new();
}

public class DutyItem
{
    public string Project { get; set; } = "";
    public string Person1 { get; set; } = "";
    public string Person2 { get; set; } = "";
    public string Person3 { get; set; } = "";
    public string Color { get; set; } = "#00BFFF";
}

#endregion

#region ExtraIsland 数据模型

public class ExtraIslandOnDutyConfig
{
    public List<ExtraIslandPeopleItem> Peoples { get; set; } = new();
    public DateTime LastUpdate { get; set; } = DateTime.Today;
    public int CurrentPeopleIndex { get; set; }
    public bool IsCycled { get; set; } = true;
    public int NumberOfPeoples { get; set; } = 1;
    public string DutyState { get; set; } = "Grouped"; // "Grouped" 或 "InOut"
}

public class ExtraIslandPeopleItem
{
    public string Name { get; set; } = "";
    public int Index { get; set; }
}

#endregion
