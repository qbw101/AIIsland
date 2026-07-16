using Avalonia.Markup.Xaml;
using ClassIsland.Core.Abstractions.Controls;

namespace ClassIsland.AISmartClass.Controls.SmartClassPanelSettings;

public partial class SmartClassPanelSettingsControl : ComponentBase<Models.SmartClassPanelSettings>
{
    public SmartClassPanelSettingsControl() => AvaloniaXamlLoader.Load(this);
}
