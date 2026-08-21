using Avalonia.Markup.Xaml;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.AISmartClass.Models;

namespace ClassIsland.AISmartClass.Controls.HomeworkEstimate;

public partial class HomeworkEstimateSettingsControl : ComponentBase<HomeworkEstimateSettings>
{
    public HomeworkEstimateSettingsControl() => AvaloniaXamlLoader.Load(this);
}
