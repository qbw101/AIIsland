using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using ClassIsland.AISmartClass.Services;

namespace ClassIsland.AISmartClass.Views;

public partial class PluginAuthorizationDialog : Window
{
    private readonly List<PluginIntegrationService.DetectablePlugin> _installedPlugins;
    private readonly Dictionary<string, CheckBox> _pluginCheckBoxes = new();
    private readonly bool _defaultSelectAll;

    public HashSet<string> AuthorizedPluginIds { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool DecisionMade { get; private set; }

    public PluginAuthorizationDialog() : this(null, true)
    {
    }

    public PluginAuthorizationDialog(
        IReadOnlyCollection<string>? selectedIds = null,
        bool defaultSelectAll = true)
    {
        InitializeComponent();
        _defaultSelectAll = defaultSelectAll;
        AuthorizedPluginIds = new HashSet<string>(selectedIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        _installedPlugins = PluginIntegrationService.GetInstalledPlugins();
        BuildPluginList();
    }

    private void BuildPluginList()
    {
        PluginListPanel.Children.Clear();
        _pluginCheckBoxes.Clear();

        if (_installedPlugins.Count == 0)
        {
            var noPluginText = new TextBlock
            {
                Text = "未检测到可集成的插件",
                FontSize = 13,
                Foreground = Brushes.Gray,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Margin = new Avalonia.Thickness(0, 20)
            };
            PluginListPanel.Children.Add(noPluginText);
            ConfirmBtn.IsEnabled = false;
            return;
        }

        foreach (var plugin in _installedPlugins)
        {
            var card = new Border
            {
                Classes = { "plugin-card" }
            };

            var checkBox = new CheckBox
            {
                IsChecked = AuthorizedPluginIds.Contains(plugin.Id) ||
                            (_defaultSelectAll && AuthorizedPluginIds.Count == 0),
                FontSize = 14,
                FontWeight = FontWeight.SemiBold
            };
            _pluginCheckBoxes[plugin.Id] = checkBox;

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                RowDefinitions = new RowDefinitions("Auto,Auto")
            };

            checkBox.SetValue(Grid.RowSpanProperty, 2);
            checkBox.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            checkBox.Margin = new Avalonia.Thickness(0, 0, 12, 0);

            var nameStack = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 8
            };

            var nameText = new TextBlock
            {
                Text = plugin.Name,
                Classes = { "plugin-name" }
            };

            var categoryBadge = new Border
            {
                Padding = new Avalonia.Thickness(6, 2),
                CornerRadius = new Avalonia.CornerRadius(4),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            var categoryText = new TextBlock
            {
                Classes = { "category-badge" }
            };

            if (plugin.Category == "birthday")
            {
                categoryText.Text = "生日提醒";
                categoryBadge.Background = new SolidColorBrush(Color.Parse("#FFF3E0"));
                categoryText.Foreground = new SolidColorBrush(Color.Parse("#E65100"));
            }
            else if (plugin.Category == "duty")
            {
                categoryText.Text = "值日管理";
                categoryBadge.Background = new SolidColorBrush(Color.Parse("#E3F2FD"));
                categoryText.Foreground = new SolidColorBrush(Color.Parse("#1565C0"));
            }

            categoryBadge.Child = categoryText;
            nameStack.Children.Add(nameText);
            nameStack.Children.Add(categoryBadge);

            var descText = new TextBlock
            {
                Text = plugin.Description,
                Classes = { "plugin-desc" }
            };

            nameStack.SetValue(Grid.ColumnProperty, 1);
            descText.SetValue(Grid.ColumnProperty, 1);
            descText.SetValue(Grid.RowProperty, 1);
            descText.Margin = new Avalonia.Thickness(0, 4, 0, 0);

            grid.Children.Add(checkBox);
            grid.Children.Add(nameStack);
            grid.Children.Add(descText);

            card.Child = grid;
            PluginListPanel.Children.Add(card);

            // 绑定勾选状态改变
            checkBox.IsCheckedChanged += (s, e) =>
            {
                if (checkBox.IsChecked == true)
                {
                    card.Classes.Add("authorized");
                }
                else
                {
                    card.Classes.Remove("authorized");
                }
            };

            if (checkBox.IsChecked == true) card.Classes.Add("authorized");
        }
    }

    private void OnConfirmClicked(object? sender, RoutedEventArgs e)
    {
        AuthorizedPluginIds.Clear();

        foreach (var kvp in _pluginCheckBoxes)
        {
            if (kvp.Value.IsChecked == true)
            {
                AuthorizedPluginIds.Add(kvp.Key);
            }
        }

        DecisionMade = true;
        Close(true);
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        DecisionMade = true;
        AuthorizedPluginIds.Clear();
        Close(false);
    }
}
