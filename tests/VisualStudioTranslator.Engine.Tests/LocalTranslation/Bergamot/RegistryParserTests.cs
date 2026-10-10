using System.Text.Json;
using AwesomeAssertions;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Engine.LocalTranslation.Bergamot;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.LocalTranslation.Bergamot;

public sealed class RegistryParserTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static JsonElement Registry(string json) => JsonDocument.Parse(json).RootElement;

    private static string Files(string tag) => $$"""
        { "model": { "path": "{{tag}}/model.bin.gz" }, "vocab": { "path": "{{tag}}/vocab.spm.gz" } }
        """;

    private static RegistryModel? Find(string json, string source = "en", string target = "ru") =>
        RegistryParser.Find(Registry(json), LanguagePair.Create(source, target)!);

    [Fact]
    public void Find_PrefersAReleaseOfTheStrongestArchitecture()
    {
        RegistryModel? model = Find($$"""
            { "baseUrl": "https://m.test/r", "models": { "en-ru": [
              { "architecture": "tiny", "files": {{Files("tiny")}} },
              { "architecture": "base-memory", "releaseStatus": "Release", "files": {{Files("memory")}} },
              { "architecture": "base", "releaseStatus": "Release", "files": {{Files("base")}} }
            ] } }
            """);

        model!.Architecture.Should().Be("base");
        model.Files[0].Path.Should().Be("base/model.bin.gz");
        model.Direction.Should().Be("en-ru");
    }

    [Fact]
    public void Find_NoBaseRelease_FallsBackToTheNextArchitecture()
    {
        RegistryModel? model = Find($$"""
            { "baseUrl": "https://m.test/r", "models": { "en-ru": [
              { "architecture": "tiny", "releaseStatus": "Release", "files": {{Files("tiny")}} },
              { "architecture": "base-memory", "releaseStatus": "Release", "files": {{Files("memory")}} }
            ] } }
            """);

        model!.Architecture.Should().Be("base-memory");
    }

    [Fact]
    public void Find_NoReleaseAtAll_StillChoosesByArchitecture()
    {
        RegistryModel? model = Find($$"""
            { "baseUrl": "https://m.test/r", "models": { "en-ru": [
              { "architecture": "tiny", "files": {{Files("tiny")}} },
              { "architecture": "base", "files": {{Files("base")}} }
            ] } }
            """);

        model!.Architecture.Should().Be("base");
    }

    [Fact]
    public void Find_ReleaseBeatsAStrongerArchitectureThatIsNotARelease()
    {
        RegistryModel? model = Find($$"""
            { "baseUrl": "https://m.test/r", "models": { "en-ru": [
              { "architecture": "base", "releaseStatus": "Beta", "files": {{Files("beta")}} },
              { "architecture": "tiny", "releaseStatus": "Release", "files": {{Files("tiny")}} }
            ] } }
            """);

        model!.Architecture.Should().Be("tiny");
    }

    [Fact]
    public void Find_SeparateVocabulariesAndAShortlist_AreAllListed()
    {
        RegistryModel? model = Find("""
            { "baseUrl": "https://m.test/r", "models": { "en-ru": [ { "architecture": "base", "files": {
              "model": { "path": "a/model.bin.gz" },
              "srcVocab": { "path": "a/src.spm.gz" },
              "trgVocab": { "path": "a/trg.spm.gz" },
              "lexicalShortlist": { "path": "a/lex.bin.gz" }
            } } ] } }
            """);

        model!.Files.Select(file => file.Path).Should().Equal("a/model.bin.gz", "a/src.spm.gz", "a/trg.spm.gz", "a/lex.bin.gz");
    }

    [Fact]
    public void Find_CandidateMissingItsVocabulary_IsSkippedForTheNextOne()
    {
        RegistryModel? model = Find($$"""
            { "baseUrl": "https://m.test/r", "models": { "en-ru": [
              { "architecture": "base", "releaseStatus": "Release", "files": { "model": { "path": "a/model.bin.gz" } } },
              { "architecture": "base-memory", "releaseStatus": "Release", "files": {{Files("memory")}} }
            ] } }
            """);

        model!.Architecture.Should().Be("base-memory");
    }

    [Theory]
    [InlineData("../escape/model.bin.gz")]
    [InlineData("/absolute/model.bin.gz")]
    [InlineData("a\\\\b\\\\model.bin.gz")]
    [InlineData("a/model.bin.gz?x=1")]
    [InlineData("a/../model.bin.gz")]
    public void Find_UnsafePath_MakesTheCandidateIncomplete(string path)
    {
        RegistryModel? model = Find($$"""
            { "baseUrl": "https://m.test/r", "models": { "en-ru": [ { "architecture": "base", "files": {
              "model": { "path": "{{path}}" }, "vocab": { "path": "a/vocab.spm.gz" } } } ] } }
            """);

        model.Should().BeNull();
    }

    [Fact]
    public void Find_CollectsChecksumsByShapeNotByFieldName()
    {
        RegistryModel? model = Find($$"""
            { "baseUrl": "https://m.test/r", "models": { "en-ru": [ { "architecture": "base", "files": {
              "model": { "path": "a/model.bin.gz", "anyName": "{{Hash.ToUpperInvariant()}}", "size": "12345", "label": "not a hash" },
              "vocab": { "path": "a/vocab.spm.gz" }
            } } ] } }
            """);

        model!.Files[0].Sha256Hashes.Should().Equal(Hash);
        model.Files[1].Sha256Hashes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("zh-Hans", "en-zh_hans")]
    [InlineData("zh-Hant", "en-zh_hant")]
    [InlineData("zh", "en-zh_hans")] // no plain name: the first variant, alphabetically
    public void Find_LanguageWithScriptVariants_FindsTheVariant(string target, string expectedDirection)
    {
        RegistryModel? model = Find($$"""
            { "baseUrl": "https://m.test/r", "models": {
              "en-zh_hant": [ { "architecture": "base", "files": {{Files("hant")}} } ],
              "en-zh_hans": [ { "architecture": "base", "files": {{Files("hans")}} } ]
            } }
            """, target: target);

        model!.Direction.Should().Be(expectedDirection);
    }

    [Fact]
    public void Find_PlainNameWinsOverAVariant()
    {
        RegistryModel? model = Find($$"""
            { "baseUrl": "https://m.test/r", "models": {
              "en-pt_br": [ { "architecture": "base", "files": {{Files("br")}} } ],
              "en-pt": [ { "architecture": "base", "files": {{Files("pt")}} } ]
            } }
            """, target: "pt");

        model!.Direction.Should().Be("en-pt");
    }

    [Fact]
    public void Find_SourceOtherThanEnglish_IsLookedUpTheSameWay()
    {
        RegistryModel? model = Find($$"""
            { "baseUrl": "https://m.test/r", "models": { "de-en": [ { "architecture": "base", "files": {{Files("de")}} } ] } }
            """, source: "de", target: "en");

        model!.Direction.Should().Be("de-en");
    }

    [Fact]
    public void Find_UnknownDirection_IsNull()
    {
        Find($$"""{ "baseUrl": "https://m.test/r", "models": { "en-de": [ { "architecture": "base", "files": {{Files("de")}} } ] } }""")
            .Should().BeNull();
    }

    [Theory]
    [InlineData("""{ "models": { } }""")] // no baseUrl
    [InlineData("""{ "baseUrl": "not a url", "models": { } }""")]
    [InlineData("""{ "baseUrl": "https://m.test/r" }""")] // no models
    [InlineData("""{ "baseUrl": "https://m.test/r", "models": [] }""")]
    [InlineData("""[]""")]
    public void Find_MalformedRegistry_IsNullNotAnException(string json)
    {
        Find(json).Should().BeNull();
    }

    [Fact]
    public void Find_ReturnsTheBaseUrl()
    {
        RegistryModel? model = Find($$"""{ "baseUrl": "https://m.test/r", "models": { "en-ru": [ { "architecture": "base", "files": {{Files("a")}} } ] } }""");

        model!.BaseUrl.Host.Should().Be("m.test");
    }
}