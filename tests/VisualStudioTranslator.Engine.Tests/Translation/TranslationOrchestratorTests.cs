using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VisualStudioTranslator.Core.Caching;
using VisualStudioTranslator.Core.Documentation;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Providers.Abstractions;
using VisualStudioTranslator.Core.Quality;
using VisualStudioTranslator.Engine.Caching;
using VisualStudioTranslator.Engine.Translation;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.Translation;

public sealed class TranslationOrchestratorTests
{
    private const string Returns = "Returns the number of items.";
    private const string Gets = "Gets the current value.";
    private const string Sets = "Sets the new name.";
    private const string Creates = "Creates a new instance.";
    private const string Removes = "Removes the item from the list.";

    // Every translation here has to pass the real validator for a Russian target, so each is
    // written with the right script, a plausible length, matching punctuation and no
    // placeholder lost.
    private static readonly Dictionary<string, string> Translations = new()
    {
        [Returns] = "Возвращает количество элементов.",
        [Gets] = "Получает текущее значение.",
        [Sets] = "Задаёт новое имя.",
        [Creates] = "Создаёт новый экземпляр.",
        [Removes] = "Удаляет элемент из списка.",
        ["Returns the number of items in ⟦0⟧."] = "Возвращает количество элементов в ⟦0⟧.",
        ["Gets the ⟦0⟧ instance now."] = "Получает ⟦0⟧ экземпляр сейчас.",
    };

    private static readonly LanguagePair EnglishToRussian = LanguagePair.Create("en", "ru")!;

    private static TranslationOrchestrator Orchestrator(ITranslationCache cache) =>
        new(cache, NullLogger<TranslationOrchestrator>.Instance);

    private static TranslationRequest Request(DocumentModel document) =>
        new() { Document = document, Languages = EnglishToRussian };

    // One summary paragraph per sentence, so every sentence is its own segment.
    private static DocumentModel Doc(params string[] sentences) => new()
    {
        Summary = new Section
        {
            Blocks = [.. sentences.Select(sentence => (Block)new Paragraph { Inlines = [new TextRun { Text = sentence }] })],
        },
    };

    // The text of each segment with every non-text inline dropped, which is enough to see what
    // was translated and what was left alone.
    private static List<string> Texts(DocumentModel document) =>
        [.. DocumentSegmenter.Segment(document)
            .Select(segment => string.Concat(segment.Inlines.OfType<TextRun>().Select(run => run.Text)))];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TranslateAsync_TranslatesEverySegmentInOneBatch()
    {
        FakeProvider provider = new(Translations);

        TranslationResult result = await Orchestrator(new MemoryTranslationCache())
            .TranslateAsync(Request(Doc(Returns, Gets)), provider, Token);

        Texts(result.Document).Should().Equal("Возвращает количество элементов.", "Получает текущее значение.");
        result.TranslatedCount.Should().Be(2);
        result.CachedCount.Should().Be(0);
        result.UnchangedCount.Should().Be(0);
        provider.Calls.Should().ContainSingle().Which.Should().Equal(Returns, Gets);
    }

    [Fact]
    public async Task TranslateAsync_SecondRun_IsServedFromTheCache()
    {
        FakeProvider provider = new(Translations);
        TranslationOrchestrator orchestrator = Orchestrator(new MemoryTranslationCache());
        TranslationRequest request = Request(Doc(Returns, Gets));

        await orchestrator.TranslateAsync(request, provider, Token);
        TranslationResult second = await orchestrator.TranslateAsync(request, provider, Token);

        provider.Calls.Should().ContainSingle();
        second.CachedCount.Should().Be(2);
        second.TranslatedCount.Should().Be(0);
        Texts(second.Document).Should().Equal("Возвращает количество элементов.", "Получает текущее значение.");
    }

    [Fact]
    public async Task TranslateAsync_IdenticalSegments_AreTranslatedOnce()
    {
        FakeProvider provider = new(Translations);

        TranslationResult result = await Orchestrator(new MemoryTranslationCache())
            .TranslateAsync(Request(Doc(Returns, Returns)), provider, Token);

        provider.Calls.Should().ContainSingle().Which.Should().Equal(Returns);
        result.TranslatedCount.Should().Be(2);
        Texts(result.Document).Should().Equal("Возвращает количество элементов.", "Возвращает количество элементов.");
    }

