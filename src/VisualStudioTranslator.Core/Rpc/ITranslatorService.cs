using System.Threading;
using System.Threading.Tasks;

namespace VisualStudioTranslator.Core.Rpc;

/// <summary>
/// The RPC contract exposed by the Engine process over the named pipe. Implemented by
/// the Engine and consumed by the Vsix client through a StreamJsonRpc proxy.
/// </summary>
public interface ITranslatorService
{
    /// <summary>
    /// The first call a client makes after connecting. Lets both sides confirm they
    /// speak a compatible protocol version before anything else happens.
    /// </summary>
    Task<ServiceInfo> HandshakeAsync(ClientInfo client, CancellationToken cancellationToken);
}