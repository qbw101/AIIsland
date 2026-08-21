using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ClassIsland.AISmartClass.Models.Automation;
using ClassIsland.Core.Abstractions.Controls;

namespace ClassIsland.AISmartClass.Controls.Automation;

public partial class GenerateAiNotificationActionSettingsControl
    : ActionSettingsControlBase<GenerateAiNotificationSettings>
{
    public GenerateAiNotificationActionSettingsControl() => AvaloniaXamlLoader.Load(this);

    private void OnInsertVariableClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string variable } ||
            this.FindControl<TextBox>("CustomPromptTextBox") is not { } textBox)
        {
            return;
        }

        var text = textBox.Text ?? "";
        var selectionStart = Math.Clamp(textBox.SelectionStart, 0, text.Length);
        var selectionEnd = Math.Clamp(textBox.SelectionEnd, selectionStart, text.Length);
        textBox.Text = text[..selectionStart] + variable + text[selectionEnd..];

        var caretIndex = selectionStart + variable.Length;
        textBox.SelectionStart = caretIndex;
        textBox.SelectionEnd = caretIndex;
        textBox.Focus();
    }
}
