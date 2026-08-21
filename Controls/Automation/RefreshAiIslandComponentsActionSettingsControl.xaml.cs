using Avalonia.Markup.Xaml;
using ClassIsland.AISmartClass.Models.Automation;
using ClassIsland.Core.Abstractions.Controls;

namespace ClassIsland.AISmartClass.Controls.Automation;

public partial class RefreshAiIslandComponentsActionSettingsControl
    : ActionSettingsControlBase<RefreshAiIslandComponentsSettings>
{
    public RefreshAiIslandComponentsActionSettingsControl() => AvaloniaXamlLoader.Load(this);
}
