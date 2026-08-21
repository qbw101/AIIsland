using ClassIsland.AISmartClass.Models.Automation;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;

namespace ClassIsland.AISmartClass.Services.Automation.Actions;

[ActionInfo(
    "aiisland.refresh-components",
    "刷新 AIIsland 组件",
    "\ue72c",
    addDefaultToMenu: true,
    defaultGroupToMenu: "AIIsland")]
public class RefreshAiIslandComponentsAction : ActionBase<RefreshAiIslandComponentsSettings>
{
    protected override async Task OnInvoke()
    {
        await base.OnInvoke();
        InterruptCancellationToken.ThrowIfCancellationRequested();

        switch (Settings.Target)
        {
            case AiIslandRefreshTarget.ScheduleSummary:
                AIRegenerationService.RequestRegenerateSummary();
                break;
            case AiIslandRefreshTarget.LearningHint:
                AIRegenerationService.RequestRegenerateHint();
                break;
            case AiIslandRefreshTarget.HomeworkEstimate:
                AIRegenerationService.RequestRegenerateHomeworkEstimate();
                break;
            case AiIslandRefreshTarget.All:
                AIRegenerationService.RequestRegenerateAll();
                break;
            default:
                throw new InvalidOperationException("不支持的 AIIsland 组件刷新目标。");
        }
    }
}
