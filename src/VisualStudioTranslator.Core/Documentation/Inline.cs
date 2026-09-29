using System.Collections.Generic;

namespace VisualStudioTranslator.Core.Documentation;

/// <summary>
/// One inline element within a <see cref="Paragraph"/> or <see cref="ListItem"/>. This is
/// the level at which translation and markup protection actually operate: a
/// <see cref="TextRun"/> is translatable, everything else is either opaque
/// (<see cref="CodeSpan"/>, <see cref="Ref"/>) or a container that only carries
/// translatable content further down (<see cref="EmphasisRun"/>).
/// </summary>
public abstract record Inline;

/// <summary>Plain translatable text.</summary>
public sealed record TextRun : Inline
{
    public required string Text { get; init; }
}

/// <summary>A <c>&lt;c&gt;</c> inline code span. Never translated.</summary>
public sealed record CodeSpan : Inline
{
    public required string Text { get; init; }
}

/// <summary>Emphasised text, from a <c>&lt;b&gt;</c> or <c>&lt;i&gt;</c> element. The content inside is still translatable.</summary>
public sealed record EmphasisRun : Inline
{
    public IReadOnlyList<Inline> Content { get; init; } = [];
}

public enum RefKind
{
    /// <summary><c>&lt;see cref="..."/&gt;</c> or <c>&lt;seealso cref="..."/&gt;</c> used inline.</summary>
    Cref,

    /// <summary><c>&lt;paramref name="..."/&gt;</c>.</summary>
    Paramref,

    /// <summary><c>&lt;typeparamref name="..."/&gt;</c>.</summary>
    Typeparamref,

    /// <summary><c>&lt;see langword="..."/&gt;</c>, e.g. <c>null</c> or <c>async</c>.</summary>
    Langword,
}

/// <summary>
/// A reference to a symbol, parameter, type parameter, or language keyword. Never
/// translated - protected as a whole and restored verbatim after translation, the same
/// way as <see cref="CodeSpan"/>.
/// </summary>
public sealed record Ref : Inline
{
    public required RefKind Kind { get; init; }

    /// <summary>The <c>cref</c> value, parameter/type parameter name, or langword text, depending on <see cref="Kind"/>.</summary>
    public required string Target { get; init; }

    /// <summary>
    /// The element's own inner content, for the rare case a <c>&lt;see cref="..."&gt;</c>
    /// carries display text instead of being self-closing. Translatable, since it is
    /// prose the author wrote, not the reference itself. <see langword="null"/> when the
    /// element was self-closing.
    /// </summary>
    public IReadOnlyList<Inline>? DisplayContent { get; init; }
}