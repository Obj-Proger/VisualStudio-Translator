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

    /// <summary>
    /// Whether a language model for the direction is installed, or how installing one is going.
    /// Sends nothing over the network, so it is safe to ask before the user has agreed to a download.
    /// Added in protocol version 1.2.
    /// </summary>
    Task<ModelInstallStatus> GetModelStatusAsync(
        string sourceLanguage, string targetLanguage, CancellationToken cancellationToken);

    /// <summary>
    /// Downloads and installs the model for the direction. Call it only once the user has agreed:
    /// this is what contacts the model server. It returns at once and the installation carries on
    /// in the Engine; follow it with <see cref="GetModelStatusAsync"/>. A call made while an
    /// installation is already running joins it instead of starting another.
    /// Added in protocol version 1.2.
    /// </summary>
    Task<ModelInstallStatus> StartModelInstallAsync(
        string sourceLanguage, string targetLanguage, CancellationToken cancellationToken);
}