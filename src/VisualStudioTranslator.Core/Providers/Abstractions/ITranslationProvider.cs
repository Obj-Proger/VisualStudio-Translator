using VisualStudioTranslator.Core.Languages;

namespace VisualStudioTranslator.Core.Providers.Abstractions;

/// <summary>
/// A translation backend: the local Bergamot engine, or a cloud service. This is the only
/// seam between the pipeline and a specific provider, so everything the pipeline needs to
/// know about one is on this interface.
/// <para>
/// A provider translates text and nothing else. Protecting markup, applying the glossary,
/// validating the result, caching and choosing between providers all happen outside it.
/// </para>
/// </summary>
public interface ITranslationProvider
{
    ProviderInfo Info { get; }

    ProviderCapabilities Capabilities { get; }

    /// <summary>Whether this provider can translate in the given direction right now.</summary>
    bool Supports(LanguagePair pair);

    /// <summary>
    /// Translates <paramref name="texts"/> and returns one result per input, in the same order.
    /// <list type="bullet">
    /// <item>The texts may contain placeholder tokens like <c>⟦0⟧</c>, which must come back
    /// unchanged. A provider cannot guarantee that, so
    /// <see cref="Quality.TranslationValidator"/> checks every result.</item>
    /// <item>An empty input returns an empty list without contacting anything.</item>
    /// <item>The call stays within <see cref="ProviderCapabilities.MaxSegmentsPerRequest"/>
    /// and <see cref="ProviderCapabilities.MaxCharactersPerRequest"/>; splitting larger work
    /// is the caller's job.</item>
    /// <item>Failure is reported as <see cref="TranslationProviderException"/>, cancellation
    /// as <see cref="OperationCanceledException"/>; nothing else escapes.</item>
    /// <item>The texts are the user's code comments: an implementation must not log or keep
    /// them beyond the call.</item>
    /// <item>It may be called from several threads at once.</item>
    /// </list>
    /// </summary>
    Task<IReadOnlyList<string>> TranslateAsync(
        LanguagePair pair, IReadOnlyList<string> texts, CancellationToken cancellationToken);
}