using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Vsix.Options;
using VisualStudioTranslator.Vsix.Translation;

namespace VisualStudioTranslator.Vsix.TranslatorWindow;

/// <summary>
/// The content of the translator window: the text to translate above, its translation below, and the language to
/// translate into. Built in code with Visual Studio's own styles and colours, so it follows the theme as the
/// built-in windows do.
/// </summary>
internal sealed class TranslatorView : UserControl
{
    private readonly TextBox _source;
    private readonly TextBox _translation;
    private readonly ComboBox _language;
    private readonly Button _translate;
    private readonly Button _copy;
    private readonly TextBlock _status;

    // The translation under way, stopped when another is asked for. Only touched on the UI thread.
    private CancellationTokenSource? _running;
    private bool _languageChosen;

    /// <summary>
    /// Runs the translations, so Visual Studio waits for them when the package is closed. The package's own, given
    /// by the window once it belongs to the package.
    /// </summary>
    internal JoinableTaskFactory? Tasks { get; set; }

    public TranslatorView()
    {
        SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
        SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        SetResourceReference(FontFamilyProperty, VsFonts.EnvironmentFontFamilyKey);
        SetResourceReference(FontSizeProperty, VsFonts.EnvironmentFontSizeKey);

        Label languageLabel = new()
        {
            Content = "_Translate into:",
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0, 0, 6, 0),
        };
        languageLabel.SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);

        _language = new ComboBox { MinWidth = 160, VerticalAlignment = VerticalAlignment.Center };
        _language.SetResourceReference(StyleProperty, VsResourceKeys.ComboBoxStyleKey);
        AutomationName(_language, "Translate into");
        languageLabel.Target = _language;

        foreach (string tag in TargetLanguages.Choices)
        {
            _language.Items.Add(new ComboBoxItem { Content = TargetLanguages.NativeName(tag), Tag = tag });
        }

        _language.SelectionChanged += (_, _) => _languageChosen = true;

        _source = TextArea("Text to translate", readOnly: false);
        _translation = TextArea("Translation", readOnly: true);

        _translate = CreateButton("Translate", "Translate the text (Ctrl+Enter)");
        _translate.Click += (_, _) => StartTranslation();

        _copy = CreateButton("Copy", "Copy the translation");
        _copy.IsEnabled = false;
        _copy.Click += (_, _) => Clipboard.SetText(_translation.Text);

        StackPanel bar = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 8, 8, 6) };
        bar.Children.Add(languageLabel);
        bar.Children.Add(_language);
        bar.Children.Add(_translate);
        bar.Children.Add(_copy);

        _status = new TextBlock { Margin = new Thickness(8, 4, 8, 8), TextWrapping = TextWrapping.Wrap };
        _status.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);

        GridSplitter splitter = new()
        {
            Height = 5,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Background = System.Windows.Media.Brushes.Transparent,
            ResizeDirection = GridResizeDirection.Rows,
        };

        Grid layout = new();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 40 });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 40 });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Place(layout, bar, 0);
        Place(layout, _source, 1);
        Place(layout, splitter, 2);
        Place(layout, _translation, 3);
        Place(layout, _status, 4);

        Content = layout;

        InputBindings.Add(new KeyBinding(new RelayCommand(StartTranslation), Key.Enter, ModifierKeys.Control));
    }

    /// <summary>Puts the cursor in the text to translate.</summary>
    public void FocusSource()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _source.Focus();
        Keyboard.Focus(_source);
    }

    /// <summary>Puts <paramref name="text"/> in as the text to translate and translates it.</summary>
    public void Translate(string text)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _source.Text = text;
        StartTranslation();
    }

    /// <summary>Chooses the language the settings say, unless the user has picked one here already.</summary>
    public void UseLanguageFromSettings(TranslatorSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (_languageChosen && _language.SelectedItem is not null)
        {
            return;
        }

        string wanted = TargetLanguages.Resolve(settings.TargetLanguage, settings.DisplayLanguage) ?? "en";
        ComboBoxItem? match = _language.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => TargetLanguages.IsSameLanguage((string)item.Tag, wanted))
            ?? _language.Items.OfType<ComboBoxItem>().First();

        _language.SelectedItem = match;
        _languageChosen = false;
    }

    private static void Place(Grid grid, UIElement element, int row)
    {
        Grid.SetRow(element, row);
        grid.Children.Add(element);
    }

    private static void AutomationName(DependencyObject element, string name) =>
        System.Windows.Automation.AutomationProperties.SetName(element, name);

    private static Button CreateButton(string text, string tip)
    {
        Button button = new()
        {
            Content = text,
            ToolTip = tip,
            MinWidth = 75,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(8, 2, 8, 2),
        };
        button.SetResourceReference(StyleProperty, VsResourceKeys.ButtonStyleKey);
        return button;
    }

    private static TextBox TextArea(string name, bool readOnly)
    {
        TextBox box = new()
        {
            AcceptsReturn = !readOnly,
            AcceptsTab = false,
            IsReadOnly = readOnly,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(8, 0, 8, 0),
            Padding = new Thickness(4),
        };

        box.SetResourceReference(StyleProperty, VsResourceKeys.TextBoxStyleKey);
        AutomationName(box, name);
        return box;
    }

    // Called on the UI thread only: from the window's own controls, and from the methods above.
    private void StartTranslation()
    {
        if (Tasks is not { } tasks
            || _language.SelectedItem is not ComboBoxItem { Tag: string target }
            || string.IsNullOrWhiteSpace(_source.Text))
        {
            return;
        }

        _running?.Cancel();
        _running?.Dispose();
        CancellationTokenSource running = new();
        _running = running;

        string text = _source.Text;
        _translation.Text = string.Empty;
        _copy.IsEnabled = false;
        _status.Text = "Translating…";

        _ = tasks.RunAsync(async () =>
        {
            TextTranslation result;

            try
            {
                result = await TextTranslator.Shared.TranslateAsync(text, target, running.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                // A newer translation took its place and shows its own state.
                return;
            }

            await tasks.SwitchToMainThreadAsync();

            if (!ReferenceEquals(_running, running))
            {
                return;
            }

            _translation.Text = result.Text ?? string.Empty;
            _copy.IsEnabled = !string.IsNullOrEmpty(result.Text);
            _status.Text = result.Problem ?? string.Empty;
        });
    }

    // Ctrl+Enter, wherever the focus is in the window.
    private sealed class RelayCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute();
    }
}