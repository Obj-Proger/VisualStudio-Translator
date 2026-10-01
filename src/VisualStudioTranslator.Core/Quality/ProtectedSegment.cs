namespace VisualStudioTranslator.Core.Quality;

/// <summary>
/// The result of <see cref="MarkupProtector.Protect"/>: a plain string safe to hand to a
/// translation provider that has no markup awareness, together with everything needed to
/// turn the translated string back into <see cref="Documentation.Inline"/> content via
/// <see cref="MarkupProtector.Restore"/>. The two travel together across whatever gap
/// separates protection from restoration - typically an async call to a provider.
/// </summary>
public sealed record ProtectedSegment
{
    public required string Text { get; init; }

    public required IReadOnlyDictionary<int, ProtectedPlaceholder> Placeholders { get; init; }

    /// <summary>
    /// Whether any text outside the placeholder tokens contains a letter. A segment made only
    /// of placeholders, digits and punctuation has nothing for a provider to translate, so
    /// sending it would cost a request and risk a mangled result for no benefit. A method
    /// rather than a property so it is never picked up as data if the record is serialized.
    /// </summary>
    public bool ContainsTranslatableText() =>
        MarkupProtector.SplitOnPlaceholders(Text).Any(part => !part.IsPlaceholder && part.Text.Any(char.IsLetter));
}

public enum PlaceholderKind
{
    /// <summary>A whole non-translatable inline, restored verbatim: <c>CodeSpan</c> or a self-closing <c>Ref</c>.</summary>
    Node,

    /// <summary>A start/end marker pair wrapping translatable content, restored as an <c>EmphasisRun</c>.</summary>
    EmphasisWrapper,

    /// <summary>A start/end marker pair wrapping a <c>Ref</c>'s translatable display text.</summary>
    RefWrapper,
}

/// <summary>
/// What one placeholder id means when restoring. <see cref="Node"/> is populated only for
/// <see cref="PlaceholderKind.Node"/>; <see cref="RefKind"/> and <see cref="RefTarget"/>
/// only for <see cref="PlaceholderKind.RefWrapper"/>, since that is the metadata that must
/// never appear in the text sent for translation in the first place.
/// </summary>
public sealed record ProtectedPlaceholder
{
    public required PlaceholderKind Kind { get; init; }

    public Documentation.Inline? Node { get; init; }

    public Documentation.RefKind? RefKind { get; init; }

    public string? RefTarget { get; init; }
}