namespace VisualStudioTranslator.Core.Quality;

/// <summary>
/// How a <see cref="GlossaryEntry"/> should be handled when its term appears in text
/// being translated.
/// </summary>
public enum GlossaryEntryKind
{
    /// <summary>The term is left exactly as the author wrote it, in whatever casing matched.</summary>
    DoNotTranslate,

    /// <summary>The term is replaced with <see cref="GlossaryEntry.Replacement"/> before translation.</summary>
    TranslateAs,
}

/// <summary>One user-configured glossary term and how to handle it.</summary>
public sealed record GlossaryEntry
{
    public required string Term { get; init; }

    public required GlossaryEntryKind Kind { get; init; }

    /// <summary>The fixed replacement text. Only meaningful when <see cref="Kind"/> is <see cref="GlossaryEntryKind.TranslateAs"/>.</summary>
    public string? Replacement { get; init; }
}

/// <summary>
/// A user's set of glossary terms for one language pair. Building and persisting this -
/// the Tools &gt; Options UI, validating for duplicate or conflicting terms - is a later,
/// separate concern (Core/Settings); this type only carries the resolved set that
/// <see cref="GlossaryProtector"/> applies.
/// </summary>
public sealed record Glossary
{
    public IReadOnlyList<GlossaryEntry> Entries { get; init; } = [];
}