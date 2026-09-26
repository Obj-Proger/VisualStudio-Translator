namespace VisualStudioTranslator.Core.Rpc;

/// <summary>
/// Returned by the Engine in response to <see cref="ITranslatorService.HandshakeAsync"/>.
/// </summary>
public sealed record ServiceInfo
{
    /// <summary>Informational version of the running Engine build, e.g. "0.1.0+abc1234".</summary>
    public required string ServiceVersion { get; init; }

    /// <summary>Wire protocol major version implemented by the Engine.</summary>
    public required int ProtocolMajor { get; init; }

    /// <summary>Wire protocol minor version implemented by the Engine.</summary>
    public required int ProtocolMinor { get; init; }
}