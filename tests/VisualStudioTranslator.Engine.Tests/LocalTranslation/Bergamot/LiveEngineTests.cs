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
}