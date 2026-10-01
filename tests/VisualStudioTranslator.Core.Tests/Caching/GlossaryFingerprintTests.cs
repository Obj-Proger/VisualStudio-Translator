using AwesomeAssertions;
using VisualStudioTranslator.Core.Caching;
using VisualStudioTranslator.Core.Quality;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Caching;

public sealed class GlossaryFingerprintTests
{
    private static Glossary Of(params GlossaryEntry[] entries) => new() { Entries = entries };

    private static GlossaryEntry Keep(string term) =>
        new() { Term = term, Kind = GlossaryEntryKind.DoNotTranslate };

    private static GlossaryEntry Replace(string term, string? replacement) =>
        new() { Term = term, Kind = GlossaryEntryKind.TranslateAs, Replacement = replacement };

    [Fact]
    public void Compute_NullAndEmptyGlossary_GiveTheSameFingerprint()
    {
        GlossaryFingerprint.Compute(null).Should().Be(GlossaryFingerprint.Compute(Of()));
    }

    [Fact]
    public void Compute_SameEntriesBuiltSeparately_GiveTheSameFingerprint()
    {
        GlossaryFingerprint.Compute(Of(Keep("Roslyn"), Replace("Task", "Задача")))
            .Should().Be(GlossaryFingerprint.Compute(Of(Keep("Roslyn"), Replace("Task", "Задача"))));
    }

    [Fact]
    public void Compute_Value_IsLowercaseHexSha256()
    {
        GlossaryFingerprint.Compute(Of(Keep("Roslyn"))).Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Compute_ChangingTheGlossary_ChangesTheFingerprint()
    {
        Glossary[] variants =
        [
            Of(),
            Of(Keep("Roslyn")),
            Of(Keep("roslyn")),
            Of(Keep("Roslyn"), Keep("Task")),
            Of(Replace("Roslyn", "Рослин")),
            Of(Replace("Roslyn", "Другое")),
        ];

        variants.Select(GlossaryFingerprint.Compute)
            .Distinct()
            .Should().HaveCount(variants.Length);
    }

    [Fact]
    public void Compute_NullReplacementDiffersFromEmptyReplacement()
    {
        GlossaryFingerprint.Compute(Of(Replace("x", null)))
            .Should().NotBe(GlossaryFingerprint.Compute(Of(Replace("x", string.Empty))));
    }

    [Fact]
    public void Compute_EntryOrder_IsPartOfTheFingerprint()
    {
        GlossaryFingerprint.Compute(Of(Keep("a"), Keep("b")))
            .Should().NotBe(GlossaryFingerprint.Compute(Of(Keep("b"), Keep("a"))));
    }
}