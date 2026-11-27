using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Presentation;
using VisualStudioTranslator.Vsix.Options;
using VisualStudioTranslator.Vsix.Resources;
using VisualStudioTranslator.Vsix.Translation;

namespace VisualStudioTranslator.Vsix.TranslatorWindow;

/// <summary>
/// The content of the translator window: the text to translate and its translation, and the language to translate
/// into. Built in code with Visual Studio's own styles, colours and font, so it follows the theme as the built-in
/// windows do.
/// <list type="bullet">
/// <item>The two texts stand side by side when each column holds a comfortable line, and one above the other when
/// the window is narrow, as when docked at the side (see <see cref="PaneLayout"/>). The limit is in characters of
/// the environment font, so it holds for any font size and display scaling.</item>
/// <item>The splitter between them keeps where the user left it, for each layout apart and between sessions.</item>
/// <item>The bar at the top wraps rather than cuts off its buttons in a narrow window.</item>
/// </list>
/// </summary>
internal sealed class TranslatorView : UserControl
{
    private const double Gap = 8;

    private readonly TextBox _source;
    private readonly TextBox _translation;
    private readonly ComboBox _language;
    private readonly Button _translate;
    private readonly Button _copy;
    private readonly TextBlock _status;
    private readonly Grid _panes = new();
    private readonly GridSplitter _splitter;

    // The translation under way, stopped when another is asked for. Only touched on the UI thread.
    private CancellationTokenSource? _running;

    // Set while the language is chosen in code, so only the user's own choices are remembered.
    private bool _choosingLanguage;

    private bool? _inColumns;
    private double _characterWidth;
    private TranslatorWindowState? _state;

