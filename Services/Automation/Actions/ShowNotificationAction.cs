using Avalonia.Threading;
using ClassIsland.AISmartClass;

namespace ClassIsland.AISmartClass.Services.Automation.Actions;

/// <summary>
/// 显示通知动作：通过 SmartClassNotifier 已验证的通知通道弹窗。
/// 在 UI 线程执行，避免跨线程访问通知 API。
/// </summary>
public class ShowNotificationAction : IAction
{
    public string Title { get; init; } = "AIIsland 自动化";

    public string Body { get; init; } = "";

    public Task ExecuteAsync(RuleContext context, CancellationToken ct = default)
    {
        Dispatcher.UIThread.Post(() =>
            Plugin.SmartClassNotifierInstance?.ShowAutomationNotification(Title, Body));
        return Task.CompletedTask;
    }
}
