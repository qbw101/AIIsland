using Avalonia.Markup.Xaml;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.AISmartClass.Models;

namespace ClassIsland.AISmartClass.Controls.ScheduleInsight;

public partial class ScheduleInsightSettingsControl : ComponentBase<ScheduleInsightSettings>
{
    public ScheduleInsightSettingsControl() => AvaloniaXamlLoader.Load(this);
}
