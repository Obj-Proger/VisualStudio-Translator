namespace VisualStudioTranslator.Engine.Lifecycle;

/// <summary>How long the Engine lives without anyone to serve.</summary>
public sealed record EngineLifetimeOptions
{
    /// <summary>
    /// How long the Engine stays up with no client connected before it shuts itself down.
    /// Long enough to ride out Visual Studio restarting, short enough that closing it
    /// does not leave a process behind for long.
    /// </summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>How often the Engine checks whether it has been idle long enough.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);
}