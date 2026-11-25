using System.Globalization;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;
using VisualStudioTranslator.Vsix.Service;

namespace VisualStudioTranslator.Vsix.TranslatorWindow;

/// <summary>
/// What the translator window remembers between sessions: where the user left the splitter, for columns and for
/// rows apart, and the language last chosen in the window. Kept in Visual Studio's store of user settings, as the
/// built-in windows keep theirs. When the store cannot be had, the window still works and simply forgets.
/// </summary>
internal sealed class TranslatorWindowState
{
    private const string Collection = @"VisualStudioTranslator\TranslatorWindow";
    private const string ColumnsShare = "ColumnsShare";
    private const string RowsShare = "RowsShare";
    private const string LanguageProperty = "Language";

    private readonly WritableSettingsStore? _store;

    public TranslatorWindowState(IServiceProvider services)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            _store = new ShellSettingsManager(services).GetWritableSettingsStore(SettingsScope.UserSettings);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error(nameof(TranslatorWindowState), $"The settings store is not available: {ex}");
        }
    }

    /// <summary>The share of the room the text to translate had, or <see langword="null"/> when never set.</summary>
    public double? GetShare(bool columns)
    {
        string? value = Read(columns ? ColumnsShare : RowsShare);

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double share) ? share : null;
    }

    public void SetShare(bool columns, double share) =>
        Write(columns ? ColumnsShare : RowsShare, share.ToString("0.###", CultureInfo.InvariantCulture));

    /// <summary>The tag of the language last chosen in the window, or <see langword="null"/> when none was.</summary>
    public string? Language
    {
        get => Read(LanguageProperty);
        set => Write(LanguageProperty, value ?? string.Empty);
    }

    // The store itself may be used from any thread; the window only uses it from the UI thread.
    private string? Read(string property)
    {
        try
        {
            string? value = _store?.GetString(Collection, property, string.Empty);
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error(nameof(TranslatorWindowState), $"Reading {property} failed: {ex}");
            return null;
        }
    }

    private void Write(string property, string value)
    {
        if (_store is null)
        {
            return;
        }

        try
        {
            if (!_store.CollectionExists(Collection))
            {
                _store.CreateCollection(Collection);
            }

            _store.SetString(Collection, property, value);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error(nameof(TranslatorWindowState), $"Saving {property} failed: {ex}");
        }
    }
}