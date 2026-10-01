namespace VisualStudioTranslator.Core.Providers.Abstractions;

/// <summary>How much markup a provider can carry through a translation by itself.</summary>
public enum MarkupSupport
{
    /// <summary>
    /// Plain text only. The universal fallback every provider supports, and the one
    /// <see cref="Quality.MarkupProtector"/> implements: markup is replaced by placeholder
    /// tokens before sending and put back afterwards.
    /// </summary>
    None,

    /// <summary>
    /// The provider understands HTML and leaves tags alone. A protection strategy that uses
    /// this is a later addition; declaring it here does not oblige the pipeline to use it.
    /// </summary>
    Html,
}

/// <summary>
/// What a provider can do and where its limits are. Every member is required: a wrong
/// default (say, assuming no batch limit for a cloud service) would not fail loudly, it
/// would surface as rejected requests at runtime, so each provider has to state its own.
/// </summary>
public sealed record ProviderCapabilities
{
    public required MarkupSupport Markup { get; init; }

    /// <summary>Most segments one <see cref="ITranslationProvider.TranslateAsync"/> call accepts. Use <see cref="int.MaxValue"/> for no limit.</summary>
    public required int MaxSegmentsPerRequest { get; init; }

    /// <summary>
    /// Most characters one call accepts, summed over all its segments. A single segment
    /// longer than this cannot be translated by the provider at all. Use
    /// <see cref="int.MaxValue"/> for no limit.
    /// </summary>
    public required int MaxCharactersPerRequest { get; init; }
}