using System.Runtime.CompilerServices;
using System.Text.Json;
using AwesomeAssertions;

namespace VisualStudioTranslator.Core.Tests.Support;

/// <summary>
/// The extension's settings are declared in a file Visual Studio reads; tests read it to keep it in step with the
/// code, which no build would otherwise notice.
/// </summary>
internal static class SettingsRegistration
{
    public static JsonDocument Read([CallerFilePath] string thisFile = "")
    {
        DirectoryInfo? directory = new FileInfo(thisFile).Directory;

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "VisualStudio-Translator.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test runs from within the repository");

        string path = Path.Combine(directory!.FullName, "src", "VisualStudioTranslator.Vsix", "Options", "registration.json");

        return JsonDocument.Parse(File.ReadAllText(path));
    }

    /// <summary>The declaration of one setting.</summary>
    public static JsonElement Property(this JsonDocument registration, string moniker) =>
        registration.RootElement.GetProperty("properties").GetProperty(moniker);
}