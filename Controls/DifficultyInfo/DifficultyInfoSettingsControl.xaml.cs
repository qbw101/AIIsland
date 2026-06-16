using Avalonia.Markup.Xaml;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.AISmartClass.Models;

namespace ClassIsland.AISmartClass.Controls.DifficultyInfo;

public partial class DifficultyInfoSettingsControl : ComponentBase<DifficultyInfoSettings>
{
    public DifficultyInfoSettingsControl() => AvaloniaXamlLoader.Load(this);
}
