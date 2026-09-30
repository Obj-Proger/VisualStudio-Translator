using AwesomeAssertions;
using VisualStudioTranslator.Core.Documentation;
using VisualStudioTranslator.Core.Quality;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Quality;

public sealed class GlossaryProtectorTests
{
    [Fact]
    public void Apply_EmptyGlossary_ReturnsSameSegmentInstance()
    {
        ProtectedSegment segment = MarkupProtector.Protect([new TextRun { Text = "Use Task.Run to start work." }]);

        ProtectedSegment result = GlossaryProtector.Apply(segment, new Glossary());

        // Reference equality is the intended fast path for the common "no glossary
        // configured" case, not an accident of record equality on a Dictionary
        // property (which record-generated Equals compares by reference anyway).
        result.Should().BeSameAs(segment);
    }

    [Fact]
    public void Apply_DoNotTranslateTerm_ProtectsItAndLeavesRestOfTextTranslatable()
    {
        ProtectedSegment segment = MarkupProtector.Protect([new TextRun { Text = "Call Task.Run to start work." }]);
        Glossary glossary = new()
        {
            Entries = [new GlossaryEntry { Term = "Task.Run", Kind = GlossaryEntryKind.DoNotTranslate }],
        };

        ProtectedSegment result = GlossaryProtector.Apply(segment, glossary);
        IReadOnlyList<Inline> restored = MarkupProtector.Restore(result.Text, result.Placeholders);

        restored.Should().BeEquivalentTo(
            new List<Inline>
            {
                new TextRun { Text = "Call " },
                new TextRun { Text = "Task.Run" },
                new TextRun { Text = " to start work." },
            },
            options => options.PreferringRuntimeMemberTypes());
    }

    [Fact]
    public void Apply_TranslateAsTerm_ReplacesWithConfiguredReplacement()
    {
        ProtectedSegment segment = MarkupProtector.Protect([new TextRun { Text = "Open the Solution Explorer now." }]);
        Glossary glossary = new()
        {
            Entries =
            [
                new GlossaryEntry { Term = "Solution Explorer", Kind = GlossaryEntryKind.TranslateAs, Replacement = "Обозреватель решений" },
            ],
        };

        ProtectedSegment result = GlossaryProtector.Apply(segment, glossary);
        IReadOnlyList<Inline> restored = MarkupProtector.Restore(result.Text, result.Placeholders);

        restored.Should().BeEquivalentTo(
            new List<Inline>
            {
                new TextRun { Text = "Open the " },
                new TextRun { Text = "Обозреватель решений" },
                new TextRun { Text = " now." },
            },
            options => options.PreferringRuntimeMemberTypes());
    }

    [Fact]
    public void Apply_TermMatchIsCaseInsensitiveButOriginalCasingIsPreservedForDoNotTranslate()
    {
        ProtectedSegment segment = MarkupProtector.Protect([new TextRun { Text = "call TASK.RUN please." }]);
        Glossary glossary = new()
        {
            Entries = [new GlossaryEntry { Term = "Task.Run", Kind = GlossaryEntryKind.DoNotTranslate }],
        };

        ProtectedSegment result = GlossaryProtector.Apply(segment, glossary);
        IReadOnlyList<Inline> restored = MarkupProtector.Restore(result.Text, result.Placeholders);

        ((TextRun)restored[1]).Text.Should().Be("TASK.RUN");
    }

    [Fact]
    public void Apply_TermInsideAnotherWord_IsNotMatched()
    {
        ProtectedSegment segment = MarkupProtector.Protect([new TextRun { Text = "MultiTasking is different." }]);
        Glossary glossary = new()
        {
            Entries = [new GlossaryEntry { Term = "Task", Kind = GlossaryEntryKind.DoNotTranslate }],
        };

        ProtectedSegment result = GlossaryProtector.Apply(segment, glossary);

        result.Text.Should().Be(segment.Text);
        result.Placeholders.Should().BeEmpty();
    }

