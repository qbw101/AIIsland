using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ClassIsland.AISmartClass.Models;
using ClassIsland.AISmartClass.Services;

namespace ClassIsland.AISmartClass.Views;

public partial class OpenSourceLicensesWindow : Window
{
    private readonly IReadOnlyList<OpenSourceLibrary> _libraries;
    private readonly TextBox _search;
    private readonly ListBox _direct;
    private readonly ListBox _transitive;
    private readonly TabControl _tabs;
    private OpenSourceLibrary? _selected;

    public OpenSourceLicensesWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _search = this.FindControl<TextBox>("SearchBox")!;
        _direct = this.FindControl<ListBox>("DirectList")!;
        _transitive = this.FindControl<ListBox>("TransitiveList")!;
        _tabs = this.FindControl<TabControl>("LibraryTabs")!;
        _libraries = OpenSourceNotices.Libraries;
        _search.TextChanged += (_, _) => RefreshLibraries();
        _direct.SelectionChanged += (_, _) => ShowSelectedLibrary();
        _transitive.SelectionChanged += (_, _) => ShowSelectedLibrary();
        _tabs.SelectionChanged += (_, _) => ShowSelectedLibrary();
        RefreshLibraries();
    }

    private void RefreshLibraries()
    {
        var query = _search.Text?.Trim() ?? "";
        var matches = _libraries.Where(library =>
            library.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            library.License.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        var direct = matches.Where(library => library.IsDirect).ToArray();
        var transitive = matches.Where(library => !library.IsDirect).ToArray();
        _direct.ItemsSource = direct;
        _transitive.ItemsSource = transitive;
        this.FindControl<TabItem>("DirectTab")!.Header = $"直接依赖 ({direct.Length})";
        this.FindControl<TabItem>("TransitiveTab")!.Header = $"间接依赖 ({transitive.Length})";
        _direct.SelectedIndex = direct.Length > 0 ? 0 : -1;
        _transitive.SelectedIndex = transitive.Length > 0 ? 0 : -1;
        if (direct.Length == 0 && transitive.Length > 0) _tabs.SelectedIndex = 1;
        else if (transitive.Length == 0 && direct.Length > 0) _tabs.SelectedIndex = 0;
        ShowSelectedLibrary();
    }

    private void ShowSelectedLibrary()
    {
        _selected = (_tabs.SelectedIndex == 1 ? _transitive : _direct).SelectedItem as OpenSourceLibrary;
        this.FindControl<Grid>("DetailsPanel")!.IsVisible = _selected != null;
        this.FindControl<TextBlock>("EmptyMessage")!.IsVisible = _selected == null;
        if (_selected == null)
        {
            this.FindControl<TextBlock>("EmptyMessage")!.Text = "没有匹配的依赖，请尝试其他库名或许可证。";
            return;
        }

        this.FindControl<TextBlock>("LibraryNameText")!.Text = _selected.Name;
        this.FindControl<TextBlock>("LibraryVersionText")!.Text =
            $"版本 {_selected.Version} · {(_selected.IsDirect ? "直接依赖" : "间接依赖")}";
        this.FindControl<TextBlock>("LibraryLicenseText")!.Text = $"许可证：{_selected.License}";
        this.FindControl<SelectableTextBlock>("SourceUrlText")!.Text =
            string.IsNullOrEmpty(_selected.SourceUrl) ? "包元数据未提供源码地址，可查看 NuGet 页面。" : _selected.SourceUrl;
        var sourceButton = this.FindControl<Button>("SourceButton")!;
        sourceButton.Content = _selected.SourceLabel;
        sourceButton.IsEnabled = !string.IsNullOrEmpty(_selected.SourceUrl);
        this.FindControl<Button>("LicenseButton")!.IsEnabled = !string.IsNullOrEmpty(_selected.LicenseUrl);
        this.FindControl<TextBox>("NoticeBox")!.Text = _selected.NoticeText;
        this.FindControl<TextBlock>("StatusText")!.Text = "许可信息以各项目原文为准。";
    }

    private async Task OpenLinkAsync(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) return;
        try
        {
            if (!await Launcher.LaunchUriAsync(uri))
                this.FindControl<TextBlock>("StatusText")!.Text = "无法打开链接，可复制上方地址后在浏览器中查看。";
        }
        catch (Exception ex)
        {
            Logger.Info($"打开开源项目链接失败：{ex.Message}");
            this.FindControl<TextBlock>("StatusText")!.Text = "打开链接失败，请稍后重试。";
        }
    }

    private async void OnSourceClicked(object? sender, RoutedEventArgs e) => await OpenLinkAsync(_selected?.SourceUrl);
    private async void OnLicenseClicked(object? sender, RoutedEventArgs e) => await OpenLinkAsync(_selected?.LicenseUrl);
    private async void OnNugetClicked(object? sender, RoutedEventArgs e) => await OpenLinkAsync(_selected?.NugetUrl);
    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}
