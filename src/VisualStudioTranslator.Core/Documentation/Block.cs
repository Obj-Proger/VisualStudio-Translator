using System.Collections.Generic;

namespace VisualStudioTranslator.Core.Documentation;

/// <summary>
/// One block-level element within a <see cref="Section"/>: a paragraph, a list, or a
/// block of source code. Never translated as a whole - translation always happens at
/// the <see cref="Inline"/> level, inside a <see cref="Paragraph"/> or a
/// <see cref="ListItem"/>.
/// </summary>
public abstract record Block;

/// <summary>A paragraph of running text, corresponding to a <c>&lt;para&gt;</c> element or implicit top-level text.</summary>
public sealed record Paragraph : Block
{
    public IReadOnlyList<Inline> Inlines { get; init; } = [];
}

/// <summary>A <c>&lt;list&gt;</c> element: bulleted, numbered, or a two-column table.</summary>
public sealed record ListBlock : Block
{
    public required ListKind Kind { get; init; }

    public IReadOnlyList<ListItem> Items { get; init; } = [];
}

public enum ListKind
{
    Bullet,
    Number,
    Table,
}

/// <summary>
/// One <c>&lt;item&gt;</c> in a <see cref="ListBlock"/>. <see cref="Term"/> is only
/// populated for <see cref="ListKind.Table"/> and definition-style lists that use
/// <c>&lt;term&gt;</c>; bullet and number lists leave it empty and put everything in
/// <see cref="Description"/>.
/// </summary>
public sealed record ListItem
{
    public IReadOnlyList<Inline> Term { get; init; } = [];

    public IReadOnlyList<Inline> Description { get; init; } = [];
}

/// <summary>
/// A <c>&lt;code&gt;</c> block. Never translated and never protected token-by-token like
/// inline content - the whole block is opaque to the translation pipeline.
/// </summary>
public sealed record CodeBlock : Block
{
    public required string Code { get; init; }

    /// <summary>The <c>language</c> attribute, when the source specified one; otherwise <see langword="null"/>.</summary>
    public string? Language { get; init; }
}