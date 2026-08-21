using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 从 ClassIsland 组件系统提取数据的服务。
/// 允许 AIIsland 读取其他组件（包括其他插件的组件）的配置和运行时数据。
/// </summary>
public sealed class ComponentDataAccessService
{
    private static Type? _componentRegistryType;
    private static PropertyInfo? _registeredProperty;
    private static Type? _componentInfoType;
    private static Type? _componentSettingsType;

    /// <summary>
    /// 初始化反射类型缓存。首次调用时自动执行。
    /// </summary>
    private static void EnsureInitialized()
    {
        if (_componentRegistryType != null) return;

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        
        // 获取 ComponentRegistryService 类型
        _componentRegistryType = assemblies
            .SelectMany(a => GetTypesSafe(a))
            .FirstOrDefault(t => t.FullName == "ClassIsland.Core.Services.Registry.ComponentRegistryService"
                              || t.FullName == "ClassIsland.Core.Extensions.Registry.ComponentRegistryService"
                              || t.FullName == "ClassIsland.Core.ComponentRegistryService");

        if (_componentRegistryType == null)
            throw new InvalidOperationException("无法找到 ComponentRegistryService 类型");

        // 获取静态属性 Registered
        _registeredProperty = _componentRegistryType.GetProperty("Registered", BindingFlags.Public | BindingFlags.Static);
        if (_registeredProperty == null)
            throw new InvalidOperationException("无法找到 ComponentRegistryService.Registered 属性");

        // 获取 ComponentInfo 和 ComponentSettings 类型
        _componentInfoType = assemblies
            .SelectMany(a => GetTypesSafe(a))
            .FirstOrDefault(t => t.FullName == "ClassIsland.Core.Attributes.ComponentInfo");

        _componentSettingsType = assemblies
            .SelectMany(a => GetTypesSafe(a))
            .FirstOrDefault(t => t.FullName == "ClassIsland.Core.Models.Components.ComponentSettings");
    }

    private static IEnumerable<Type> GetTypesSafe(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }

    /// <summary>
    /// 获取所有已注册的组件元数据。
    /// </summary>
    /// <returns>组件信息字典，key 为 GUID，value 为组件名称和类型信息</returns>
    public static Dictionary<string, ComponentMetadata> GetAllRegisteredComponents()
    {
        EnsureInitialized();

        var registered = _registeredProperty!.GetValue(null) as IEnumerable;
        if (registered == null) return new Dictionary<string, ComponentMetadata>();

        var result = new Dictionary<string, ComponentMetadata>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in registered)
        {
            if (item == null) continue;

            var guidProp = item.GetType().GetProperty("Guid");
            var nameProp = item.GetType().GetProperty("Name");
            var settingsTypeProp = item.GetType().GetProperty("SettingsType");

            if (guidProp == null || nameProp == null) continue;

            var guid = guidProp.GetValue(item)?.ToString();
            var name = nameProp.GetValue(item)?.ToString();
            var settingsType = settingsTypeProp?.GetValue(item) as Type;

            if (string.IsNullOrEmpty(guid) || string.IsNullOrEmpty(name)) continue;

            result[guid] = new ComponentMetadata
            {
                Guid = guid,
                Name = name,
                SettingsType = settingsType
            };
        }

        return result;
    }

    /// <summary>
    /// 根据组件 GUID 或名称查找组件元数据。
    /// </summary>
    /// <param name="guidOrName">组件 GUID 或名称（模糊匹配）</param>
    /// <returns>找到的组件元数据，未找到返回 null</returns>
    public static ComponentMetadata? FindComponent(string guidOrName)
    {
        var all = GetAllRegisteredComponents();

        // 精确匹配 GUID
        if (all.TryGetValue(guidOrName, out var exact))
            return exact;

        // 模糊匹配名称
        return all.Values.FirstOrDefault(c => 
            c.Name.Contains(guidOrName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 从当前布局中获取指定组件的配置（Settings 对象）。
    /// </summary>
    /// <param name="componentGuid">组件 GUID</param>
    /// <returns>组件的 Settings 对象，未找到返回 null</returns>
    public static object? GetComponentSettings(string componentGuid)
    {
        var settings = FindCurrentComponentSettings(componentGuid);
        return settings?.GetType().GetProperty("Settings")?.GetValue(settings);
    }

    /// <summary>
    /// 从当前布局中查找指定组件的 ComponentSettings 对象。
    /// </summary>
    public static object? FindCurrentComponentSettings(string componentGuid)
    {
        var componentsService = IAppHost.TryGetService<IComponentsService>();
        var lines = componentsService?.CurrentComponents?.GetType().GetProperty("Lines")
            ?.GetValue(componentsService.CurrentComponents) as IEnumerable;
        if (lines == null) return null;

        foreach (var line in lines)
        {
            var children = line?.GetType().GetProperty("Children")?.GetValue(line) as IEnumerable;
            var result = FindInChildren(children, componentGuid);
            if (result != null) return result;
        }

        return null;
    }

    /// <summary>
    /// 获取指定当前布局组件的运行时实例。调用此方法可能触发组件构造和初始化。
    /// </summary>
    public static object? GetComponentInstance(string componentGuid)
    {
        var componentsService = IAppHost.TryGetService<IComponentsService>();
        var settings = FindCurrentComponentSettings(componentGuid);
        if (componentsService == null || settings == null) return null;

        var method = typeof(IComponentsService).GetMethod("GetComponent");
        return method?.Invoke(componentsService, new[] { settings, false });
    }

    private static object? FindInChildren(IEnumerable? children, string targetGuid)
    {
        if (children == null) return null;

        foreach (var child in children)
        {
            if (child == null) continue;

            var id = child.GetType().GetProperty("Id")?.GetValue(child)?.ToString();
            if (string.Equals(id, targetGuid, StringComparison.OrdinalIgnoreCase)) return child;

            var nestedChildren = child.GetType().GetProperty("Children")?.GetValue(child) as IEnumerable;
            var nested = FindInChildren(nestedChildren, targetGuid);
            if (nested != null) return nested;
        }

        return null;
    }

    /// <summary>
    /// 组件元数据
    /// </summary>
    public class ComponentMetadata
    {
        public required string Guid { get; init; }
        public required string Name { get; init; }
        public Type? SettingsType { get; init; }
    }
}
