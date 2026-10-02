using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Providers.Abstractions;

namespace VisualStudioTranslator.Engine.LocalTranslation;

/// <summary>
/// The provider that translates on this machine, so text never leaves it. It loads a model the
/// first time a direction is used and keeps it for the life of the process; loading takes
/// noticeable time and memory, and doing it per request would be unusable.
/// <para>
/// A model is used by one call at a time. Nothing is known about the engine's thread safety, and
/// the work is bound by the processor anyway, so serializing it costs little. The engine only
/// translates one text per call, so a batch is translated text by text.
/// </para>
/// <para>
/// Replacing a model on disk takes effect after the Engine restarts: the loaded one stays in use
/// and the revision in cache keys was fixed when the process started, which keeps the two consistent.
/// </para>
/// </summary>
internal sealed class LocalTranslationProvider(
    LocalModelStore models,
    ITextTranslatorFactory factory,
    ILogger<LocalTranslationProvider> logger) : ITranslationProvider, IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<Task<LoadedModel>>> _loaded = new();

    private readonly Lazy<ProviderInfo> _info = new(() => new ProviderInfo
    {
        Id = "local",
        DisplayName = "Local translation",
        Revision = models.ComputeRevision(),
        Kind = ProviderKind.Local,
    });

    public ProviderInfo Info => _info.Value;

    public ProviderCapabilities Capabilities { get; } = new()
    {
        Markup = MarkupSupport.None,
        MaxSegmentsPerRequest = 64,
        MaxCharactersPerRequest = 100_000,
    };

    public bool Supports(LanguagePair pair) => factory.IsModelPresent(models.DirectoryFor(pair));

    public async Task<IReadOnlyList<string>> TranslateAsync(
        LanguagePair pair, IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        LoadedModel model = await GetModelAsync(pair, cancellationToken).ConfigureAwait(false);

        await model.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => TranslateAll(model.Translator, texts, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            model.Gate.Release();
        }
    }

    public void Dispose()
    {
        foreach (Lazy<Task<LoadedModel>> entry in _loaded.Values)
        {
            if (entry.IsValueCreated && entry.Value.IsCompletedSuccessfully)
            {
                entry.Value.Result.Dispose();
            }
        }
    }

    private async Task<LoadedModel> GetModelAsync(LanguagePair pair, CancellationToken cancellationToken)
    {
        string key = pair.ToString();
        Lazy<Task<LoadedModel>> entry = _loaded.GetOrAdd(key, _ => new Lazy<Task<LoadedModel>>(() => Task.Run(() => Load(pair))));

        try
        {
            return await entry.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (TranslationProviderException)
        {
            // A failed load is not remembered, so installing the model or fixing the problem
            // takes effect on the next request instead of after a restart.
            _loaded.TryRemove(new KeyValuePair<string, Lazy<Task<LoadedModel>>>(key, entry));
            throw;
        }
    }

    private LoadedModel Load(LanguagePair pair)
    {
        string directory = models.DirectoryFor(pair);

        if (!factory.IsModelPresent(directory))
        {
            throw new TranslationProviderException(
                ProviderFailureKind.UnsupportedLanguagePair, "No local model is installed for this language pair.");
        }

        try
        {
            LoadedModel loaded = new(factory.Create(directory));
            logger.ModelLoaded(pair);
            return loaded;
        }
        catch (Exception exception)
        {
            logger.ModelLoadFailed(pair, exception);
            throw new TranslationProviderException(
                ProviderFailureKind.Unavailable, "The local translation model could not be loaded.", exception);
        }
    }

    private static List<string> TranslateAll(
        ITextTranslator translator, IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        List<string> results = [];

        foreach (string text in texts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                results.Add(translator.Translate(text));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The message deliberately says nothing about the text.
                throw new TranslationProviderException(
                    ProviderFailureKind.InvalidResponse, "The local engine failed to translate a segment.", exception);
            }
        }

        return results;
    }

    private sealed class LoadedModel(ITextTranslator translator) : IDisposable
    {
        public ITextTranslator Translator { get; } = translator;

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public void Dispose()
        {
            Translator.Dispose();
            Gate.Dispose();
        }
    }
}