using VisualStudioTranslator.Core.Languages;
using VisualStudioTranslator.Core.Providers.Abstractions;

namespace VisualStudioTranslator.Core.Caching;

/// <summary>Everything that decides what a provider would return for one segment.</summary>
public sealed record TranslationCacheKeyInputs
{
    /// <summary>Only <see cref="ProviderInfo.Id"/> and <see cref="ProviderInfo.Revision"/> are used.</summary>
    public required ProviderInfo Provider { get; init; }

    public required LanguagePair Languages { get; init; }

    /// <summary>The protection strategy actually applied to the text, not merely what the provider could do.</summary>
    public required MarkupSupport MarkupProtection { get; init; }

    /// <summary>From <see cref="GlossaryFingerprint.Compute"/>.</summary>
    public required string GlossaryFingerprint { get; init; }

    /// <summary>
    /// Exactly the text the provider receives: the protected text with glossary applied.
    /// Not the original comment. Code spans and refs live in the placeholder map, not in
    /// this text, so two segments that differ only in which symbol a placeholder stands
    /// for share a key and share the cached translation, which stays correct because the
    /// placeholders are re-resolved against the current segment on restore.
    /// </summary>
    public required string Text { get; init; }
}

/// <summary>
/// The key a translation is cached under: a SHA-256 over <see cref="TranslationCacheKeyInputs"/>
/// plus <see cref="CacheVersions"/>. The source text itself is never stored, only this hash
/// and the translation.
/// </summary>
public sealed record TranslationCacheKey
{
    /// <summary>64 lowercase hex characters.</summary>
    public required string Value { get; init; }

    public static TranslationCacheKey Create(TranslationCacheKeyInputs inputs)
    {
        string value = CanonicalHash.Compute(writer =>
        {
            writer.Write(CacheVersions.Schema);
            writer.Write(CacheVersions.Pipeline);
            writer.Write(inputs.Provider.Id);
            writer.Write(inputs.Provider.Revision);
            writer.Write(inputs.Languages.Source);
            writer.Write(inputs.Languages.Target);
            writer.Write((int)inputs.MarkupProtection);
            writer.Write(inputs.GlossaryFingerprint);
            writer.Write(inputs.Text);
        });

        return new TranslationCacheKey { Value = value };
    }

    public override string ToString() => Value;
}