    [Fact]
    public async Task TranslateAsync_CodeSpan_IsRestoredIntoTheTranslation()
    {
        DocumentModel document = new()
        {
            Summary = new Section
            {
                Blocks =
                [
                    new Paragraph
                    {
                        Inlines =
                        [
                            new TextRun { Text = "Returns the number of items in " },
                            new CodeSpan { Text = "List<T>" },
                            new TextRun { Text = "." },
                        ],
                    },
                ],
            },
        };

        TranslationResult result = await Orchestrator(new MemoryTranslationCache())
            .TranslateAsync(Request(document), new FakeProvider(Translations), Token);

        Segment translated = DocumentSegmenter.Segment(result.Document).Single();
        translated.Inlines.OfType<CodeSpan>().Single().Text.Should().Be("List<T>");
        Texts(result.Document).Single().Should().Be("Возвращает количество элементов в .");
    }

    [Fact]
    public async Task TranslateAsync_GlossaryTerm_IsKeptAndPartOfTheCacheKey()
    {
        FakeProvider provider = new(Translations);
        MemoryTranslationCache cache = new();
        TranslationOrchestrator orchestrator = Orchestrator(cache);
        DocumentModel document = Doc("Gets the Roslyn instance now.");

        Glossary keepRoslyn = new() { Entries = [Keep("Roslyn")] };
        Glossary keepMore = new() { Entries = [Keep("Roslyn"), Keep("Unrelated")] };

        TranslationResult first = await orchestrator.TranslateAsync(
            Request(document) with { Glossary = keepRoslyn }, provider, Token);

        // Same protected text, different glossary: it must not be served from the cache.
        await orchestrator.TranslateAsync(Request(document) with { Glossary = keepMore }, provider, Token);

        Texts(first.Document).Single().Should().Be("Получает Roslyn экземпляр сейчас.");
        provider.Calls.Should().HaveCount(2);
    }

    [Fact]
    public async Task TranslateAsync_SegmentsWithoutLetters_AreNotSent()
    {
        DocumentModel document = new()
        {
            Summary = new Section
            {
                Blocks =
                [
                    new Paragraph { Inlines = [new TextRun { Text = "42" }] },
                    new Paragraph { Inlines = [new CodeSpan { Text = "x" }] },
                    new Paragraph { Inlines = [new TextRun { Text = Gets }] },
                ],
            },
        };
        FakeProvider provider = new(Translations);

        TranslationResult result = await Orchestrator(new MemoryTranslationCache())
            .TranslateAsync(Request(document), provider, Token);

        provider.Calls.Should().ContainSingle().Which.Should().Equal(Gets);
        result.TranslatedCount.Should().Be(1);
        result.UnchangedCount.Should().Be(2);
        result.SegmentCount.Should().Be(3);
    }

    // --- Batching ---

    [Fact]
    public async Task TranslateAsync_RespectsTheSegmentLimitPerRequest()
    {
        FakeProvider provider = new(Translations) { MaxSegmentsPerRequest = 2 };

        TranslationResult result = await Orchestrator(new MemoryTranslationCache())
            .TranslateAsync(Request(Doc(Returns, Gets, Sets, Creates, Removes)), provider, Token);

        provider.Calls.Select(call => call.Count).Should().Equal(2, 2, 1);
        result.TranslatedCount.Should().Be(5);
    }

    [Fact]
    public async Task TranslateAsync_RespectsTheCharacterLimitPerRequest()
    {
        // 28, 23 and 18 characters: the first cannot share a request with the second (51 > 45),
        // but the second and third fit together (41).
        FakeProvider provider = new(Translations) { MaxCharactersPerRequest = 45 };

        await Orchestrator(new MemoryTranslationCache())
            .TranslateAsync(Request(Doc(Returns, Gets, Sets)), provider, Token);

        provider.Calls.Select(call => call.Count).Should().Equal(1, 2);
    }

