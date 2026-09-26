using Microsoft.Extensions.Logging;

namespace VisualStudioTranslator.Engine.Rpc;

/// <summary>
/// Source-generated log messages for the RPC layer. Defined as extension methods so the
/// message templates are compiled once and their arguments are evaluated only when the
/// corresponding log level is enabled.
/// </summary>
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Handshake from {ProductName} {VisualStudioVersion}, extension {ExtensionVersion}, protocol {ProtocolMajor}.{ProtocolMinor}.")]
    public static partial void Handshake(
        this ILogger logger,
        string productName,
        string visualStudioVersion,
        string extensionVersion,
        int protocolMajor,
        int protocolMinor);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Client protocol major version {ClientMajor} is incompatible with this Engine's major version {ServiceMajor}.")]
    public static partial void IncompatibleClientProtocol(this ILogger logger, int clientMajor, int serviceMajor);

    [LoggerMessage(Level = LogLevel.Information, Message = "Listening on named pipe '{PipeName}'.")]
    public static partial void ListeningOnPipe(this ILogger logger, string pipeName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A client connection on '{PipeName}' ended unexpectedly.")]
    public static partial void ClientConnectionEndedUnexpectedly(this ILogger logger, string pipeName, Exception exception);
}