using Avalonia.Markup.Xaml;
using ClassIsland.AISmartClass.Models.Automation;
using ClassIsland.Core.Abstractions.Controls;

namespace ClassIsland.AISmartClass.Controls.Automation;

public partial class SetAiIslandExamModeActionSettingsControl
    : ActionSettingsControlBase<SetAiIslandExamModeSettings>
{
    public SetAiIslandExamModeActionSettingsControl() => AvaloniaXamlLoader.Load(this);
}
