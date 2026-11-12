using AwesomeAssertions;
using VisualStudioTranslator.Engine.LocalTranslation.Downloads;
using VisualStudioTranslator.Engine.LocalTranslation.Llama;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.LocalTranslation.Llama;

public sealed class LlamaCatalogTests
{
    private const long GiB = 1024L * 1024 * 1024;

    public static TheoryData<string> Files => ["runtime", "quality", "light"];

    private static (DownloadSource Source, string FileName) Entry(string name) => name switch
    {
        "runtime" => (LlamaCatalog.Runtime.Source, LlamaCatalog.Runtime.ArchiveName),
        "quality" => (LlamaCatalog.Quality.Source, LlamaCatalog.Quality.FileName),
        _ => (LlamaCatalog.Light.Source, LlamaCatalog.Light.FileName),
    };

    [Theory]
    [MemberData(nameof(Files))]
    public void Entry_IsAnHttpsAddressWithASizeAndAFullChecksum(string name)
    {
        DownloadSource source = Entry(name).Source;

        source.Url.Scheme.Should().Be(Uri.UriSchemeHttps);
        source.Size.Should().BePositive();
        source.Sha256.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Entry_AddressEndsInTheFileName(string name)
    {
        (DownloadSource source, string fileName) = Entry(name);

        source.Url.AbsolutePath.Should().EndWith("/" + fileName);
    }

    [Theory]
    [InlineData("quality")]
    [InlineData("light")]
    public void Model_IsPinnedToARevisionNotToWhateverIsLatest(string name)
    {
        Entry(name).Source.Url.AbsolutePath.Should().MatchRegex("/resolve/[0-9a-f]{40}/");
    }

    [Fact]
    public void Runtime_IsPinnedToItsBuild()
    {
        LlamaCatalog.Runtime.Source.Url.AbsolutePath.Should().Contain("/download/" + LlamaCatalog.Runtime.Version + "/");
    }

    [Theory]
    [InlineData(8)]
    [InlineData(12)]
    public void ChooseModel_LittleMemory_IsTheLightModel(int gigabytes)
    {
        LlamaCatalog.ChooseModel(gigabytes * GiB).Should().BeSameAs(LlamaCatalog.Light);
    }

    [Fact]
    public void ChooseModel_MachineSoldWith16Gigabytes_IsTheQualityModel()
    {
        // What such a machine reports once the graphics and firmware have taken their share.
        LlamaCatalog.ChooseModel((long)(15.3 * GiB)).Should().BeSameAs(LlamaCatalog.Quality);
    }

    [Fact]
    public void ChooseModel_MemoryNotKnown_IsTheLightModel()
    {
        LlamaCatalog.ChooseModel(0).Should().BeSameAs(LlamaCatalog.Light);
    }
}