using System.Collections.Generic;
using VisualStudioTranslator.Core.Quality;

namespace VisualStudioTranslator.Core.Rpc;

/// <summary>
/// One documentation comment to translate, with everything that shapes the result. The
/// request is self-contained: the Engine keeps no per-client state between calls.
/// </summary>
public sealed record TranslateDocumentationRequest
{
    /// <summary>
    /// The XML returned by <c>ISymbol.GetDocumentationCommentXml()</c>. A string rather than a
    /// parsed model on purpose: the model's inline and block types are abstract, and plain
    /// strings cross the wire without any serializer configuration.
    /// </summary>
    public required string DocumentationXml { get; init; }

    /// <summary>BCP 47 tag of the language the comment is written in, e.g. "en".</summary>
    public required string SourceLanguage { get; init; }

    /// <summary>BCP 47 tag of the language to translate into, e.g. "ru" or "pt-BR".</summary>
    public required string TargetLanguage { get; init; }

    /// <summary>Terms to keep or to translate in a fixed way. Entries with a blank term are ignored.</summary>
    public IReadOnlyList<GlossaryEntry> Glossary { get; init; } = [];

    /// <summary>
    /// Whether the user has agreed to their text being sent to a remote service. Without it
    /// no cloud provider is ever used.
    /// </summary>
    public bool AllowCloudProvider { get; init; }
}