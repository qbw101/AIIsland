using Avalonia.Markup.Xaml;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.AISmartClass.Models;

namespace ClassIsland.AISmartClass.Controls.ClassCountdown;

public partial class ClassCountdownSettingsControl : ComponentBase<ClassCountdownSettings>
{
    public ClassCountdownSettingsControl() => AvaloniaXamlLoader.Load(this);
}
