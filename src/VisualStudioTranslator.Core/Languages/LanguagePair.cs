namespace VisualStudioTranslator.Core.Languages;

/// <summary>
/// A directed translation direction, such as English to Russian. Both tags are expected to
/// be in <see cref="LanguageTag.Normalize"/> form, which <see cref="Create"/> guarantees;
/// build pairs through it rather than with an object initializer unless the tags are
/// already known to be canonical.
/// <para>
/// The source is a language, or <see cref="LanguageTag.Undetermined"/> when the comment may be written in any
/// language: documentation is not always in English, and a language model can tell which language it reads.
/// </para>
/// </summary>
public sealed record LanguagePair
{
    public required string Source { get; init; }

    public required string Target { get; init; }

    /// <returns>The pair with both tags normalized, or <see langword="null"/> when either tag is invalid.</returns>
    public static LanguagePair? Create(string? source, string? target)
    {
        string? normalizedSource = LanguageTag.Normalize(source);
        string? normalizedTarget = LanguageTag.Normalize(target);

        return normalizedSource is null || normalizedTarget is null
            ? null
            : new LanguagePair { Source = normalizedSource, Target = normalizedTarget };
    }

    public override string ToString() => $"{Source}->{Target}";
}