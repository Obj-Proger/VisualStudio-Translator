using Microsoft.Extensions.Logging;
using VisualStudioTranslator.Core.Caching;
using VisualStudioTranslator.Core.Documentation;
using VisualStudioTranslator.Core.Providers.Abstractions;
using VisualStudioTranslator.Core.Quality;

namespace VisualStudioTranslator.Engine.Translation;

/// <summary>
/// Runs a documentation comment through the whole pipeline: segment, protect markup, apply
/// the glossary, look in the cache, translate what is missing, validate, cache what passed,
/// restore, recompose.
/// <para>
/// A failure never reaches the caller as an exception (principle P7): whatever cannot be
/// translated keeps its original text, and <see cref="TranslationResult"/> says how much that
/// was. The one exception is cancellation, which is the caller's own request and propagates.
/// </para>
/// <para>
/// The cache holds one translation per segment, and a better provider's translation replaces a
/// worse one. A cached translation is used only if it is at least as good as what the chosen
/// provider would produce, so choosing a better provider re-translates what a weaker one
/// translated, and the new result then serves everyone, including the weaker provider when
/// the better one is switched off.
/// </para>
/// <para>
/// Deliberately not here: choosing a provider, retrying, falling back to another one, and
/// deciding whether the user consented to a cloud provider. The orchestrator is handed a
/// provider and a consent flag, and only refuses to send text where consent is missing.
/// </para>
/// </summary>
internal sealed class TranslationOrchestrator(ITranslationCache cache, ILogger<TranslationOrchestrator> logger)
{
    public async Task<TranslationResult> TranslateAsync(
        TranslationRequest request, ITranslationProvider provider, CancellationToken cancellationToken)
    {
        IReadOnlyList<Segment> segments = DocumentSegmenter.Segment(request.Document);

        if (string.Equals(request.Languages.Source, request.Languages.Target, StringComparison.Ordinal))
        {
            return Unchanged(request.Document, segments.Count);
        }

        // Checked before anything can be sent, as the last line of defence for the user's
        // privacy: even if whatever picks the provider gets this wrong, text stays local.
        if (provider.Info.Kind == ProviderKind.Cloud && !request.AllowCloudProvider)
        {
            logger.CloudConsentMissing(provider.Info.Id);
            return Unchanged(request.Document, segments.Count) with { BlockedByCloudConsent = true };
        }

        if (!provider.Supports(request.Languages))
        {
            logger.LanguagePairUnsupported(provider.Info.Id, request.Languages);
            return Unchanged(request.Document, segments.Count)
                with
            { ProviderFailure = ProviderFailureKind.UnsupportedLanguagePair };
        }

        List<Group> groups = Prepare(request, segments);

        List<Group> misses = [];
        foreach (Group group in groups)
        {
            CachedTranslation? cached = await TryGetCachedAsync(group.Key, cancellationToken).ConfigureAwait(false);

            // A stored translation counts only if it is at least as good as this provider's would be.
            if (cached is not null && cached.Satisfies(provider.Info))
            {
                group.Translation = cached.Text;
                group.FromCache = true;
            }
            else
            {
                misses.Add(group);
            }
        }

        ProviderFailureKind? failure = misses.Count > 0
            ? await TranslateMissesAsync(request, provider, misses, cancellationToken).ConfigureAwait(false)
            : null;

        Dictionary<int, IReadOnlyList<Inline>> translations = [];
        int cachedCount = 0;
        int translatedCount = 0;

        foreach (Group group in groups)
        {
            if (group.Translation is null)
            {
                continue;
            }

            foreach (Prepared member in group.Members)
            {
                // Restored against this segment's own placeholders, not the group's first:
                // two segments sharing a translation may stand for different code spans.
                translations[member.Segment.Id] = MarkupProtector.Restore(group.Translation, member.Protected.Placeholders);

                if (group.FromCache)
                {
                    cachedCount++;
                }
                else
                {
                    translatedCount++;
                }
            }
        }

        return new TranslationResult
        {
            Document = DocumentSegmenter.Compose(request.Document, translations),
            SegmentCount = segments.Count,
            CachedCount = cachedCount,
            TranslatedCount = translatedCount,
            UnchangedCount = segments.Count - cachedCount - translatedCount,
            ProviderFailure = failure,
        };
    }

    // Protects every segment and groups the ones whose protected text is identical, so each
    // distinct text is looked up and translated once however often it occurs.
    private static List<Group> Prepare(TranslationRequest request, IReadOnlyList<Segment> segments)
    {
        string glossaryFingerprint = GlossaryFingerprint.Compute(request.Glossary);

        List<Group> ordered = [];
        Dictionary<string, Group> byKey = [];

        foreach (Segment segment in segments)
        {
            ProtectedSegment protectedSegment = GlossaryProtector.Apply(
                MarkupProtector.Protect(segment.Inlines), request.Glossary);

            if (!protectedSegment.ContainsTranslatableText())
            {
                continue;
            }

            TranslationCacheKey key = TranslationCacheKey.Create(new TranslationCacheKeyInputs
            {
                Languages = request.Languages,
                MarkupProtection = MarkupSupport.None,
                GlossaryFingerprint = glossaryFingerprint,
                Text = protectedSegment.Text,
            });

            if (!byKey.TryGetValue(key.Value, out Group? group))
            {
                group = new Group(key, protectedSegment.Text);
                byKey[key.Value] = group;
                ordered.Add(group);
            }

            group.Members.Add(new Prepared(segment, protectedSegment));
        }

        return ordered;
    }

