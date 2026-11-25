namespace VisualStudioTranslator.Core.Rpc;

/// <summary>
/// The RPC contract exposed by the Engine process over the named pipe. Implemented by
/// the Engine and consumed by the Vsix client through a StreamJsonRpc proxy.
/// <para>
/// A plain interface on purpose, with no reference to StreamJsonRpc and none of the attributes
/// its newer versions use to generate the proxy at compile time. Inside Visual Studio the
/// extension runs on Visual Studio's own copy of that library, and a contract compiled against
/// a newer one than the oldest supported Visual Studio ships would fail to load there.
/// </para>
/// </summary>
public interface ITranslatorService
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

    /// <summary>
    /// Tells the Engine which language model to translate with on this machine and whether it may use a graphics
    /// card. Returns once they apply: a translation already under way finishes first, and the next one runs the
    /// way they say. Model status and installation then concern the model chosen here. Sends nothing over the
    /// network, and a model chosen that is not installed is installed only through <see cref="StartModelInstallAsync"/>.
    /// Added in protocol version 1.3.
    /// </summary>
    Task ApplyLocalModelSettingsAsync(LocalModelSettings settings, CancellationToken cancellationToken);

    /// <summary>
    /// Translates plain text, through the same providers, cache and checks as documentation. Like
    /// <see cref="TranslateDocumentationAsync"/> it never fails because of the content: what cannot be translated
    /// comes back as it was.
    /// Added in protocol version 1.4.
    /// </summary>
    Task<TranslateTextResult> TranslateTextAsync(TranslateTextRequest request, CancellationToken cancellationToken);
}