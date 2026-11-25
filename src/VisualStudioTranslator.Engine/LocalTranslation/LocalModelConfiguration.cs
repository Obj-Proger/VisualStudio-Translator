using Microsoft.Extensions.Logging;
using VisualStudioTranslator.Core.Rpc;
using VisualStudioTranslator.Engine.LocalTranslation.Llama;

namespace VisualStudioTranslator.Engine.LocalTranslation;

/// <summary>Puts the user's settings for translation on this machine into effect.</summary>
internal interface ILocalModelConfiguration
{
    /// <summary>Returns once the settings apply; a translation under way finishes first.</summary>
    Task ApplyAsync(LocalModelSettings settings, CancellationToken cancellationToken);
}

/// <summary>
/// Switches the model the local provider translates with, and whether it may use a graphics card.
/// <list type="bullet">
/// <item>Settings arrive with every connection of every Visual Studio, nearly always the same as before, so ones
/// that change nothing do nothing: the server keeps running.</item>
/// <item>A model that is not installed is not downloaded here. Translations stop until the user agrees to the
/// download the extension then offers; the model before stays on disk, so going back to it is immediate.</item>
/// <item>Settings are applied one after another, in the order they came, so the last one sent is the one in effect.</item>
/// </list>
/// </summary>
/// <param name="machineMemory">How much memory the machine has, which decides <see cref="LocalModelChoice.Automatic"/>.</param>
internal sealed class LocalModelConfiguration(
    LlamaInstallation installation,
    LlamaTranslationProvider provider,
    long machineMemory,
    ILogger<LocalModelConfiguration> logger) : ILocalModelConfiguration, IDisposable
{
    private const string Allowed = "allowed";
    private const string NotUsed = "not used";

    private readonly SemaphoreSlim _applying = new(1, 1);

    // What the Engine starts with: the model the machine's memory suits, on a graphics card if one works. Only
    // touched while holding the gate above.
    private LocalModelSettings _applied = new();

    public async Task ApplyAsync(LocalModelSettings settings, CancellationToken cancellationToken)
    {
        await _applying.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (settings == _applied)
            {
                return;
            }

            ModelPackage model = LlamaCatalog.ChooseModel(settings.Model, machineMemory);
            installation.UseModel(model);

            // Not cut short by the caller giving up: the installation has already moved to the new model, and the
            // server must follow it, or it would go on translating with a model the user no longer wants.
            await provider.ReconfigureAsync(
                installation.ModelPath,
                settings.UseGraphicsCard ? LlamaCatalog.AllLayers : 0,
                CancellationToken.None).ConfigureAwait(false);

            _applied = settings;
            logger.LocalModelChanged(model.Name, settings.Model, settings.UseGraphicsCard ? Allowed : NotUsed);
        }
        finally
        {
            _applying.Release();
        }
    }

    public void Dispose() => _applying.Dispose();
}