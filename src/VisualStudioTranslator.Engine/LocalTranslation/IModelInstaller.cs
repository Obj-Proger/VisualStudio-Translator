using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Rpc;

namespace VisualStudioTranslator.Engine.LocalTranslation;

/// <summary>
/// Gets a language model onto this machine. Kept behind an interface so the rest of the Engine
/// neither knows nor cares where models come from; only the implementation knows the server.
/// </summary>
internal interface IModelInstaller
{
    /// <summary>
    /// Whether a model is installed or being installed. Never touches the network, so the user's
    /// consent is not needed to ask.
    /// </summary>
    Task<ModelInstallStatus> GetStatusAsync(LanguagePair pair, CancellationToken cancellationToken);

    /// <summary>
    /// Starts installing the model and returns at once; the work carries on in the background.
    /// This is the call that reaches out to the model server, so it is made only after the user agreed.
    /// </summary>
    Task<ModelInstallStatus> StartInstallAsync(LanguagePair pair, CancellationToken cancellationToken);
}