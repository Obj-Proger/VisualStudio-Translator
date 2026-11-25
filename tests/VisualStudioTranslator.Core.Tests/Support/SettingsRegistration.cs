using System.Text.Json;

namespace VisualStudioTranslator.Core.Tests.Support;

/// <summary>
/// The extension's settings are declared in a file Visual Studio reads; tests read it to keep it in step with the
/// code, which no build would otherwise notice.
/// </summary>
internal static class SettingsRegistration
{
    public static JsonDocument Read() => JsonDocument.Parse(
        File.ReadAllText(Repository.PathOf(["src", "VisualStudioTranslator.Vsix", "Options", "registration.json"])));

    /// <summary>The declaration of one setting.</summary>
    public static JsonElement Property(this JsonDocument registration, string moniker) =>
        registration.RootElement.GetProperty("properties").GetProperty(moniker);
}