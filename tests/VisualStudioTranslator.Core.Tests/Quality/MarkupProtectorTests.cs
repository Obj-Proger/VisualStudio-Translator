using System.Text.RegularExpressions;
using AwesomeAssertions;
using VisualStudioTranslator.Core.Documentation;
using VisualStudioTranslator.Core.Quality;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Quality;

public sealed partial class MarkupProtectorTests
{
    // Mirrors MarkupProtector's own placeholder syntax, so tests can uppercase only the
    // translatable portions of a protected string - simulating a well-behaved provider
    // that leaves placeholder tokens untouched - without corrupting the tokens the way a
    // blind ToUpperInvariant() over the whole string would (":o"/":c" would become
    // ":O"/":C" and stop matching).
    [GeneratedRegex(@"⟦\d+(:[oc])?⟧")]
    private static partial Regex Placeholder();

    private static string SimulateTranslation(string protectedText) =>
        string.Concat(SplitOnPlaceholders(protectedText).Select(part => part.IsPlaceholder ? part.Text : part.Text.ToUpperInvariant()));

    private static IEnumerable<(string Text, bool IsPlaceholder)> SplitOnPlaceholders(string text)
    {
        int position = 0;
        foreach (Match match in Placeholder().Matches(text))
        {
            if (match.Index > position)
            {
                yield return (text[position..match.Index], false);
            }
            yield return (match.Value, true);
            position = match.Index + match.Length;
        }
        if (position < text.Length)
        {
            yield return (text[position..], false);
        }
    }

    [Fact]
    public void Protect_PlainText_ProducesTextWithNoPlaceholders()
    {
        ProtectedSegment result = MarkupProtector.Protect([new TextRun { Text = "Hello world." }]);

        result.Text.Should().Be("Hello world.");
        result.Placeholders.Should().BeEmpty();
    }

    [Fact]
    public void ProtectThenRestore_PlainText_RoundTripsUnchanged()
    {
        // Declared as List<Inline>, not just [...], so the comparison below is against
        // the concrete list type rather than the compiler inferring the element type
        // fresh at the call site - keeps this test's shape identical to the others
        // below, all of which need PreferringRuntimeMemberTypes() for the same reason.
        List<Inline> original = [new TextRun { Text = "Hello world." }];
        ProtectedSegment protectedSegment = MarkupProtector.Protect(original);

        IReadOnlyList<Inline> restored = MarkupProtector.Restore(protectedSegment.Text, protectedSegment.Placeholders);

        // PreferringRuntimeMemberTypes(): Inline is an abstract record with no members
        // of its own - every property lives on a derived type (TextRun.Text,
        // CodeSpan.Text, and so on). Without this option, AwesomeAssertions compares by
        // the *declared* type (Inline) and finds nothing to compare at all. Renamed
        // from FluentAssertions' RespectingRuntimeTypes in 8.0 (AwesomeAssertions 9.x
        // carries the new name).
        restored.Should().BeEquivalentTo(original, options => options.PreferringRuntimeMemberTypes());
    }

    [Fact]
    public void ProtectThenRestore_CodeSpan_IsPreservedVerbatimAndSurroundingTextIsTranslated()
    {
        IReadOnlyList<Inline> original =
        [
            new TextRun { Text = "call " },
            new CodeSpan { Text = "DoWork()" },
            new TextRun { Text = " first." },
        ];

        ProtectedSegment protectedSegment = MarkupProtector.Protect(original);
        string translated = SimulateTranslation(protectedSegment.Text);
        IReadOnlyList<Inline> restored = MarkupProtector.Restore(translated, protectedSegment.Placeholders);

        List<Inline> expected =
        [
            new TextRun { Text = "CALL " },
            new CodeSpan { Text = "DoWork()" },
            new TextRun { Text = " FIRST." },
        ];
        restored.Should().BeEquivalentTo(expected, options => options.PreferringRuntimeMemberTypes());
    }

    [Fact]
    public void ProtectThenRestore_SelfClosingRef_IsPreservedAsASingleOpaqueNode()
    {
        IReadOnlyList<Inline> original =
        [
            new TextRun { Text = "see " },
            new Ref { Kind = RefKind.Cref, Target = "T:System.String" },
        ];

        ProtectedSegment protectedSegment = MarkupProtector.Protect(original);
        string translated = SimulateTranslation(protectedSegment.Text);
        IReadOnlyList<Inline> restored = MarkupProtector.Restore(translated, protectedSegment.Placeholders);

        List<Inline> expected =
        [
            new TextRun { Text = "SEE " },
            new Ref { Kind = RefKind.Cref, Target = "T:System.String" },
        ];
        restored.Should().BeEquivalentTo(expected, options => options.PreferringRuntimeMemberTypes());
    }

