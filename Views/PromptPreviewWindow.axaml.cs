using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ClassIsland.AISmartClass.Services;

namespace ClassIsland.AISmartClass.Views;

public partial class PromptPreviewWindow : Window
{
    private readonly TextBox? _contentBox;

    public PromptPreviewWindow(string title, string content)
    {
        InitializeComponent();
        Title = title;
        var titleText = this.FindControl<TextBlock>("TitleText");
        _contentBox = this.FindControl<TextBox>("ContentBox");
        if (titleText != null) titleText.Text = title;
        if (_contentBox != null) _contentBox.Text = content;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void OnCopyClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null && _contentBox != null)
                await clipboard.SetTextAsync(_contentBox.Text ?? "");
        }
        catch (Exception ex)
        {
            Logger.Info($"复制 AI 预览内容失败: {ex.Message}");
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
