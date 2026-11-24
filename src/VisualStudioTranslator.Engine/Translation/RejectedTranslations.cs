using VisualStudioTranslator.Core.Caching;
using VisualStudioTranslator.Core.Providers.Abstractions;

namespace VisualStudioTranslator.Engine.Translation;

/// <summary>
/// Remembers for a while which segments a provider could not translate acceptably, so that hovering over
/// the same symbol again does not send the same text to it again.
/// <para>
/// A local model answers the same text with the same translation, so asking again only spends seconds of
/// processor time on a result the validator will turn down again. The memory is tied to the provider's
/// revision: a new model or a new instruction is a new chance. It is also kept short and in memory only,
/// since a cloud service may well do better on a second try, and a restart of the Engine forgets it.
/// </para>
/// </summary>
internal sealed class RejectedTranslations(TimeProvider time)
{
    /// <summary>How long a rejection stands.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromHours(1);

    // Far more segments than anyone hovers over in an hour; only a runaway would reach it.
    private const int Capacity = 4096;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _until = new(StringComparer.Ordinal);

    public bool IsRejected(TranslationCacheKey key, ProviderInfo provider)
    {
        string entry = EntryFor(key, provider);

        lock (_gate)
        {
            if (!_until.TryGetValue(entry, out DateTimeOffset until))
            {
                return false;
            }

            if (time.GetUtcNow() < until)
            {
                return true;
            }

            _until.Remove(entry);
            return false;
        }
    }

    public void Remember(TranslationCacheKey key, ProviderInfo provider)
    {
        string entry = EntryFor(key, provider);
        DateTimeOffset now = time.GetUtcNow();

        lock (_gate)
        {
            if (_until.Count >= Capacity && !_until.ContainsKey(entry))
            {
                MakeRoom(now);
            }

            _until[entry] = now + Retention;
        }
    }

    // Called with the gate held. Expired entries go first; if none has, the one closest to expiring does.
    private void MakeRoom(DateTimeOffset now)
    {
        List<string> expired = [.. _until.Where(pair => pair.Value <= now).Select(pair => pair.Key)];

        foreach (string entry in expired)
        {
            _until.Remove(entry);
        }

        if (_until.Count >= Capacity)
        {
            _until.Remove(_until.MinBy(pair => pair.Value).Key);
        }
    }

    private static string EntryFor(TranslationCacheKey key, ProviderInfo provider) =>
        provider.Id + "\n" + provider.Revision + "\n" + key.Value;
}