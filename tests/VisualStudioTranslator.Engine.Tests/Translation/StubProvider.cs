using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Providers.Abstractions;

namespace VisualStudioTranslator.Engine.Tests.Translation;

/// <summary>A provider for tests: answers from a fixed table and records what it was asked.</summary>
internal sealed class StubProvider(
    string id,
    ProviderKind kind = ProviderKind.Local,
    IReadOnlyDictionary<string, string>? translations = null,
    int? qualityTier = null) : ITranslationProvider
{
    public Func<LanguagePair, bool> SupportsPair { get; init; } = _ => true;

    public Exception? Throws { get; init; }

    public List<IReadOnlyList<string>> Calls { get; } = [];

    // A cloud stub is of the cloud tier and a local one of the compact tier, unless a test says otherwise.
    public ProviderInfo Info { get; } = new()
    {
        Id = id,
        DisplayName = id,
        Revision = "1",
        Kind = kind,
        QualityTier = qualityTier ?? (kind == ProviderKind.Cloud ? QualityTiers.Cloud : QualityTiers.Compact),
    };

    public ProviderCapabilities Capabilities { get; } = new()
    {
        Markup = MarkupSupport.None,
        MaxSegmentsPerRequest = 100,
        MaxCharactersPerRequest = 10_000,
    };

    public bool Supports(LanguagePair pair) => SupportsPair(pair);

    public Task<IReadOnlyList<string>> TranslateAsync(
        LanguagePair pair, IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add(texts);

        if (Throws is not null)
        {
            throw Throws;
        }

        return Task.FromResult<IReadOnlyList<string>>([.. texts.Select(Translate)]);
    }

    private string Translate(string text) =>
        translations is not null && translations.TryGetValue(text, out string? translated)
            ? translated
            : throw new InvalidOperationException($"No stub translation was set up for: {text}");
}