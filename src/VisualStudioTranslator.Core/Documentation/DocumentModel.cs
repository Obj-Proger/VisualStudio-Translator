using System.Collections.Generic;

namespace VisualStudioTranslator.Core.Documentation;

/// <summary>
/// A structured representation of a symbol's XML documentation comment, parsed once and
/// shared by every consumer: translation, quality checks, and rendering back into a
/// Quick Info element. Nothing here is specific to any of those consumers.
/// </summary>
public sealed record DocumentModel
{
    public Section? Summary { get; init; }

    public Section? Remarks { get; init; }

    public Section? Returns { get; init; }

    public Section? Value { get; init; }

    public IReadOnlyList<NamedSection> Params { get; init; } = [];

    public IReadOnlyList<NamedSection> TypeParams { get; init; } = [];

    /// <summary>Exceptions, keyed by the <c>cref</c> of the exception type.</summary>
    public IReadOnlyList<NamedSection> Exceptions { get; init; } = [];

    public IReadOnlyList<Section> Examples { get; init; } = [];

    /// <summary>
    /// Targets from <c>&lt;seealso&gt;</c> elements. These are <c>cref</c> references,
    /// not prose, so they are kept as plain strings rather than translatable sections.
    /// </summary>
    public IReadOnlyList<string> SeeAlso { get; init; } = [];
}

/// <summary>A section identified by a name: a <c>param</c>, <c>typeparam</c>, or <c>exception</c> entry.</summary>
public sealed record NamedSection
{
    /// <summary>The parameter name, type parameter name, or exception <c>cref</c>.</summary>
    public required string Name { get; init; }

    public required Section Content { get; init; }
}

/// <summary>An ordered sequence of blocks making up one documentation section.</summary>
public sealed record Section
{
    public IReadOnlyList<Block> Blocks { get; init; } = [];
}