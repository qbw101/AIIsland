using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClassIsland.AISmartClass.Models;
using ClassIsland.AISmartClass.PublicApi;
using FluentAvalonia.UI.Controls;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 管理外部插件对 AIIsland API 的调用授权。
/// 默认策略：每次确认。用户在弹窗中选择"允许并记住"后切换为直接授权。
/// </summary>
public class AuthGuard
{
    private AISettings _settings;
    private readonly object _lock = new();

    /// <summary>授权记录或调用统计发生变化时触发。</summary>
    public event EventHandler? EntriesChanged;

    internal AuthGuard(AISettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// 更新内部引用的设置对象。当用户保存新设置时调用，
    /// 会将旧设置中的授权记录迁移到新设置对象。
    /// </summary>
    internal void UpdateSettings(AISettings newSettings)
    {
        lock (_lock)
        {
            // 将运行时积累的授权记录迁移到新设置对象
            if (_settings.PluginAuthEntries.Count > 0 &&
                newSettings.PluginAuthEntries.Count == 0)
            {
                newSettings.PluginAuthEntries = _settings.PluginAuthEntries;
            }
            _settings = newSettings;
        }
    }

    /// <summary>
    /// 检查调用方是否已被授权为直接调用模式。
    /// </summary>
    /// <param name="pluginId">调用方插件标识（程序集名称）。</param>
    /// <returns>已授权返回 true，需要弹窗确认返回 false。</returns>
    public bool IsTrusted(string pluginId)
    {
        lock (_lock)
        {
            return _settings.PluginAuthEntries.FirstOrDefault(
                e => e.PluginId == pluginId && e.AuthMode == AIIslandAuthMode.Trusted) != null;
        }
    }

    /// <summary>
    /// 确认调用方是否有权调用。未授权时弹出确认对话框。
    /// 若全局默认策略为 Trusted，则首次调用自动授权，不弹窗。
    /// </summary>
    /// <param name="pluginId">调用方插件标识。</param>
    /// <param name="pluginName">调用方插件显示名称。</param>
    /// <param name="method">被调用的 API 方法名。</param>
    /// <param name="description">调用方提供的用途描述。</param>
    /// <returns>用户允许返回 true，拒绝返回 false。</returns>
    public async Task<bool> ConfirmAsync(
        string pluginId,
        string pluginName,
        string method,
        string? description)
    {
        // 已授权插件直接放行
        if (IsTrusted(pluginId))
        {
            RecordCall(pluginId, pluginName, method);
            return true;
        }

        // 检查全局默认授权策略：若为 Trusted 模式，首次调用自动授权
        if (GetDefaultAuthMode() == AIIslandAuthMode.Trusted && !HasEntry(pluginId))
        {
            Trust(pluginId, pluginName);
            RecordCall(pluginId, pluginName, method);
            Logger.Info($"[AuthGuard] 插件 {pluginName} ({pluginId}) 按默认策略自动授权");
            return true;
        }

        // 未授权或每次确认模式 → 弹窗
        var allow = await ShowConfirmationDialog(pluginId, pluginName, method, description);
        if (!allow) return false;

        RecordCall(pluginId, pluginName, method);
        return true;
    }

    /// <summary>获取全局默认授权模式。</summary>
    private AIIslandAuthMode GetDefaultAuthMode()
    {
        lock (_lock)
        {
            return _settings.DefaultAuthMode == 1
                ? AIIslandAuthMode.Trusted
                : AIIslandAuthMode.PerCallConfirm;
        }
    }

    /// <summary>检查是否已有该插件的授权记录。</summary>
    private bool HasEntry(string pluginId)
    {
        lock (_lock)
        {
            return _settings.PluginAuthEntries.Any(e => e.PluginId == pluginId);
        }
    }

    /// <summary>
    /// 将指定插件标记为已授权（直接调用模式）。
    /// </summary>
    public void Trust(string pluginId, string pluginName)
    {
        lock (_lock)
        {
            var entry = _settings.PluginAuthEntries.FirstOrDefault(e => e.PluginId == pluginId);
            if (entry == null)
            {
                entry = new AIIslandPluginAuthEntry
                {
                    PluginId = pluginId,
                    PluginName = pluginName
                };
                _settings.PluginAuthEntries.Add(entry);
            }
            else
            {
                entry.PluginName = pluginName;
            }

            entry.AuthMode = AIIslandAuthMode.Trusted;
            entry.AuthorizedAt = DateTime.Now;
        }

        OnEntriesChanged();
    }

    /// <summary>
    /// 将指定插件切换回每次确认模式。
    /// </summary>
    public void RevokeTrust(string pluginId)
    {
        var changed = false;
        lock (_lock)
        {
            var entry = _settings.PluginAuthEntries.FirstOrDefault(e => e.PluginId == pluginId);
            if (entry != null)
            {
                entry.AuthMode = AIIslandAuthMode.PerCallConfirm;
                entry.AuthorizedAt = null;
                changed = true;
            }
        }

        if (changed)
            OnEntriesChanged();
    }

    /// <summary>
    /// 完全移除某个插件的授权记录。
    /// </summary>
    public void RemoveEntry(string pluginId)
    {
        var changed = false;
        lock (_lock)
        {
            changed = _settings.PluginAuthEntries.RemoveAll(e => e.PluginId == pluginId) > 0;
        }


        if (changed)
            OnEntriesChanged();
    }

    /// <summary>
    /// 尝试从调用栈推断调用方插件标识。
    /// </summary>
    internal static (string pluginId, string pluginName) IdentifyCaller()
    {
        // 跳过当前程序集和 async 基础设施帧，找到第一个外部调用方
        var assemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var frames = new StackTrace().GetFrames();
        if (frames != null)
        {
            foreach (var frame in frames)
            {
                var asm = frame.GetMethod()?.DeclaringType?.Assembly;
                if (asm == null) continue;
                var name = asm.GetName().Name ?? "";
                if (string.IsNullOrEmpty(name)) continue;

                // 跳过 AIIsland 自身、System、Microsoft、Avalonia 等
                if (name.StartsWith("ClassIsland.AISmartClass", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("FluentAvalonia", StringComparison.OrdinalIgnoreCase))
                    continue;

                assemblies.Add(name);
                if (assemblies.Count == 1) break;
            }
        }

        var pluginId = assemblies.FirstOrDefault() ?? "unknown";
        return (pluginId, pluginId);
    }

    private void RecordCall(string pluginId, string pluginName, string method)
    {
        lock (_lock)
        {
            var entry = _settings.PluginAuthEntries.FirstOrDefault(e => e.PluginId == pluginId);
            if (entry == null)
            {
                entry = new AIIslandPluginAuthEntry
                {
                    PluginId = pluginId,
                    PluginName = pluginName,
                    AuthMode = AIIslandAuthMode.PerCallConfirm
                };
                _settings.PluginAuthEntries.Add(entry);
            }
            else if (!string.IsNullOrWhiteSpace(pluginName))
            {
                entry.PluginName = pluginName;
            }

            entry.CallCount++;
            entry.LastCalledAt = DateTime.Now;
            entry.LastMethod = method;
        }

        OnEntriesChanged();
    }

    private void OnEntriesChanged() => EntriesChanged?.Invoke(this, EventArgs.Empty);

    private async Task<bool> ShowConfirmationDialog(
        string pluginId,
        string pluginName,
        string method,
        string? description)
    {
        // 在 UI 线程上显示确认对话框
        var tcs = new TaskCompletionSource<bool>();

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = "AIIsland 授权请求",
                    PrimaryButtonText = "允许",
                    SecondaryButtonText = "允许并记住",
                    CloseButtonText = "拒绝",
                    DefaultButton = ContentDialogButton.Primary
                };

                var message = $"插件「{pluginName}」请求调用 AIIsland 的 AI 服务。\n\n";
                message += $"调用方法：{method}\n";
                if (!string.IsNullOrWhiteSpace(description))
                    message += $"用途：{description}\n";
                message += $"\n允许后，本次调用将使用你已配置的 AI API，可能产生费用。";

                dialog.Content = message;

                var result = await dialog.ShowAsync();
                tcs.SetResult(result == ContentDialogResult.Primary ||
                              result == ContentDialogResult.Secondary);

                // "允许并记住" → 标记为已授权
                if (result == ContentDialogResult.Secondary)
                {
                    Trust(pluginId, pluginName);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"授权确认对话框显示失败: {ex.Message}");
                tcs.SetResult(false);
            }
        });

        return await tcs.Task;
    }
}
