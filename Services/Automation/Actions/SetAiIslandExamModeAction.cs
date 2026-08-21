using ClassIsland.AISmartClass.Models.Automation;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;

namespace ClassIsland.AISmartClass.Services.Automation.Actions;

[ActionInfo(
    "aiisland.set-exam-mode",
    "设置考试模式",
    "\ue91f",
    addDefaultToMenu: true,
    defaultGroupToMenu: "AIIsland")]
public class SetAiIslandExamModeAction : ActionBase<SetAiIslandExamModeSettings>
{
    protected override async Task OnInvoke()
    {
        await base.OnInvoke();

        var shouldRun = Settings.Mode == AiIslandExamMode.Start;
        if (!await AIRegenerationService.SetExamModeAsync(
                shouldRun,
                Settings.OpenDashboard,
                InterruptCancellationToken))
        {
            throw new InvalidOperationException(shouldRun
                ? "AIIsland 考试模式启动失败。"
                : "AIIsland 考试模式停止失败。");
        }
    }
}