    [Fact]
    public void ProtectThenRestore_RefWithDisplayContent_TranslatesDisplayTextButKeepsTarget()
    {
        IReadOnlyList<Inline> original =
        [
            new Ref
            {
                Kind = RefKind.Cref,
                Target = "T:System.String",
                DisplayContent = [new TextRun { Text = "the string type" }],
            },
        ];

        ProtectedSegment protectedSegment = MarkupProtector.Protect(original);
        string translated = SimulateTranslation(protectedSegment.Text);
        IReadOnlyList<Inline> restored = MarkupProtector.Restore(translated, protectedSegment.Placeholders);

        restored.Should().BeEquivalentTo(
        [
            new Ref
            {
                Kind = RefKind.Cref,
                Target = "T:System.String",
                DisplayContent = [new TextRun { Text = "THE STRING TYPE" }],
            },
        ]);
    }

    [Fact]
    public void ProtectThenRestore_Emphasis_TranslatesInnerTextAndKeepsTheWrapper()
    {
        IReadOnlyList<Inline> original =
        [
            new TextRun { Text = "This is " },
            new EmphasisRun { Content = [new TextRun { Text = "important" }] },
            new TextRun { Text = "." },
        ];

        ProtectedSegment protectedSegment = MarkupProtector.Protect(original);
        string translated = SimulateTranslation(protectedSegment.Text);
        IReadOnlyList<Inline> restored = MarkupProtector.Restore(translated, protectedSegment.Placeholders);

        List<Inline> expected =
        [
            new TextRun { Text = "THIS IS " },
            new EmphasisRun { Content = [new TextRun { Text = "IMPORTANT" }] },
            new TextRun { Text = "." },
        ];
        restored.Should().BeEquivalentTo(expected, options => options.PreferringRuntimeMemberTypes());
    }

    [Fact]
    public void ProtectThenRestore_EmphasisNestedInsideRefDisplayContent_RoundTripsBothLevels()
    {
        IReadOnlyList<Inline> original =
        [
            new Ref
            {
                Kind = RefKind.Cref,
                Target = "T:System.String",
                DisplayContent =
                [
                    new TextRun { Text = "the " },
                    new EmphasisRun { Content = [new TextRun { Text = "string" }] },
                    new TextRun { Text = " type" },
                ],
            },
        ];

        ProtectedSegment protectedSegment = MarkupProtector.Protect(original);
        string translated = SimulateTranslation(protectedSegment.Text);
        IReadOnlyList<Inline> restored = MarkupProtector.Restore(translated, protectedSegment.Placeholders);

        restored.Should().BeEquivalentTo(
        [
            new Ref
            {
                Kind = RefKind.Cref,
                Target = "T:System.String",
                DisplayContent =
                [
                    new TextRun { Text = "THE " },
                    new EmphasisRun { Content = [new TextRun { Text = "STRING" }] },
                    new TextRun { Text = " TYPE" },
                ],
            },
        ]);
    }

    [Fact]
    public void Restore_UnknownNodePlaceholderId_IsDroppedNotThrown()
    {
        IReadOnlyList<Inline> result = MarkupProtector.Restore(
            "before ⟦99⟧ after", new Dictionary<int, ProtectedPlaceholder>());

        result.Should().BeEquivalentTo(
        [
            new TextRun { Text = "before " },
            new TextRun { Text = " after" },
        ]);
    }

    [Fact]
    public void Restore_OrphanCloseMarkerWithNothingOpen_IsDroppedNotThrown()
    {
        IReadOnlyList<Inline> result = MarkupProtector.Restore(
            "before ⟦0:c⟧ after", new Dictionary<int, ProtectedPlaceholder>());

        result.Should().BeEquivalentTo(
        [
            new TextRun { Text = "before " },
            new TextRun { Text = " after" },
        ]);
    }

    [Fact]
    public void Restore_UnclosedWrapperAtEndOfText_ClosesUsingWhateverWasCollected()
    {
        Dictionary<int, ProtectedPlaceholder> placeholders = new()
        {
            [0] = new ProtectedPlaceholder { Kind = PlaceholderKind.EmphasisWrapper },
        };

        IReadOnlyList<Inline> result = MarkupProtector.Restore("⟦0:o⟧never closed", placeholders);

        result.Should().BeEquivalentTo(
        [
            new EmphasisRun { Content = [new TextRun { Text = "never closed" }] },
        ]);
    }

    [Theory]
    [InlineData("⟦99999999999999999999⟧")] // does not fit an Int32
    [InlineData("⟦\u0663⟧")] // Arabic-Indic digit three: \d matches it, int.Parse does not
    [InlineData("⟦\u0663:o⟧x⟦\u0663:c⟧")]
    public void Restore_PlaceholderIdThatIsNotAnInt32_DoesNotThrow(string translated)
    {
        Dictionary<int, ProtectedPlaceholder> placeholders = new()
        {
            [3] = new ProtectedPlaceholder { Kind = PlaceholderKind.Node, Node = new CodeSpan { Text = "x" } },
        };

        Action act = () => MarkupProtector.Restore(translated, placeholders);

        act.Should().NotThrow();
    }

    [Fact]
    public void Restore_PlaceholderIdThatIsNotAnInt32_DropsTokenAndKeepsSurroundingText()
    {
        IReadOnlyList<Inline> result = MarkupProtector.Restore("see ⟦\u0663⟧ now", new Dictionary<int, ProtectedPlaceholder>());

        result.Should().BeEquivalentTo([new TextRun { Text = "see  now" }]);
    }
}