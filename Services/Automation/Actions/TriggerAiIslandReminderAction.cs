using ClassIsland.AISmartClass.Models.Automation;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;

namespace ClassIsland.AISmartClass.Services.Automation.Actions;

[ActionInfo(
    "aiisland.trigger-reminder",
    "触发 AIIsland 贴心提醒",
    "\ue7ed",
    addDefaultToMenu: true,
    defaultGroupToMenu: "AIIsland")]
public class TriggerAiIslandReminderAction : ActionBase<TriggerAiIslandReminderSettings>
{
    protected override async Task OnInvoke()
    {
        await base.OnInvoke();

        var notifier = Plugin.SmartClassNotifierInstance
            ?? throw new InvalidOperationException("AIIsland 智能提醒提供方尚未就绪。");

        switch (Settings.Reminder)
        {
            case AiIslandReminder.BeforeSchool:
                await notifier.ManualBeforeSchoolReminderAsync(
                    Settings.BypassCache, InterruptCancellationToken);
                break;
            case AiIslandReminder.BeforeClass:
                await notifier.ManualBeforeClassReminderAsync(
                    Settings.BypassCache, InterruptCancellationToken);
                break;
            case AiIslandReminder.AfterSchool:
                await notifier.ManualAfterSchoolSummaryAsync(
                    Settings.BypassCache, InterruptCancellationToken);
                break;
            default:
                throw new InvalidOperationException("不支持的 AIIsland 提醒类型。");
        }
    }
}
