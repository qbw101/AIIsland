using Avalonia.Markup.Xaml;
using ClassIsland.AISmartClass.Models.Automation;
using ClassIsland.Core.Abstractions.Controls;

namespace ClassIsland.AISmartClass.Controls.Automation;

public partial class TriggerAiIslandReminderActionSettingsControl
    : ActionSettingsControlBase<TriggerAiIslandReminderSettings>
{
    public TriggerAiIslandReminderActionSettingsControl() => AvaloniaXamlLoader.Load(this);
}
