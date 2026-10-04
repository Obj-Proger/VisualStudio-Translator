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

/// <summary>
/// How good a provider's translations are, relative to other providers. Only the order matters:
/// it decides whether a translation cached by one provider may stand in for another's, and a
/// better one is never replaced by a worse one.
/// </summary>
public static class QualityTiers
{
    /// <summary>Compact on-device models: fast and private, but a plain translation of technical text.</summary>
    public const int Compact = 1;

    /// <summary>Cloud translation services, used when the user chooses higher quality over keeping text local.</summary>
    public const int Cloud = 2;
}

/// <summary>Identity of a provider, as it appears in settings, logs and cached entries.</summary>
public sealed record ProviderInfo
{
    /// <summary>
    /// Stable, lowercase identifier such as "azure". Stored with every cached translation and in
    /// the saved settings, so it must never change once released.
    /// </summary>
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>
    /// Changes whenever the provider could return different output for the same input: a
    /// new model version, a changed API version. A cached translation made by the same provider
    /// under an older revision is no longer used.
    /// </summary>
    public required string Revision { get; init; }

    public required ProviderKind Kind { get; init; }

    /// <summary>Higher is better; see <see cref="QualityTiers"/>. Every provider states its own.</summary>
    public required int QualityTier { get; init; }
}