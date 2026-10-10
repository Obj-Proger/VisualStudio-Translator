using AwesomeAssertions;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Engine.LocalTranslation;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.LocalTranslation;

public sealed class LocalModelStoreTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("vst-models-");

    public void Dispose() => _root.Delete(recursive: true);

    private LocalModelStore Store() => new(_root.FullName);

    private void WriteFile(string relativePath, int size)
    {
        string path = Path.Combine(_root.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
    }

    [Fact]
    public void DirectoryFor_UsesThePrimaryLanguageOfEachSide()
    {
        LanguagePair pair = LanguagePair.Create("en", "pt-BR")!;

        Store().DirectoryFor(pair).Should().Be(Path.Combine(_root.FullName, "en-pt"));
    }

    [Fact]
    public void ComputeRevision_NoModelsInstalled_IsNone()
    {
        Store().ComputeRevision().Should().Be("none");
        new LocalModelStore(Path.Combine(_root.FullName, "does-not-exist")).ComputeRevision().Should().Be("none");
    }

    [Fact]
    public void ComputeRevision_IsAShortLowercaseHexValueThatIsStableForTheSameFiles()
    {
        WriteFile("en-ru/model.bin", 10);

        string first = Store().ComputeRevision();

        first.Should().MatchRegex("^[0-9a-f]{12}$");
        Store().ComputeRevision().Should().Be(first);
    }

    [Fact]
    public void ComputeRevision_ChangesWhenAModelIsAddedOrReplacedByAnotherSize()
    {
        WriteFile("en-ru/model.bin", 10);
        string before = Store().ComputeRevision();

        WriteFile("en-de/model.bin", 10);
        string withAnotherModel = Store().ComputeRevision();

        WriteFile("en-ru/model.bin", 20);
        string resized = Store().ComputeRevision();

        new[] { before, withAnotherModel, resized }.Distinct().Should().HaveCount(3);
    }

    [Fact]
    public void ComputeRevision_IgnoresConfigFiles()
    {
        WriteFile("en-ru/model.bin", 10);
        string before = Store().ComputeRevision();

        WriteFile("en-ru/config.yml", 500);
        WriteFile("en-ru/config.txt", 500);

        Store().ComputeRevision().Should().Be(before);
    }

    [Fact]
    public void NotifyChanged_RaisesTheChangeCount()
    {
        LocalModelStore store = Store();
        int before = store.ChangeCount;

        store.NotifyChanged();

        store.ChangeCount.Should().Be(before + 1);
    }

    [Fact]
    public void ComputeRevision_IgnoresAFolderThatIsStillBeingInstalled()
    {
        WriteFile("en-ru/model.bin", 10);
        string before = Store().ComputeRevision();

        WriteFile($"{LocalModelStore.InstallingPrefix}abc/model.bin", 999);

        Store().ComputeRevision().Should().Be(before);
    }
}