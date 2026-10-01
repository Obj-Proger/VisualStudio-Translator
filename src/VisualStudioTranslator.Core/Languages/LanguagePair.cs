namespace VisualStudioTranslator.Core.Languages;

/// <summary>
/// A directed translation direction, such as English to Russian. Both tags are expected to
/// be in <see cref="LanguageTag.Normalize"/> form, which <see cref="Create"/> guarantees;
/// build pairs through it rather than with an object initializer unless the tags are
/// already known to be canonical.
/// <para>
/// The source is always a concrete language, never "auto": deciding what language a
/// comment is written in is a separate concern that happens before a pair exists.
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