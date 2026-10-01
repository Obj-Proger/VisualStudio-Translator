namespace VisualStudioTranslator.Core.Providers.Abstractions;

/// <summary>Where a provider does its work, which decides whether the user's text leaves the machine.</summary>
public enum ProviderKind
{
    /// <summary>Runs on this machine; text never leaves it.</summary>
    Local,

    /// <summary>
    /// Sends text to a remote service. The orchestrator must refuse to use such a provider
    /// without the user's explicit consent.
    /// </summary>
    Cloud,
}

/// <summary>Identity of a provider, as it appears in settings, logs and cache keys.</summary>
public sealed record ProviderInfo
{
    /// <summary>
    /// Stable, lowercase identifier such as "bergamot". Part of every cache key and of the
    /// saved settings, so it must never change once released.
    /// </summary>
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>
    /// Changes whenever the provider could return different output for the same input: a
    /// new model version, a changed API version. Cached translations are keyed on it, so
    /// bumping it retires every entry the old revision produced.
    /// </summary>
    public required string Revision { get; init; }

    public required ProviderKind Kind { get; init; }
}