    [Fact]
    public async Task TranslateAsync_SegmentOverTheCharacterLimit_IsLeftUntranslated()
    {
        FakeProvider provider = new(Translations) { MaxCharactersPerRequest = 20 };

        TranslationResult result = await Orchestrator(new MemoryTranslationCache())
            .TranslateAsync(Request(Doc(Returns, Gets, Sets)), provider, Token);

        provider.Calls.Should().ContainSingle().Which.Should().Equal(Sets);
        result.TranslatedCount.Should().Be(1);
        result.UnchangedCount.Should().Be(2);
        Texts(result.Document).Should().Equal(Returns, Gets, "Задаёт новое имя.");
    }

    // --- Failures never reach the caller ---

    [Fact]
    public async Task TranslateAsync_RecognizedProviderFailure_KeepsTheOriginalAndStopsAskingTheProvider()
    {
        FakeProvider provider = new(Translations)
        {
            MaxSegmentsPerRequest = 1,
            Throws = new TranslationProviderException(ProviderFailureKind.Unauthorized, "bad key"),
        };

        TranslationResult result = await Orchestrator(new MemoryTranslationCache())
            .TranslateAsync(Request(Doc(Returns, Gets, Sets)), provider, Token);

        Texts(result.Document).Should().Equal(Returns, Gets, Sets);
        result.ProviderFailure.Should().Be(ProviderFailureKind.Unauthorized);
        result.UnchangedCount.Should().Be(3);
        provider.Calls.Should().ContainSingle(); // the later batches were not attempted
    }

    [Fact]
    public async Task TranslateAsync_UnexpectedProviderException_DoesNotEscape()
    {
        FakeProvider provider = new(Translations) { Throws = new InvalidOperationException("boom") };

        TranslationResult result = await Orchestrator(new MemoryTranslationCache())
            .TranslateAsync(Request(Doc(Returns)), provider, Token);

        Texts(result.Document).Should().Equal(Returns);
        result.ProviderFailure.Should().BeNull();
        result.UnchangedCount.Should().Be(1);
    }

    [Fact]
    public async Task TranslateAsync_WrongNumberOfResults_IsAnInvalidResponse()
    {
        FakeProvider provider = new(Translations) { ReturnWrongCount = true };

        TranslationResult result = await Orchestrator(new MemoryTranslationCache())
            .TranslateAsync(Request(Doc(Returns, Gets)), provider, Token);

        Texts(result.Document).Should().Equal(Returns, Gets);
        result.ProviderFailure.Should().Be(ProviderFailureKind.InvalidResponse);
    }

    [Fact]
    public async Task TranslateAsync_ResultThatFailsValidation_KeepsTheOriginalAndIsNotCached()
    {
        DocumentModel document = new()
        {
            Summary = new Section
            {
                Blocks =
                [
                    new Paragraph
                    {
                        Inlines =
                        [
                            new TextRun { Text = "Returns the number of items in " },
                            new CodeSpan { Text = "List<T>" },
                            new TextRun { Text = "." },
                        ],
                    },
                ],
            },
        };

        // The provider drops the placeholder, which would silently lose the code span.
        FakeProvider provider = new(new Dictionary<string, string>
        {
            ["Returns the number of items in ⟦0⟧."] = "Возвращает количество элементов в.",
        });
        MemoryTranslationCache cache = new();

        TranslationResult result = await Orchestrator(cache).TranslateAsync(Request(document), provider, Token);

        Texts(result.Document).Single().Should().Be("Returns the number of items in .");
        DocumentSegmenter.Segment(result.Document).Single().Inlines.OfType<CodeSpan>().Should().ContainSingle();
        result.UnchangedCount.Should().Be(1);
        cache.Count.Should().Be(0);
    }

    [Fact]
    public async Task TranslateAsync_CacheThatThrows_DoesNotBreakTheTranslation()
    {
        TranslationResult result = await Orchestrator(new ThrowingCache())
            .TranslateAsync(Request(Doc(Returns)), new FakeProvider(Translations), Token);

        Texts(result.Document).Should().Equal("Возвращает количество элементов.");
        result.TranslatedCount.Should().Be(1);
    }

