namespace VisualStudioTranslator.Core.Rpc;

/// <summary>
/// Where installing a language model for one direction stands. Sent as a number, so values are
/// never renumbered or reused: add new ones at the end.
/// </summary>
public enum ModelInstallState
{
    /// <summary>A model for this direction is installed and ready.</summary>
    Installed = 0,

    /// <summary>
    /// No model is installed. Nothing has been looked up on the network: this answer costs
    /// nothing and sends nothing, so it can be asked for before the user has agreed to anything.
    /// </summary>
    NotInstalled = 1,

    /// <summary>The model server has no model for this direction.</summary>
    Unavailable = 2,

    /// <summary>A model is being downloaded and installed right now.</summary>
    Installing = 3,

    /// <summary>The last attempt failed; <see cref="ModelInstallStatus.Detail"/> says why. Starting again retries.</summary>
    Failed = 4,

    /// <summary>The model server could not be reached, so it is not known whether a model exists.</summary>
    RegistryUnreachable = 5,
}

public sealed record ModelInstallStatus
{
    public required ModelInstallState State { get; init; }

    /// <summary>The size of the download in bytes while installing, or 0 when not known.</summary>
    public long TotalBytes { get; init; }

    /// <summary>How much of it has arrived.</summary>
    public long DoneBytes { get; init; }

    /// <summary>
    /// The host a model comes from. Present before anything is downloaded, so the user can be
    /// told where to before they agree.
    /// </summary>
    public string? SourceHost { get; init; }

    /// <summary>A short, user-facing reason for <see cref="ModelInstallState.Failed"/> and the other unsuccessful states. Never contains any of the user's text.</summary>
    public string? Detail { get; init; }
}