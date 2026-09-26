namespace VisualStudioTranslator.Core.Rpc;

/// <summary>
/// Sent by the Vsix client to the Engine as the first call after the RPC channel is
/// established, so the Engine knows who it is talking to.
/// </summary>
public sealed record ClientInfo
{
    /// <summary>Human-readable Visual Studio product name, e.g. "Visual Studio 2026".</summary>
    public required string ProductName { get; init; }

    /// <summary>Full Visual Studio version, e.g. "18.0.1234.5".</summary>
    public required string VisualStudioVersion { get; init; }

    /// <summary>Version of the Vsix extension making the call.</summary>
    public required string ExtensionVersion { get; init; }

    /// <summary>Wire protocol major version the client was built against.</summary>
    public required int ProtocolMajor { get; init; }

    /// <summary>Wire protocol minor version the client was built against.</summary>
    public required int ProtocolMinor { get; init; }
}