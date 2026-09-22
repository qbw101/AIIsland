using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ClassIsland.AISmartClass.Services;

namespace ClassIsland.AISmartClass.Views;

/// <summary>
/// AI 角色设定生成向导。
/// 流程：填写角色名/补充要求 → 预填并可自由修改的提示词 → 调 AI 生成 → 预览中直接编辑 → 应用。
/// </summary>
public partial class PersonaWizardDialog : Window
{
    /// <summary>生成的（或用户编辑后的）角色设定文本。</summary>
    public string Result { get; private set; } = "";

    /// <summary>用户是否点了「应用」。</summary>
    public bool Confirmed { get; private set; }

    private TextBox? _characterNameBox;
    private TextBox? _extraBox;
    private TextBox? _promptBox;
    private Border? _resultSection;
    private TextBox? _resultBox;
    private TextBlock? _statusText;
    private TextBlock? _errorText;
    private Button? _generateButton;
    private Button? _applyButton;

    private bool _busy;
    private readonly string _existingPersona;

    public PersonaWizardDialog() : this("") { }

    /// <summary>传入现有自定义角色设定时，会预填到结果区，便于直接在其基础上修改或让 AI 重写。</summary>
    public PersonaWizardDialog(string? existingPersona)
    {
        _existingPersona = existingPersona?.Trim() ?? "";
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _characterNameBox = this.FindControl<TextBox>("CharacterNameBox");
        _extraBox = this.FindControl<TextBox>("ExtraBox");
        _promptBox = this.FindControl<TextBox>("PromptBox");
        _resultSection = this.FindControl<Border>("ResultSection");
        _resultBox = this.FindControl<TextBox>("ResultBox");
        _statusText = this.FindControl<TextBlock>("StatusText");
        _errorText = this.FindControl<TextBlock>("ErrorText");
        _generateButton = this.FindControl<Button>("GenerateBtn");
        _applyButton = this.FindControl<Button>("ApplyBtn");

        if (_resultBox != null)
            _resultBox.TextChanged += (_, _) => UpdateGenerateButtonLabel();

        if (_promptBox != null)
            _promptBox.Text = PersonaWizardPrompt.DefaultUserPromptTemplate;

        if (!string.IsNullOrWhiteSpace(_existingPersona))
        {
            if (_resultBox != null) _resultBox.Text = _existingPersona;
            if (_resultSection != null) _resultSection.IsVisible = true;
            SetApplyEnabled(true);

            // 已有设定里带了角色名 → 回填到输入框，方便直接「重新生成」同一角色
            if (PersonaCatalog.TrySplitCustomPersona(_existingPersona, out var existingName, out _)
                && _characterNameBox != null
                && !string.Equals(existingName, PersonaCatalog.FallbackName, StringComparison.Ordinal))
            {
                _characterNameBox.Text = existingName;
            }
        }

        UpdateGenerateButtonLabel();
        SetStatus("填写信息后点击左下角按钮，AI 会给出角色语气描述；不满意可以随时再生成一次。");
    }

    private async void OnGenerateClicked(object? sender, RoutedEventArgs e)
    {
        if (_busy) return;

        if (_promptBox == null || string.IsNullOrWhiteSpace(_promptBox.Text))
        {
            SetError("生成提示词不能为空。");
            return;
        }

        var aiService = Plugin.GetAIService();
        if (aiService == null)
        {
            SetError("AI 服务未初始化，请先在设置页保存 API 配置后再试。");
            return;
        }

        SetError(null);
        SetBusy(true);
        SetStatus(HasResult() ? "正在重新生成，请稍候…" : "正在生成角色设定，请稍候…");

        try
        {
            var userPrompt = PersonaWizardPrompt.BuildUserPrompt(
                _promptBox.Text,
                _characterNameBox?.Text,
                _extraBox?.Text);

            // bypassCache：角色设定允许反复「重新生成」，必须绕开结果缓存，否则会原样返回上一次的内容。
            var raw = await aiService.ChatAsync(
                PersonaWizardPrompt.SystemPrompt,
                userPrompt,
                temperature: 0.8,
                throwOnError: true,
                bypassCache: true);

            // 统一整理成「角色名称：XX + 语气描述」，保证生成结果一定带角色名
            var result = PersonaWizardPrompt.ComposePersonaText(raw, _characterNameBox?.Text);
            if (string.IsNullOrWhiteSpace(result))
            {
                SetBusy(false);
                SetStatus(null);
                SetError("AI 没有返回有效内容，请调整提示词后重试。");
                return;
            }

            if (_resultBox != null) _resultBox.Text = result;
            if (_resultSection != null) _resultSection.IsVisible = true;
            SetApplyEnabled(true);
            SetBusy(false);
            UpdateGenerateButtonLabel();
            SetStatus("生成完成，可以在下方直接修改；不满意的角色名或语气可改好后点「重新生成」。");
        }
        catch (Exception ex)
        {
            Logger.Error($"AI 生成角色设定失败: {ex.Message}");
            SetBusy(false);
            SetStatus(null);
            SetError($"生成失败: {ex.Message}");
        }
    }

    private void OnApplyClicked(object? sender, RoutedEventArgs e)
    {
        var raw = _resultBox?.Text ?? "";
        var composed = PersonaWizardPrompt.ComposePersonaText(raw, _characterNameBox?.Text);
        if (string.IsNullOrWhiteSpace(composed))
        {
            SetError("生成结果为空，无法应用。请先生成或填写内容。");
            return;
        }

        Result = composed;
        Confirmed = true;
        Close();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        if (_generateButton == null) return;

        _generateButton.IsEnabled = !busy;
        if (busy)
            _generateButton.Content = "⏳ 生成中…";
        else
            UpdateGenerateButtonLabel();
    }

    /// <summary>已有结果时按钮显示「重新生成」，方便反复换一版。</summary>
    private void UpdateGenerateButtonLabel()
    {
        if (_generateButton == null || _busy) return;
        _generateButton.Content = HasResult() ? "🔄 重新生成" : "✨ 生成设定";
    }

    private bool HasResult() => !string.IsNullOrWhiteSpace(_resultBox?.Text);

    private void SetApplyEnabled(bool enabled)
    {
        if (_applyButton != null) _applyButton.IsEnabled = enabled;
    }

    private void SetStatus(string? message)
    {
        if (_statusText == null) return;
        _statusText.Text = message ?? "";
        _statusText.IsVisible = !string.IsNullOrWhiteSpace(message);
    }

    private void SetError(string? message)
    {
        if (_errorText == null) return;
        _errorText.Text = message ?? "";
        _errorText.IsVisible = !string.IsNullOrWhiteSpace(message);
    }
}
