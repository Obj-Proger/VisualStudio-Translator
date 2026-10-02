namespace VisualStudioTranslator.Core.Providers.Abstractions;

/// <summary>
/// Why a provider call failed, in terms of what the caller should do next. Providers map
/// their own errors (HTTP statuses, native engine codes) onto these, so the orchestrator
/// never has to know how any particular provider reports trouble.
/// Sent over the RPC channel as a number: do not renumber, add new members at the end
/// </summary>
public enum ProviderFailureKind
{
    /// <summary>Temporary: no network, service down, model still loading. Worth a retry or another provider.</summary>
    Unavailable = 0,

    /// <summary>The credentials are missing or rejected. Retrying cannot help; the user has to fix settings.</summary>
    Unauthorized = 1,

    /// <summary>A rate limit or spending quota was hit. Back off, and let the user know.</summary>
    QuotaExceeded = 2,

    /// <summary>This provider cannot translate the requested direction. Try another provider.</summary>
    UnsupportedLanguagePair = 3,

    /// <summary>The provider answered, but not with something usable: malformed body, wrong number of results.</summary>
    InvalidResponse = 4,
}

/// <summary>
/// The one exception type providers throw for a failed translation. A message must
/// describe the failure without quoting the text being translated, so it is always safe to
/// log.
/// </summary>
public sealed class TranslationProviderException(ProviderFailureKind kind, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public ProviderFailureKind Kind { get; } = kind;
}