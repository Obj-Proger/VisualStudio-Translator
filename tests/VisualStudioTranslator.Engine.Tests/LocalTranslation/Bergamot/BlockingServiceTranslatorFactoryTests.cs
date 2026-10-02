using AwesomeAssertions;
using VisualStudioTranslator.Engine.LocalTranslation.Bergamot;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.LocalTranslation.Bergamot;

// Only what can be checked without the native library: whether a directory counts as a model.
public sealed class BlockingServiceTranslatorFactoryTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("vst-factory-");

    public void Dispose() => _root.Delete(recursive: true);

    private void Touch(string name) => File.WriteAllBytes(Path.Combine(_root.FullName, name), []);

    [Fact]
    public void IsModelPresent_MissingDirectory_IsFalse()
    {
        new BlockingServiceTranslatorFactory().IsModelPresent(Path.Combine(_root.FullName, "nope")).Should().BeFalse();
    }

    [Fact]
    public void IsModelPresent_EmptyDirectory_IsFalse()
    {
        new BlockingServiceTranslatorFactory().IsModelPresent(_root.FullName).Should().BeFalse();
    }

    [Fact]
    public void IsModelPresent_DirectoryWithAConfig_IsTrue()
    {
        Touch("config.yml");

        new BlockingServiceTranslatorFactory().IsModelPresent(_root.FullName).Should().BeTrue();
    }

    [Fact]
    public void IsModelPresent_CompleteSetOfModelFilesWithoutAConfig_IsTrue()
    {
        Touch("model.enru.intgemm.alphas.bin");
        Touch("vocab.enru.spm");

        new BlockingServiceTranslatorFactory().IsModelPresent(_root.FullName).Should().BeTrue();
    }

    [Fact]
    public void IsModelPresent_OnlyPartOfTheModelFiles_IsFalse()
    {
        Touch("model.enru.intgemm.alphas.bin");

        new BlockingServiceTranslatorFactory().IsModelPresent(_root.FullName).Should().BeFalse();
    }
}