    [Fact]
    public void Apply_TermEndingInSymbol_IsStillCorrectlyBoundedNextToPunctuation()
    {
        ProtectedSegment segment = MarkupProtector.Protect([new TextRun { Text = "Written in C#, mostly." }]);
        Glossary glossary = new()
        {
            Entries = [new GlossaryEntry { Term = "C#", Kind = GlossaryEntryKind.DoNotTranslate }],
        };

        ProtectedSegment result = GlossaryProtector.Apply(segment, glossary);
        IReadOnlyList<Inline> restored = MarkupProtector.Restore(result.Text, result.Placeholders);

        restored.Should().BeEquivalentTo(
            new List<Inline>
            {
                new TextRun { Text = "Written in " },
                new TextRun { Text = "C#" },
                new TextRun { Text = ", mostly." },
            },
            options => options.PreferringRuntimeMemberTypes());
    }

    [Fact]
    public void Apply_LongerTermTakesPrecedenceOverAShorterOverlappingTerm()
    {
        ProtectedSegment segment = MarkupProtector.Protect([new TextRun { Text = "Use the Task Scheduler here." }]);
        Glossary glossary = new()
        {
            Entries =
            [
                new GlossaryEntry { Term = "Task", Kind = GlossaryEntryKind.DoNotTranslate },
                new GlossaryEntry { Term = "Task Scheduler", Kind = GlossaryEntryKind.DoNotTranslate },
            ],
        };

        ProtectedSegment result = GlossaryProtector.Apply(segment, glossary);
        IReadOnlyList<Inline> restored = MarkupProtector.Restore(result.Text, result.Placeholders);

        ((TextRun)restored[1]).Text.Should().Be("Task Scheduler");
    }

    [Fact]
    public void Apply_DoesNotMatchInsideAnExistingMarkupPlaceholder()
    {
        ProtectedSegment segment = MarkupProtector.Protect(
            [
                new TextRun { Text = "See " },
                new CodeSpan { Text = "code" },
                new TextRun { Text = " for code." },
            ]);
        Glossary glossary = new()
        {
            Entries = [new GlossaryEntry { Term = "code", Kind = GlossaryEntryKind.TranslateAs, Replacement = "КОД" }],
        };

        ProtectedSegment result = GlossaryProtector.Apply(segment, glossary);
        IReadOnlyList<Inline> restored = MarkupProtector.Restore(result.Text, result.Placeholders);

        restored.Should().BeEquivalentTo(
            new List<Inline>
            {
                new TextRun { Text = "See " },
                new CodeSpan { Text = "code" },
                new TextRun { Text = " for " },
                new TextRun { Text = "КОД" },
                new TextRun { Text = "." },
            },
            options => options.PreferringRuntimeMemberTypes());
    }

    [Fact]
    public void Apply_MultipleOccurrencesOfSameTerm_AreEachProtectedSeparately()
    {
        ProtectedSegment segment = MarkupProtector.Protect([new TextRun { Text = "Task waits for Task." }]);
        Glossary glossary = new()
        {
            Entries = [new GlossaryEntry { Term = "Task", Kind = GlossaryEntryKind.DoNotTranslate }],
        };

        ProtectedSegment result = GlossaryProtector.Apply(segment, glossary);

        result.Placeholders.Should().HaveCount(2);
    }

    [Fact]
    public void Apply_MissingReplacementForTranslateAs_FallsBackToTheTermItself()
    {
        ProtectedSegment segment = MarkupProtector.Protect([new TextRun { Text = "Use Widget now." }]);
        Glossary glossary = new()
        {
            Entries = [new GlossaryEntry { Term = "Widget", Kind = GlossaryEntryKind.TranslateAs, Replacement = null }],
        };

        ProtectedSegment result = GlossaryProtector.Apply(segment, glossary);
        IReadOnlyList<Inline> restored = MarkupProtector.Restore(result.Text, result.Placeholders);

        ((TextRun)restored[1]).Text.Should().Be("Widget");
    }
}