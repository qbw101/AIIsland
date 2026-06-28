using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Media;
using ClassIsland.AISmartClass.Attributes;
using ClassIsland.Core.Attributes;
using FluentAvalonia.UI.Controls;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 在插件初始化后，反射替换已注册组件的 ComponentInfo.IconSource，
/// 将兜底的 fluent 图标替换为自定义 AIIsland Icons 字体图标。
/// FontIconSource 使用 Avalonia 标准文本渲染，Foreground 变化时自动重绘，
/// 完美跟随主题切换（深色主题白色、浅色主题黑色）。
/// </summary>
public static class IconPatcher
{
    /// <summary>AIIsland Icons 字体族名（avares 资源引用）。</summary>
    private static readonly FontFamily IconFontFamily =
        new("avares://ClassIsland.AISmartClass/icon#AIIsland Icons");

    /// <summary>ComponentInfo.IconSource 自动属性的 backing field 名。</summary>
    private const string BackingFieldName = "<IconSource>k__BackingField";

    /// <summary>
    /// 遍历 ComponentRegistryService.Registered，找到带 [AIIslandIcon] 标记的组件，
    /// 反射替换其 IconSource 为自定义字体图标。
    /// 必须在 AddComponent 调用之后执行。
    /// </summary>
    public static void PatchAll()
    {
        try
        {
            // 通过反射获取 ComponentRegistryService.Registered 静态属性
            var registryType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => SafeGetTypes(a))
                .FirstOrDefault(t => t.FullName == "ClassIsland.Core.Extensions.Registry.ComponentRegistryService"
                                  || t.FullName == "ClassIsland.Core.ComponentRegistryService");

            if (registryType == null)
            {
                // 尝试从 ClassIsland.Core 程序集直接获取
                var coreAsm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "ClassIsland.Core");
                registryType = coreAsm?.GetTypes()
                    .FirstOrDefault(t => t.Name == "ComponentRegistryService");
            }

            if (registryType == null)
            {
                Logger.Info("[IconPatcher] 未找到 ComponentRegistryService 类型，跳过图标替换");
                return;
            }

            var registeredProp = registryType.GetProperty("Registered",
                BindingFlags.Public | BindingFlags.Static);
            if (registeredProp == null)
            {
                Logger.Info("[IconPatcher] 未找到 Registered 属性，跳过图标替换");
                return;
            }

            var registered = registeredProp.GetValue(null) as System.Collections.IEnumerable;
            if (registered == null)
            {
                Logger.Info("[IconPatcher] Registered 集合为 null，跳过图标替换");
                return;
            }

            // 获取 ComponentInfo.IconSource 的 backing field
            var componentInfoType = typeof(ComponentInfo);
            var backingField = componentInfoType.GetField(BackingFieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (backingField == null)
            {
                Logger.Info("[IconPatcher] 未找到 IconSource backing field，跳过图标替换");
                return;
            }

            // 构建 GUID → glyph 映射（从所有带 [AIIslandIcon] 的组件类型中提取）
            var glyphMap = BuildGlyphMap();
            if (glyphMap.Count == 0)
            {
                Logger.Info("[IconPatcher] 未找到任何 [AIIslandIcon] 标记，跳过图标替换");
                return;
            }

            int patched = 0;
            foreach (var item in registered)
            {
                if (item is not ComponentInfo info) continue;

                if (!glyphMap.TryGetValue(info.Guid, out var glyph)) continue;

                var fontIcon = new FontIconSource
                {
                    FontFamily = IconFontFamily,
                    Glyph = glyph
                };

                backingField.SetValue(info, fontIcon);
                patched++;
                Logger.Info($"[IconPatcher] 已替换组件 {info.Name} ({info.Guid}) 的图标为自定义字体图标");
            }

            Logger.Info($"[IconPatcher] 图标替换完成，共替换 {patched} 个组件图标");
        }
        catch (Exception ex)
        {
            Logger.Info($"[IconPatcher] 图标替换失败: {ex}");
        }
    }

    /// <summary>
    /// 扫描当前插件程序集中所有带 [AIIslandIcon] 特性的组件类型，
    /// 从其 [ComponentInfo] 读取 GUID，构建 GUID → glyph 映射。
    /// </summary>
    private static Dictionary<Guid, string> BuildGlyphMap()
    {
        var map = new Dictionary<Guid, string>();
        var pluginAsm = typeof(IconPatcher).Assembly;

        foreach (var type in SafeGetTypes(pluginAsm))
        {
            var iconAttr = type.GetCustomAttribute<AIIslandIconAttribute>();
            if (iconAttr == null) continue;

            var infoAttr = type.GetCustomAttribute<ComponentInfo>();
            if (infoAttr == null) continue;

            map[infoAttr.Guid] = iconAttr.Glyph;
        }

        return map;
    }

    /// <summary>安全获取程序集类型，避免加载异常导致崩溃。</summary>
    private static IEnumerable<Type> SafeGetTypes(System.Reflection.Assembly asm)
    {
        try
        {
            return asm.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null)!;
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }
}
