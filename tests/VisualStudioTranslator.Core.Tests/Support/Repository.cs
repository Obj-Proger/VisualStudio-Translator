using System.Runtime.CompilerServices;
using AwesomeAssertions;

namespace VisualStudioTranslator.Core.Tests.Support;

/// <summary>Files of the repository that tests check against the code, such as the extension's own declarations.</summary>
internal static class Repository
{
    /// <summary>The full path of a file or folder, given from the repository's root.</summary>
    public static string PathOf(string[] parts, [CallerFilePath] string thisFile = "")
    {
        DirectoryInfo? directory = new FileInfo(thisFile).Directory;

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "VisualStudio-Translator.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test runs from within the repository");

        return Path.Combine([directory!.FullName, .. parts]);
    }
}