    [Fact]
    public async Task TranslateAsync_CancelledToken_PropagatesTheCancellation()
    {
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        Func<Task> act = () => Orchestrator(new MemoryTranslationCache())
            .TranslateAsync(Request(Doc(Returns)), new FakeProvider(Translations), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // --- Refusing to send ---

    [Fact]
    public async Task TranslateAsync_SameSourceAndTarget_ReturnsTheDocumentWithoutCallingTheProvider()
    {
        FakeProvider provider = new(Translations);
        TranslationRequest request = Request(Doc(Returns)) with { Languages = LanguagePair.Create("en", "en")! };

        TranslationResult result = await Orchestrator(new MemoryTranslationCache()).TranslateAsync(request, provider, Token);

        provider.Calls.Should().BeEmpty();
        result.UnchangedCount.Should().Be(1);
    }

    [Fact]
    public async Task TranslateAsync_CloudProviderWithoutConsent_SendsNothing()
    {
        FakeProvider provider = new(Translations) { Kind = ProviderKind.Cloud };

        TranslationResult result = await Orchestrator(new MemoryTranslationCache())
            .TranslateAsync(Request(Doc(Returns)), provider, Token);

        provider.Calls.Should().BeEmpty();
        result.BlockedByCloudConsent.Should().BeTrue();
        Texts(result.Document).Should().Equal(Returns);
    }

    [Fact]
    public async Task TranslateAsync_CloudProviderWithConsent_Translates()
    {
        FakeProvider provider = new(Translations) { Kind = ProviderKind.Cloud };

        TranslationResult result = await Orchestrator(new MemoryTranslationCache())
            .TranslateAsync(Request(Doc(Returns)) with { AllowCloudProvider = true }, provider, Token);

        result.BlockedByCloudConsent.Should().BeFalse();
        result.TranslatedCount.Should().Be(1);
    }

    [Fact]
    public async Task TranslateAsync_UnsupportedLanguagePair_ReportsItWithoutCallingTheProvider()
    {
        FakeProvider provider = new(Translations) { SupportsPairs = false };

        TranslationResult result = await Orchestrator(new MemoryTranslationCache())
            .TranslateAsync(Request(Doc(Returns)), provider, Token);

        provider.Calls.Should().BeEmpty();
        result.ProviderFailure.Should().Be(ProviderFailureKind.UnsupportedLanguagePair);
    }

    private static GlossaryEntry Keep(string term) => new() { Term = term, Kind = GlossaryEntryKind.DoNotTranslate };

    private sealed class FakeProvider(IReadOnlyDictionary<string, string> translations) : ITranslationProvider
    {
        public ProviderKind Kind { get; init; } = ProviderKind.Local;

        public int MaxSegmentsPerRequest { get; init; } = 100;

        public int MaxCharactersPerRequest { get; init; } = 10_000;

        public bool SupportsPairs { get; init; } = true;

        public Exception? Throws { get; init; }

        public bool ReturnWrongCount { get; init; }

        public List<IReadOnlyList<string>> Calls { get; } = [];

        public ProviderInfo Info => new() { Id = "fake", DisplayName = "Fake", Revision = "1", Kind = Kind };

        public ProviderCapabilities Capabilities => new()
        {
            Markup = MarkupSupport.None,
            MaxSegmentsPerRequest = MaxSegmentsPerRequest,
            MaxCharactersPerRequest = MaxCharactersPerRequest,
        };

        public bool Supports(LanguagePair pair) => SupportsPairs;

        public Task<IReadOnlyList<string>> TranslateAsync(
            LanguagePair pair, IReadOnlyList<string> texts, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(texts);

            if (Throws is not null)
            {
                throw Throws;
            }

            if (ReturnWrongCount)
            {
                return Task.FromResult<IReadOnlyList<string>>([]);
            }

            return Task.FromResult<IReadOnlyList<string>>([.. texts.Select(Translate)]);
        }

        private string Translate(string text) =>
            translations.TryGetValue(text, out string? translated)
                ? translated
                : throw new InvalidOperationException($"No fake translation was set up for: {text}");
    }

    private sealed class ThrowingCache : ITranslationCache
    {
        public Task<string?> TryGetAsync(TranslationCacheKey key, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");

        public Task SetAsync(TranslationCacheKey key, string translation, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");
    }
}