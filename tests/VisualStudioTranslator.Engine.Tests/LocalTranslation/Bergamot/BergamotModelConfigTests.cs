using AwesomeAssertions;
using VisualStudioTranslator.Engine.LocalTranslation.Bergamot;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.LocalTranslation.Bergamot;

public sealed class BergamotModelConfigTests
{
    private static readonly string[] SeparateVocabularies =
    [
        "model.enru.intgemm.alphas.bin",
        "srcvocab.enru.spm",
        "trgvocab.enru.spm",
        "lex.50.50.enru.s2t.bin",
    ];

    [Fact]
    public void Build_CompleteModel_ListsEveryFileAndTheMatchingPrecision()
    {
        string? config = BergamotModelConfig.Build(SeparateVocabularies);

        config.Should().NotBeNull();
        config.Should().StartWith("relative-paths: true\n");
        config.Should().Contain("models:\n- model.enru.intgemm.alphas.bin\n");
        config.Should().Contain("vocabs:\n- srcvocab.enru.spm\n- trgvocab.enru.spm\n");
        config.Should().Contain("shortlist:\n- lex.50.50.enru.s2t.bin\n- false\n");
        config.Should().EndWith("gemm-precision: int8shiftAlphaAll\n");
    }

    [Fact]
    public void Build_ModelWithoutAlphasInItsName_UsesTheOtherPrecision()
    {
        string? config = BergamotModelConfig.Build(["model.enru.intgemm.bin", "vocab.enru.spm"]);

        config.Should().EndWith("gemm-precision: int8shiftAll\n");
    }

    [Fact]
    public void Build_SharedVocabulary_IsListedForBothSides()
    {
        string? config = BergamotModelConfig.Build(["model.enru.intgemm.alphas.bin", "vocab.enru.spm"]);

        config.Should().Contain("vocabs:\n- vocab.enru.spm\n- vocab.enru.spm\n");
    }

    [Fact]
    public void Build_NoLexicalShortlist_LeavesTheSectionOut()
    {
        string? config = BergamotModelConfig.Build(["model.enru.intgemm.alphas.bin", "vocab.enru.spm"]);

        config.Should().NotContain("shortlist");
    }

    [Fact]
    public void Build_MatchesFileNamesIgnoringCase()
    {
        string? config = BergamotModelConfig.Build(["MODEL.enru.BIN", "VOCAB.enru.SPM"]);

        config.Should().Contain("- MODEL.enru.BIN");
    }

    [Theory]
    [InlineData("vocab.enru.spm")] // no model
    [InlineData("model.enru.intgemm.alphas.bin")] // no vocabulary
    [InlineData("model.enru.intgemm.alphas.bin", "srcvocab.enru.spm")] // only one side of the vocabulary
    public void Build_IncompleteModel_ReturnsNull(params string[] files)
    {
        BergamotModelConfig.Build(files).Should().BeNull();
    }

    [Fact]
    public void Build_NoFiles_ReturnsNull()
    {
        BergamotModelConfig.Build([]).Should().BeNull();
    }
}