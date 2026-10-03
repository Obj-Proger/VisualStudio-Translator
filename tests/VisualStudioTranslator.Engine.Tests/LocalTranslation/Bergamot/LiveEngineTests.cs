using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Quality;
using VisualStudioTranslator.Engine.LocalTranslation;
using VisualStudioTranslator.Engine.LocalTranslation.Bergamot;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.LocalTranslation.Bergamot;

/// <summary>
/// Runs the real engine on a real model, when one is installed. It answers the question no
/// other test can: does the neural model carry the placeholder tokens through a translation?
/// If this fails, the failure message shows what the engine actually returned.
/// </summary>
public sealed class LiveEngineTests
{
    private static readonly LanguagePair EnglishToRussian = LanguagePair.Create("en", "ru")!;

    [Fact]
    public async Task EnglishToRussian_KeepsPlaceholdersAndPassesValidation()
    {
        LocalModelStore store = new(LocalModelStore.DefaultRoot);
        BlockingServiceTranslatorFactory factory = new();

        if (!factory.IsModelPresent(store.DirectoryFor(EnglishToRussian)))
        {
            Assert.Skip("No en-ru model is installed under " + store.DirectoryFor(EnglishToRussian));
        }

        using LocalTranslationProvider provider = new(store, factory, NullLogger<LocalTranslationProvider>.Instance);

        string[] sources =
        [
            "Returns the number of items in ⟦0⟧.",
            "Use ⟦0:o⟧the cache⟦0:c⟧ to avoid repeated calls to ⟦1⟧.",
            "Gets the current value.",
        ];

        IReadOnlyList<string> translated = await provider.TranslateAsync(
            EnglishToRussian, sources, TestContext.Current.CancellationToken);

        TranslationValidationOptions options = TranslationValidationOptions.ForTarget("ru");

        for (int i = 0; i < sources.Length; i++)
        {
            ProtectedSegment segment = new() { Text = sources[i], Placeholders = new Dictionary<int, ProtectedPlaceholder>() };
            TranslationValidationResult result = TranslationValidator.Validate(segment, translated[i], options);

            result.Issues.Select(issue => issue.Kind).Should().BeEmpty(
                $"the engine turned \"{sources[i]}\" into \"{translated[i]}\"");
        }
    }

    [Fact]
    public async Task EnglishToRussian_WritesRawOutputsForInspection()
    {
        LocalModelStore store = new(LocalModelStore.DefaultRoot);
        BlockingServiceTranslatorFactory factory = new();

        if (!factory.IsModelPresent(store.DirectoryFor(EnglishToRussian)))
        {
            Assert.Skip("No en-ru model is installed under " + store.DirectoryFor(EnglishToRussian));
        }

        using LocalTranslationProvider provider = new(store, factory, NullLogger<LocalTranslationProvider>.Instance);

        string[] sources =
        [
            "A per-location price override for a service. The row's existence is the override — application code falls back to ⟦0⟧ when no override exists for a given (location, service) pair.",
            "Represents the outcome of an operation that does not return a value. Use ⟦0⟧ when the operation produces a value on success.",
            "Prefer returning ⟦0⟧ over throwing exceptions for expected domain errors. Reserve exceptions for truly exceptional, unrecoverable situations.",
            "Defines a generalized method that a value type or class implements to create a type-specific method for determining equality of instances.",
            "A one-off override to an employee's recurring schedule on a specific date — either a full day off or hours different from the usual recurring pattern.",
        ];

        IReadOnlyList<string> raw = await provider.TranslateAsync(
            EnglishToRussian, sources, TestContext.Current.CancellationToken);

        List<string> lines = [];
        for (int i = 0; i < sources.Length; i++)
        {
            // Everything that is neither ASCII nor Cyrillic, as code points: this is what shows whether
            // a stray "]" is really a bracket or a different character that merely looks like one.
            string odd = string.Join(" ", raw[i].Where(c => c > 127 && (c < 0x400 || c > 0x4FF)).Select(c => $"U+{(int)c:X4}"));

            lines.Add("SOURCE: " + sources[i]);
            lines.Add("RAW:    " + raw[i]);
            lines.Add("ODD:    " + odd);
            lines.Add(string.Empty);
        }

        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "vst-live-output.txt"), lines);
    }
}