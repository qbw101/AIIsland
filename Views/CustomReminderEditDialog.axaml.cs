using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ClassIsland.AISmartClass.Models;

namespace ClassIsland.AISmartClass.Views;

public partial class CustomReminderEditDialog : Window
{
    private readonly CustomReminder _workingCopy;

    public CustomReminder Result { get; private set; }
    public bool Confirmed { get; private set; }

    private ComboBox? _typeCombo;
    private StackPanel? _dateSection;
    private StackPanel? _timeSection;
    private StackPanel? _subjectSection;
    private StackPanel? _minutesSection;
    private DatePicker? _datePicker;
    private NumericUpDown? _hourBox;
    private NumericUpDown? _minuteBox;
    private TextBox? _subjectBox;
    private NumericUpDown? _minutesBeforeBox;
    private TextBox? _contentBox;
    private CheckBox? _enabledCb;
    private TextBlock? _errorText;

    public CustomReminderEditDialog() : this(new CustomReminder()) { }

    public CustomReminderEditDialog(CustomReminder reminder)
    {
        _workingCopy = reminder.Clone();
        Result = _workingCopy;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _typeCombo = this.FindControl<ComboBox>("TypeCombo");
        _dateSection = this.FindControl<StackPanel>("DateSection");
        _timeSection = this.FindControl<StackPanel>("TimeSection");
        _subjectSection = this.FindControl<StackPanel>("SubjectSection");
        _minutesSection = this.FindControl<StackPanel>("MinutesSection");
        _datePicker = this.FindControl<DatePicker>("DatePicker");
        _hourBox = this.FindControl<NumericUpDown>("HourBox");
        _minuteBox = this.FindControl<NumericUpDown>("MinuteBox");
        _subjectBox = this.FindControl<TextBox>("SubjectBox");
        _minutesBeforeBox = this.FindControl<NumericUpDown>("MinutesBeforeBox");
        _contentBox = this.FindControl<TextBox>("ContentBox");
        _enabledCb = this.FindControl<CheckBox>("EnabledCb");
        _errorText = this.FindControl<TextBlock>("ErrorText");

        if (_typeCombo != null)
            _typeCombo.SelectionChanged += (_, _) => UpdateVisibility();

        LoadReminder();
    }

    private void LoadReminder()
    {
        if (_typeCombo != null) _typeCombo.SelectedIndex = (int)_workingCopy.Type;

        var dateTime = _workingCopy.FixedDateTime ?? DateTime.Today.AddHours(8);
        if (_datePicker != null) _datePicker.SelectedDate = dateTime.Date;
        if (_hourBox != null) _hourBox.Value = dateTime.Hour;
        if (_minuteBox != null) _minuteBox.Value = dateTime.Minute;

        if (_subjectBox != null) _subjectBox.Text = _workingCopy.SubjectName ?? "";
        if (_minutesBeforeBox != null) _minutesBeforeBox.Value = Math.Clamp(_workingCopy.MinutesBefore, 0, 120);
        if (_contentBox != null) _contentBox.Text = _workingCopy.Content;
        if (_enabledCb != null) _enabledCb.IsChecked = _workingCopy.IsEnabled;

        UpdateVisibility();
        SetError(null);
    }

    private void UpdateVisibility()
    {
        var type = (ReminderType)(_typeCombo?.SelectedIndex ?? 0);
        var isFixed = type == ReminderType.FixedTime;
        var isSubject = type == ReminderType.SubjectLinked;
        var isDaily = type == ReminderType.DailyRepeat;

        if (_dateSection != null) _dateSection.IsVisible = isFixed;
        if (_timeSection != null) _timeSection.IsVisible = isFixed || isDaily;
        if (_subjectSection != null) _subjectSection.IsVisible = isSubject;
        if (_minutesSection != null) _minutesSection.IsVisible = isSubject;
    }

    private void OnOkClicked(object? sender, RoutedEventArgs e)
    {
        SetError(null);

        var type = (ReminderType)(_typeCombo?.SelectedIndex ?? 0);
        var content = _contentBox?.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(content))
        {
            SetError("请填写提醒内容。");
            return;
        }

        _workingCopy.Type = type;
        _workingCopy.Content = content;
        _workingCopy.IsEnabled = _enabledCb?.IsChecked ?? true;
        _workingCopy.LastTriggeredDate = null;
        _workingCopy.LastTriggeredKey = null;

        if (type == ReminderType.FixedTime || type == ReminderType.DailyRepeat)
        {
            var hour = Math.Clamp((int)(_hourBox?.Value ?? 8), 0, 23);
            var minute = Math.Clamp((int)(_minuteBox?.Value ?? 0), 0, 59);
            var date = _datePicker?.SelectedDate?.DateTime.Date ?? DateTime.Today;

            _workingCopy.FixedDateTime = type == ReminderType.FixedTime
                ? new DateTime(date.Year, date.Month, date.Day, hour, minute, 0)
                : new DateTime(2000, 1, 1, hour, minute, 0);
            _workingCopy.SubjectName = null;
            _workingCopy.MinutesBefore = 0;
            _workingCopy.IsRepeating = type == ReminderType.DailyRepeat;
        }
        else
        {
            var subject = _subjectBox?.Text?.Trim().TrimEnd('课') ?? "";
            if (string.IsNullOrWhiteSpace(subject))
            {
                SetError("请选择关联科目，例如：数学。");
                return;
            }

            _workingCopy.FixedDateTime = null;
            _workingCopy.SubjectName = subject;
            _workingCopy.MinutesBefore = Math.Clamp((int)(_minutesBeforeBox?.Value ?? 3), 0, 120);
            _workingCopy.IsRepeating = true;
        }

        Result = _workingCopy;
        Confirmed = true;
        Close();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }

    private void SetError(string? message)
    {
        if (_errorText == null) return;
        _errorText.Text = message ?? "";
        _errorText.IsVisible = !string.IsNullOrWhiteSpace(message);
    }
}
