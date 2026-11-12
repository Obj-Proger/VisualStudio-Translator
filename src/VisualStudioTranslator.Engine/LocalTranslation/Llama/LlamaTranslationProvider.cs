using System.Globalization;
using Microsoft.Extensions.Logging;
using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Providers.Abstractions;

namespace VisualStudioTranslator.Engine.LocalTranslation.Llama;

/// <summary>
/// The local provider built on a language model: it translates on this machine, through the llama.cpp
/// server that <see cref="LlamaServer"/> runs as a program of its own, so text never leaves the machine.
/// <para>
/// One request reaches the model at a time. The server is started with one slot, and the work is bound by
/// the processor, so two requests side by side would only make both slower; waiting here also keeps the
/// order in which they arrived. A batch is translated segment by segment, since the model takes one at a time.
/// </para>
/// <para>
/// The server is started by the first request and stopped once <see cref="LlamaServerOptions.IdleTimeout"/>
/// has passed with none, which gives the memory the model holds back to the user. The next request starts it
/// again, at the cost of a few seconds of loading.
/// </para>
/// <para>
/// Its identifier, "local", is the one the Bergamot provider it replaced had, so a saved choice of provider
/// keeps working. Its revision is a different one, so translations Bergamot made are not served as its own.
/// </para>
/// </summary>
internal sealed class LlamaTranslationProvider : ITranslationProvider, IDisposable
{
    private readonly LlamaServer _server;
    private readonly LlamaServerOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<LlamaTranslationProvider> _logger;
    private readonly ChatCompletionTranslator _translator;

    // Held for the whole of a request, and by the idle check while it stops the server, so the two never meet.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ITimer _idleTimer;

    // When the last request ended. Written and read only while holding the gate.
    private long _lastUsed;

    public LlamaTranslationProvider(
        LlamaServer server, LlamaServerOptions options, TimeProvider time, ILogger<LlamaTranslationProvider> logger)
    {
        _server = server;
        _options = options;
        _time = time;
        _logger = logger;
        _translator = new ChatCompletionTranslator(server.Client);
        _idleTimer = time.CreateTimer(
            static state => ((LlamaTranslationProvider)state!).StopIfIdle(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
    }

    public ProviderInfo Info => new()
    {
        Id = "local",
        DisplayName = "Local translation",
        Revision = ComputeRevision(),
        Kind = ProviderKind.Local,
        QualityTier = QualityTiers.Compact,
    };

    public ProviderCapabilities Capabilities { get; } = new()
    {
        Markup = MarkupSupport.None,

        // A request holds the model until its last segment is done, and every other request waits for it.
        // A few seconds a segment, so a small batch keeps that wait short.
        MaxSegmentsPerRequest = 8,

        // Prompt, segment and translation share the server's context of 4096 tokens. A segment of this size
        // is under a thousand tokens of English, and its translation may take twice that.
        MaxCharactersPerRequest = 3000,
    };

    /// <summary>
    /// Whether the server and the model are installed. The model is multilingual, so the direction does not
    /// decide anything here; a translation into the wrong language or script is caught by the validator.
    /// </summary>
    public bool Supports(LanguagePair pair) => File.Exists(_options.ExecutablePath) && File.Exists(_options.ModelPath);

    public async Task<IReadOnlyList<string>> TranslateAsync(
        LanguagePair pair, IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        if (!Supports(pair))
        {
            throw new TranslationProviderException(
                ProviderFailureKind.UnsupportedLanguagePair, "The local translation model is not installed.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _idleTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

            await _server.EnsureStartedAsync(cancellationToken).ConfigureAwait(false);

            List<string> results = new(texts.Count);

            foreach (string text in texts)
            {
                results.Add(await _translator.TranslateAsync(pair, text, cancellationToken).ConfigureAwait(false));
            }

            return results;
        }
        finally
        {
            // Counted from the end of the request, failed or not: a server that is there costs memory either way.
            _lastUsed = _time.GetTimestamp();
            _idleTimer.Change(_options.IdleTimeout, Timeout.InfiniteTimeSpan);
            _gate.Release();
        }
    }

    /// <summary>Stops the idle check. The server itself belongs to whoever created it.</summary>
    public void Dispose() => _idleTimer.Dispose();

    private void StopIfIdle()
    {
        // A request is under way; it schedules the next check when it ends.
        if (!_gate.Wait(0))
        {
            return;
        }

        try
        {
            // A check that was already on its way when a later request moved it: not idle long enough yet.
            if (_time.GetElapsedTime(_lastUsed) < _options.IdleTimeout)
            {
                return;
            }

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.ServerIdle(_options.IdleTimeout.TotalMinutes);
            }

            _server.Stop();
        }
        finally
        {
            _gate.Release();
        }
    }

    // What decides the output for a given text: the model file and the instruction. The file is known by its
    // name, which names the model and its quantization, and its size, which tells two builds of it apart.
    private string ComputeRevision()
    {
        FileInfo model = new(_options.ModelPath);
        string size = model.Exists ? model.Length.ToString(CultureInfo.InvariantCulture) : "missing";

        return $"{model.Name}/{size}/prompt-{TranslationPrompt.Version}";
    }
}