    // Returns the recognized failure that stopped the work, if any. The first failure of any
    // kind ends it: retrying and falling back are a later layer's job, and hammering a
    // provider that is failing helps nobody.
    private async Task<ProviderFailureKind?> TranslateMissesAsync(
        TranslationRequest request, ITranslationProvider provider, List<Group> misses, CancellationToken cancellationToken)
    {
        TranslationValidationOptions options = TranslationValidationOptions.ForTarget(request.Languages.Target);

        foreach (List<Group> batch in Batch(misses, provider.Capabilities))
        {
            IReadOnlyList<string> results;

            try
            {
                results = await provider
                    .TranslateAsync(request.Languages, [.. batch.Select(group => group.Text)], cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TranslationProviderException exception)
            {
                logger.ProviderFailed(provider.Info.Id, exception.Kind, exception);
                return exception.Kind;
            }
            catch (Exception exception)
            {
                logger.ProviderBrokeContract(provider.Info.Id, exception);
                return null;
            }

            if (results.Count != batch.Count)
            {
                logger.ProviderReturnedWrongCount(provider.Info.Id, batch.Count, results.Count);
                return ProviderFailureKind.InvalidResponse;
            }

            for (int i = 0; i < batch.Count; i++)
            {
                Group group = batch[i];

                // Every member shares the same protected text, so the first one stands for all.
                ProtectedSegment protectedSegment = group.Members[0].Protected;

                // Cleaned before validating and caching, so a stray character from the model is
                // neither judged as part of the translation nor kept in the cache.
                string translated = PlaceholderArtifacts.Clean(protectedSegment.Text, results[i] ?? string.Empty);

                TranslationValidationResult validation = TranslationValidator.Validate(
                    protectedSegment, translated, options);

                if (!validation.IsValid)
                {
                    if (logger.IsEnabled(LogLevel.Warning))
                    {
                        logger.TranslationRejected(
                            group.Members[0].Segment.Id, string.Join(", ", validation.Issues.Select(issue => issue.Kind)));
                    }

                    continue;
                }

                group.Translation = translated;

                // Stored with who made it. If a better provider's translation is already there the
                // cache keeps it, so this write can never make the stored translation worse.
                await TrySetCachedAsync(
                    group.Key, CachedTranslation.From(provider.Info, translated), cancellationToken).ConfigureAwait(false);
            }
        }

        return null;
    }

    // Splits the work to fit what the provider accepts per request. A text longer than the
    // character limit cannot be sent at all and stays untranslated.
    private List<List<Group>> Batch(List<Group> misses, ProviderCapabilities capabilities)
    {
        List<List<Group>> batches = [];
        List<Group> current = [];
        int currentCharacters = 0;

        foreach (Group group in misses)
        {
            int length = group.Text.Length;

            if (length > capabilities.MaxCharactersPerRequest)
            {
                logger.SegmentTooLarge(group.Members[0].Segment.Id, length, capabilities.MaxCharactersPerRequest);
                continue;
            }

            if (current.Count > 0
                && (current.Count >= capabilities.MaxSegmentsPerRequest
                    || currentCharacters + length > capabilities.MaxCharactersPerRequest))
            {
                batches.Add(current);
                current = [];
                currentCharacters = 0;
            }

            current.Add(group);
            currentCharacters += length;
        }

        if (current.Count > 0)
        {
            batches.Add(current);
        }

        return batches;
    }

    // The cache is an optimization, so a cache that misbehaves must cost speed, never
    // correctness: a failed read is a miss and a failed write is dropped.
    private async Task<CachedTranslation?> TryGetCachedAsync(TranslationCacheKey key, CancellationToken cancellationToken)
    {
        try
        {
            return await cache.TryGetAsync(key, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.CacheFailed("read", exception);
            return null;
        }
    }

    private async Task TrySetCachedAsync(TranslationCacheKey key, CachedTranslation translation, CancellationToken cancellationToken)
    {
        try
        {
            await cache.SetAsync(key, translation, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.CacheFailed("write", exception);
        }
    }

    private static TranslationResult Unchanged(DocumentModel document, int segmentCount) => new()
    {
        Document = document,
        SegmentCount = segmentCount,
        CachedCount = 0,
        TranslatedCount = 0,
        UnchangedCount = segmentCount,
    };

    private sealed record Prepared(Segment Segment, ProtectedSegment Protected);

    // All the segments of one document whose protected text is identical, and so share one
    // cache key and one translation.
    private sealed class Group(TranslationCacheKey key, string text)
    {
        public TranslationCacheKey Key { get; } = key;

        public string Text { get; } = text;

        public List<Prepared> Members { get; } = [];

        public string? Translation { get; set; }

        public bool FromCache { get; set; }
    }
}