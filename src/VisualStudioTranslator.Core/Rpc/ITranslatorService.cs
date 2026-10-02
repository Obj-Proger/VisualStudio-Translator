using System.Threading;
using System.Threading.Tasks;
using PolyType;
using StreamJsonRpc;

namespace VisualStudioTranslator.Core.Rpc;

/// <summary>
/// The RPC contract exposed by the Engine process over the named pipe. Implemented by
/// the Engine and consumed by the Vsix client through a StreamJsonRpc proxy.
/// </summary>
[JsonRpcContract]
[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
public partial interface ITranslatorService
{
    /// <summary>
    /// The first call a client makes after connecting. Lets both sides confirm they
    /// speak a compatible protocol version before anything else happens.
    /// </summary>
    Task<ServiceInfo> HandshakeAsync(ClientInfo client, CancellationToken cancellationToken);

    /// <summary>
    /// Translates one documentation comment. Never fails because of the content: whatever
    /// cannot be translated comes back in its original language, and
    /// <see cref="TranslateDocumentationResult.Outcome"/> and the counts say how much was done.
    /// Added in protocol version 1.1.
    /// </summary>
    Task<TranslateDocumentationResult> TranslateDocumentationAsync(
        TranslateDocumentationRequest request, CancellationToken cancellationToken);
}