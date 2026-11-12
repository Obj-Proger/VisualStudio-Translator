namespace VisualStudioTranslator.Engine.LocalTranslation.Llama;

/// <summary>What the model server is started with, and how long it is given.</summary>
internal sealed record LlamaServerOptions
{
    /// <summary>Full path of <c>llama-server.exe</c>.</summary>
    public required string ExecutablePath { get; init; }

    /// <summary>Full path of the model file the server loads.</summary>
    public required string ModelPath { get; init; }

    /// <summary>
    /// How many tokens the model can hold at once, instruction, segment and answer together. A
    /// documentation paragraph is a few hundred; the memory the server takes grows with this number.
    /// </summary>
    public int ContextSize { get; init; } = 4096;

    /// <summary>How many of the model's layers go to the graphics card. Zero keeps everything on the processor.</summary>
    public int GpuLayers { get; init; }

    /// <summary>
    /// How long the server may take from being started to answering. Nearly all of it is reading the
    /// model from disk, which takes seconds from a solid-state drive and far longer from a slow one.
    /// </summary>
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>How often a starting server is asked whether it is ready.</summary>
    public TimeSpan HealthPollInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>How long one such question may take before it counts as "not yet".</summary>
    public TimeSpan HealthProbeTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long one request to the server may take. Generous, because a long paragraph on a slow
    /// processor takes a while, and giving up early would only throw the work away.
    /// </summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long the server is kept after the last request before it is stopped to give its memory back.
    /// Long enough to span a pause in reading code, short enough not to hold gigabytes for an afternoon.
    /// </summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(10);
}