    public TranslatorView()
    {
        SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
        SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        SetResourceReference(FontFamilyProperty, VsFonts.EnvironmentFontFamilyKey);
        SetResourceReference(FontSizeProperty, VsFonts.EnvironmentFontSizeKey);

        Label languageLabel = new()
        {
            Content = UiText.TranslateIntoLabel,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 6, Gap / 2),
        };
        languageLabel.SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);

        _language = new ComboBox { MinWidth = 160, VerticalAlignment = VerticalAlignment.Center, Margin = BarItemMargin };
        _language.SetResourceReference(StyleProperty, VsResourceKeys.ComboBoxStyleKey);
        AutomationName(_language, UiText.TranslateIntoName);
        languageLabel.Target = _language;

        foreach (string tag in TargetLanguages.Choices)
        {
            _language.Items.Add(new ComboBoxItem { Content = TargetLanguages.ListName(tag), Tag = tag });
        }

        _language.SelectionChanged += (_, _) => RememberLanguage();

        _source = TextArea(UiText.SourceTextName, readOnly: false);
        _translation = TextArea(UiText.TranslationLabel, readOnly: true);

        _translate = CreateButton(UiText.TranslateButton, UiText.TranslateButtonTip);
        _translate.Click += (_, _) => StartTranslation();

        _copy = CreateButton(UiText.CopyButton, UiText.CopyButtonTip);
        _copy.IsEnabled = false;
        _copy.Click += (_, _) => Clipboard.SetText(_translation.Text);

        WrapPanel bar = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(Gap, Gap, 0, Gap / 2) };
        bar.Children.Add(languageLabel);
        bar.Children.Add(_language);
        bar.Children.Add(_translate);
        bar.Children.Add(_copy);

        _status = new TextBlock { Margin = new Thickness(Gap, Gap / 2, Gap, Gap), TextWrapping = TextWrapping.Wrap };
        _status.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);

        // No line of its own, like the splitters between Visual Studio's panels; resized with the mouse, and not a
        // stop for Tab, which goes from the text to its translation.
        _splitter = new GridSplitter { Background = Brushes.Transparent, IsTabStop = false };
        _splitter.DragCompleted += (_, _) => RememberShare();

        _panes.Children.Add(_source);
        _panes.Children.Add(_splitter);
        _panes.Children.Add(_translation);
        Arrange(columns: false);

        Grid layout = new();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Place(layout, bar, 0);
        Place(layout, _panes, 1);
        Place(layout, _status, 2);

        Content = layout;

        SizeChanged += (_, _) => UpdateLayoutChoice();
        InputBindings.Add(new KeyBinding(new RelayCommand(StartTranslation), Key.Enter, ModifierKeys.Control));
    }

    /// <summary>
    /// Runs the translations, so Visual Studio waits for them when the package is closed. The package's own, given
    /// by the window once it belongs to the package.
    /// </summary>
    internal JoinableTaskFactory? Tasks { get; set; }

    private static Thickness BarItemMargin => new(0, 0, Gap, Gap / 2);

    /// <summary>Gives the view what it remembers between sessions, once the window belongs to the package.</summary>
    public void Attach(JoinableTaskFactory tasks, TranslatorWindowState state)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Tasks = tasks;
        _state = state;

        if (_inColumns is { } columns)
        {
            Arrange(columns);
        }

        // Visual Studio brings an open window back when it starts, before any command has chosen its language.
        if (_language.SelectedItem is null)
        {
            _ = tasks.RunAsync(async () =>
            {
                TranslatorSettings settings = await TranslatorOptions.Shared.ReadAsync(CancellationToken.None).ConfigureAwait(true);
                await tasks.SwitchToMainThreadAsync();

                if (_language.SelectedItem is null)
                {
                    ChooseLanguage(settings);
                }
            });
        }
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

    /// <summary>
    /// Chooses the language last chosen in the window, or, when none was, the one the settings say.
    /// </summary>
    public void ChooseLanguage(TranslatorSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        string wanted = _state?.Language
            ?? TargetLanguages.Resolve(settings.TargetLanguage, settings.DisplayLanguage)
            ?? "en";

        ComboBoxItem match = _language.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => TargetLanguages.IsSameLanguage((string)item.Tag, wanted))
            ?? _language.Items.OfType<ComboBoxItem>().First();

        _choosingLanguage = true;

        try
        {
            _language.SelectedItem = match;
        }
        finally
        {
            _choosingLanguage = false;
        }
    }

    // A change of the environment font changes how many characters fit, and how small a text may be made.
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == FontFamilyProperty || e.Property == FontSizeProperty)
        {
            _characterWidth = 0;

            if (_inColumns is { } columns)
            {
                Arrange(columns);
            }

            UpdateLayoutChoice();
        }
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
            Margin = BarItemMargin,
            Padding = new Thickness(Gap, 2, Gap, 2),
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
            Padding = new Thickness(4),
        };

        box.SetResourceReference(StyleProperty, VsResourceKeys.TextBoxStyleKey);
        AutomationName(box, name);
        return box;
    }

    // The average width of a character of the environment font, measured over the alphabet.
    private double CharacterWidth()
    {
        if (_characterWidth <= 0)
        {
            const string Alphabet = "abcdefghijklmnopqrstuvwxyz";

            FormattedText sample = new(
                Alphabet,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily, FontStyle, FontWeight, FontStretch),
                FontSize,
                Brushes.Black,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            _characterWidth = sample.WidthIncludingTrailingWhitespace / Alphabet.Length;
        }

        return _characterWidth;
    }

    private void UpdateLayoutChoice()
    {
        if (_panes.ActualWidth <= 0)
        {
            return;
        }

        double columnWidth = (_panes.ActualWidth - (3 * Gap)) / 2;
        bool columns = PaneLayout.UseColumns(columnWidth, CharacterWidth(), _inColumns);

        if (columns != _inColumns)
        {
            Arrange(columns);
        }
    }

    // The texts in two columns or two rows with the splitter between them, sharing the room as the user last left it.
    private void Arrange(bool columns)
    {
        _inColumns = columns;
        _panes.RowDefinitions.Clear();
        _panes.ColumnDefinitions.Clear();

        double share = PaneLayout.Share(_state?.GetShare(columns));
        GridLength first = new(share, GridUnitType.Star);
        GridLength second = new(1 - share, GridUnitType.Star);
        double characterWidth = _characterWidth > 0 ? _characterWidth : FontSize / 2;

        if (columns)
        {
            double minWidth = PaneLayout.MinColumnCharacters * characterWidth;
            _panes.ColumnDefinitions.Add(new ColumnDefinition { Width = first, MinWidth = minWidth });
            _panes.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _panes.ColumnDefinitions.Add(new ColumnDefinition { Width = second, MinWidth = minWidth });
        }
        else
        {
            // A line of text, the box's padding and border, times the lines a text keeps at least.
            double minHeight = (PaneLayout.MinRowLines * FontSize * 1.4) + 12;
            _panes.RowDefinitions.Add(new RowDefinition { Height = first, MinHeight = minHeight });
            _panes.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _panes.RowDefinitions.Add(new RowDefinition { Height = second, MinHeight = minHeight });
        }

        UIElement[] order = [_source, _splitter, _translation];

        for (int index = 0; index < order.Length; index++)
        {
            Grid.SetRow(order[index], columns ? 0 : index);
            Grid.SetColumn(order[index], columns ? index : 0);
        }

        _splitter.ResizeDirection = columns ? GridResizeDirection.Columns : GridResizeDirection.Rows;
        _splitter.Width = columns ? Gap : double.NaN;
        _splitter.Height = columns ? double.NaN : Gap;
        _splitter.HorizontalAlignment = columns ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        _splitter.VerticalAlignment = columns ? VerticalAlignment.Stretch : VerticalAlignment.Center;
        _splitter.Cursor = columns ? Cursors.SizeWE : Cursors.SizeNS;

        _source.Margin = columns ? new Thickness(Gap, 0, 0, 0) : new Thickness(Gap, 0, Gap, 0);
        _translation.Margin = columns ? new Thickness(0, 0, Gap, 0) : new Thickness(Gap, 0, Gap, 0);
    }

    private void RememberShare()
    {
        if (_state is null || _inColumns is not { } columns)
        {
            return;
        }

        double first = columns ? _source.ActualWidth : _source.ActualHeight;
        double second = columns ? _translation.ActualWidth : _translation.ActualHeight;

        if (first + second > 0)
        {
            _state.SetShare(columns, first / (first + second));
        }
    }

    private void RememberLanguage()
    {
        if (!_choosingLanguage && _state is not null && _language.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            _state.Language = tag;
        }
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
        _status.Text = UiText.Translating;

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