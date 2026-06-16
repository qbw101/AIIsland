using Avalonia.Markup.Xaml;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.AISmartClass.Models;

namespace ClassIsland.AISmartClass.Controls.NotificationProviders;

/// <summary>
/// AIIsland 智能提醒的设置控件。
/// 必须继承 NotificationProviderControlBase&lt;T&gt;，由 ClassIsland 通过 SettingsInternal 注入设置实例，
/// 设置变更后由主程序自动持久化到 Settings.json（NotificationProvidersSettings 字典）。
/// 不要在此设置 DataContext = this，axaml 内部用 FindAncestor 绑定，否则 Settings 解析失败。
/// </summary>
public partial class SmartClassNotifierSettingsControl : NotificationProviderControlBase<SmartClassNotifierSettings>
{
    public SmartClassNotifierSettingsControl()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
