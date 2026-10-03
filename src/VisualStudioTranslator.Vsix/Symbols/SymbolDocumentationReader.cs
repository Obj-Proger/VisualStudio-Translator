using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;

namespace VisualStudioTranslator.Vsix.Symbols;

/// <summary>The documentation of the symbol under the pointer, and where that symbol's token sits in the text.</summary>
internal sealed class SymbolDocumentation
{
    public SymbolDocumentation(string xml, int spanStart, int spanLength)
    {
        Xml = xml;
        SpanStart = spanStart;
        SpanLength = spanLength;
    }

    /// <summary>The documentation comment as XML, as <c>ISymbol.GetDocumentationCommentXml()</c> returns it.</summary>
    public string Xml { get; }

    public int SpanStart { get; }

    public int SpanLength { get; }
}

/// <summary>
/// The Roslyn adapter: turns "the pointer is at this position of this text" into "this is the
/// documentation of the symbol there". It is the only place that touches Roslyn types, so
/// everything else works with plain strings.
/// <para>
/// Not handled: <c>&lt;inheritdoc/&gt;</c>. Roslyn returns it as an unexpanded element, so such a
/// symbol shows no translation until the adapter learns to find the documentation it inherits.
/// </para>
/// </summary>
internal static class SymbolDocumentationReader
{
    public static async Task<SymbolDocumentation?> ReadAsync(ITextSnapshot snapshot, int position, CancellationToken cancellationToken)
    {
        // Null for text that Roslyn does not manage; not an error.
        Document? document = snapshot.GetOpenDocumentInCurrentContextWithChanges();
        if (document is null)
        {
            return null;
        }

        ISymbol? symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, position, cancellationToken).ConfigureAwait(false);
        if (symbol is null)
        {
            return null;
        }

        string? xml = symbol.GetDocumentationCommentXml(expandIncludes: true, cancellationToken: cancellationToken);
        if (xml is null || xml.Trim().Length == 0)
        {
            return null;
        }

        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        TextSpan span = root is null ? new TextSpan(position, 0) : root.FindToken(position).Span;

        return new SymbolDocumentation(xml, span.Start, span.Length);
    }
}