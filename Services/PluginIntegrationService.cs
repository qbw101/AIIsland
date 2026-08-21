using System;
using System.Collections.Generic;
using System.Linq;

namespace ClassIsland.AISmartClass.Services;

/// <summary>Describes external plugins whose data AIIsland can read.</summary>
public static class PluginIntegrationService
{
    public const string BirthdayIslandId = "PikaBoo0.birthdayisland";
    public const string DutyListId = "dutylist.jimmyxiao";
    public const string DutyIslandId = "lrs2187.duty";
    public const string ExtraIslandId = "ink.lipoly.ext.extraisland";

    private static readonly PluginDefinition[] Definitions =
    {
        new(BirthdayIslandId, "BirthdayIsland（生日显示插件）",
            "读取今日生日名单，用于智能每日简报中的生日祝福", "birthday",
            new[] { "BirthdayIsland.Services.BirthdayDataService" }),
        new(DutyListId, "值日生名单",
            "读取今日值日生名单，用于每日简报和放学总结", "duty",
            new[] { "DutyListPlugin.Plugin" }),
        new(DutyIslandId, "DutyIsland",
            "读取当前值日计划，用于每日简报和放学总结", "duty",
            new[]
            {
                "DutyIsland.Interface.Services.IDutyPlanService",
                "DutyIsland.Interface.Services.IPublicDutyPlanService"
            }),
        new(ExtraIslandId, "ExtraIsland",
            "读取值日组件中的人员信息，用于每日简报和放学总结", "duty",
            new[] { "ExtraIsland.Components.OnDuty", "ExtraIsland.Plugin" })
    };

    public sealed class DetectablePlugin
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public string Category { get; init; } = "";
        public bool IsInstalled { get; init; }
        public string? DetectedVersion { get; init; }
    }

    private sealed record PluginDefinition(
        string Id,
        string Name,
        string Description,
        string Category,
        IReadOnlyList<string> TypeNames);

    public static List<DetectablePlugin> DetectAvailablePlugins()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        return Definitions.Select(definition =>
        {
            var detectedType = definition.TypeNames
                .SelectMany(typeName => assemblies.Select(assembly =>
                {
                    try { return assembly.GetType(typeName, throwOnError: false); }
                    catch { return null; }
                }))
                .FirstOrDefault(type => type != null);
            return new DetectablePlugin
            {
                Id = definition.Id,
                Name = definition.Name,
                Description = definition.Description,
                Category = definition.Category,
                IsInstalled = detectedType != null,
                DetectedVersion = detectedType?.Assembly.GetName().Version?.ToString()
            };
        }).ToList();
    }

    public static List<DetectablePlugin> GetInstalledPlugins() =>
        DetectAvailablePlugins().Where(plugin => plugin.IsInstalled).ToList();

    public static bool HasAnyIntegratablePlugins() => GetInstalledPlugins().Count > 0;

    public static bool IsAuthorized(Models.SmartClassNotifierSettings settings, string pluginId) =>
        settings.EnableExternalPluginIntegration &&
        settings.PluginAuthorizationConfirmed &&
        settings.AuthorizedPluginIds?.Contains(pluginId) == true;

    public static HashSet<string> GetAuthorizedDutyPluginIds(Models.SmartClassNotifierSettings settings)
    {
        if (!settings.EnableExternalPluginIntegration || !settings.PluginAuthorizationConfirmed)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var dutyIds = new HashSet<string>(new[] { DutyListId, DutyIslandId, ExtraIslandId },
            StringComparer.OrdinalIgnoreCase);
        dutyIds.IntersectWith(settings.AuthorizedPluginIds ?? Enumerable.Empty<string>());
        return dutyIds;
    }

    public static HashSet<string> NormalizeAuthorizedPluginIds(IEnumerable<string>? pluginIds)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in pluginIds ?? Enumerable.Empty<string>())
        {
            normalized.Add(id switch
            {
                "BirthdayIsland" => BirthdayIslandId,
                "DutyList" => DutyListId,
                "DutyIsland" => DutyIslandId,
                "ExtraIsland" => ExtraIslandId,
                _ => id
            });
        }
        return normalized;
    }

}
