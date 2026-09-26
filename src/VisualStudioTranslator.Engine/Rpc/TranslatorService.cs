using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VisualStudioTranslator.Core.Rpc;

namespace VisualStudioTranslator.Engine.Rpc;

/// <summary>
/// The Engine-side implementation of <see cref="ITranslatorService"/>, exposed to Vsix
/// clients over the named pipe RPC server.
/// </summary>
internal sealed class TranslatorService(ILogger<TranslatorService> logger) : ITranslatorService
{
    public Task<ServiceInfo> HandshakeAsync(ClientInfo client, CancellationToken cancellationToken)
    {
        logger.Handshake(
            client.ProductName,
            client.VisualStudioVersion,
            client.ExtensionVersion,
            client.ProtocolMajor,
            client.ProtocolMinor);

        if (!ProtocolVersion.IsCompatibleWith(client.ProtocolMajor))
        {
            logger.IncompatibleClientProtocol(client.ProtocolMajor, ProtocolVersion.Major);
        }

        ServiceInfo info = new()
        {
            ServiceVersion = GetServiceVersion(),
            ProtocolMajor = ProtocolVersion.Major,
            ProtocolMinor = ProtocolVersion.Minor,
        };

        return Task.FromResult(info);
    }

    private static string GetServiceVersion() =>
        typeof(TranslatorService).Assembly.GetName().Version?.ToString() ?? "0.0.0";
}