using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace ClassIsland.AISmartClass.Controls.SmartClassPanelSettings;

public partial class SmartClassPanelSettingsControl : UserControl
{
    public ClassIsland.AISmartClass.Models.SmartClassPanelSettings Settings { get; }

    public SmartClassPanelSettingsControl()
    {
        InitializeComponent();
        Settings = new ClassIsland.AISmartClass.Models.SmartClassPanelSettings();
        DataContext = this;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
