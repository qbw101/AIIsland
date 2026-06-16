using Avalonia.Markup.Xaml;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.AISmartClass.Models;

namespace ClassIsland.AISmartClass.Controls.CurrentHint;

public partial class CurrentHintSettingsControl : ComponentBase<CurrentHintSettings>
{
    public CurrentHintSettingsControl() => AvaloniaXamlLoader.Load(this);
}
