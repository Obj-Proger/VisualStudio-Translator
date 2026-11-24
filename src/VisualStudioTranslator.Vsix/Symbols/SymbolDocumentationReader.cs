using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;
using VisualStudioTranslator.Core.Documentation;

namespace VisualStudioTranslator.Vsix.Symbols;

/// <summary>The documentation of the symbol under the pointer, and where that symbol's token sits in the text.</summary>
internal sealed class SymbolDocumentation(string xml, int spanStart, int spanLength)
{
    /// <summary>The documentation comment as XML, as <c>ISymbol.GetDocumentationCommentXml()</c> returns it.</summary>
    public string Xml { get; } = xml;

    public int SpanStart { get; } = spanStart;

    public int SpanLength { get; } = spanLength;
}

/// <summary>
/// The Roslyn adapter: turns "the pointer is at this position of this text" into "this is the
/// documentation of the symbol there". It is the only place that touches Roslyn types, so
/// everything else works with plain strings.
/// <para>
/// <c>&lt;inheritdoc/&gt;</c> is followed here, since Roslyn returns it as it was written: to the member
/// a <c>cref</c> names, or else to the member overridden or implemented, or for a type to its base type or
/// first interface. What is inherited is put together with the comment's own sections by
/// <see cref="InheritedDocumentation"/>, the way the compiler's documentation tools do it.
/// </para>
/// </summary>
internal static class SymbolDocumentationReader
{
    // Chains of inheriting comments longer than this are cut short; it also ends a cycle of crefs.
    private const int MaxInheritanceDepth = 8;

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

        string? xml = await DocumentationOfAsync(symbol, document.Project, 0, cancellationToken).ConfigureAwait(false);
        if (xml is null || xml.Trim().Length == 0)
        {
            return null;
        }

        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        TextSpan span = root is null ? new TextSpan(position, 0) : root.FindToken(position).Span;

        return new SymbolDocumentation(xml, span.Start, span.Length);
    }

    private static async Task<string?> DocumentationOfAsync(
        ISymbol symbol, Project project, int depth, CancellationToken cancellationToken)
    {
        string? xml = symbol.GetDocumentationCommentXml(expandIncludes: true, cancellationToken: cancellationToken);

        if (xml is null || depth >= MaxInheritanceDepth || !InheritedDocumentation.Inherits(xml, out string? cref))
        {
            return xml;
        }

        ISymbol? source = cref is null
            ? InheritsFrom(symbol)
            : await ResolveAsync(cref, project, cancellationToken).ConfigureAwait(false);

        if (source is null)
        {
            return xml;
        }

        string? inherited = await DocumentationOfAsync(source, project, depth + 1, cancellationToken).ConfigureAwait(false);

        return inherited is null ? xml : InheritedDocumentation.Merge(xml, inherited);
    }

    // Where a comment that names no member inherits from, in the order the documentation tools look.
    private static ISymbol? InheritsFrom(ISymbol symbol) => symbol switch
    {
        IMethodSymbol { MethodKind: MethodKind.Constructor } constructor => BaseConstructor(constructor),
        IMethodSymbol method => method.OverriddenMethod ?? ImplementedMember(method, method.ExplicitInterfaceImplementations),
        IPropertySymbol property => property.OverriddenProperty
            ?? ImplementedMember(property, property.ExplicitInterfaceImplementations),
        IEventSymbol @event => @event.OverriddenEvent ?? ImplementedMember(@event, @event.ExplicitInterfaceImplementations),
        INamedTypeSymbol type => type.BaseType is { SpecialType: not SpecialType.System_Object } baseType
            ? baseType
            : type.Interfaces.FirstOrDefault(),
        _ => null,
    };

    // The interface member this one implements: named outright when explicit, found otherwise.
    private static ISymbol? ImplementedMember<TSymbol>(TSymbol member, IEnumerable<TSymbol> explicitImplementations)
        where TSymbol : class, ISymbol
    {
        TSymbol? named = explicitImplementations.FirstOrDefault();
        if (named is not null)
        {
            return named;
        }

        INamedTypeSymbol? type = member.ContainingType;
        if (type is null)
        {
            return null;
        }

        foreach (INamedTypeSymbol @interface in type.AllInterfaces)
        {
            foreach (ISymbol candidate in @interface.GetMembers(member.Name))
            {
                if (SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(candidate), member))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    // A constructor inherits from the base class's constructor with the same parameter types.
    private static IMethodSymbol? BaseConstructor(IMethodSymbol constructor) =>
        constructor.ContainingType.BaseType?.InstanceConstructors
            .FirstOrDefault(candidate => SameParameterTypes(candidate, constructor));

    private static bool SameParameterTypes(IMethodSymbol first, IMethodSymbol second)
    {
        if (first.Parameters.Length != second.Parameters.Length)
        {
            return false;
        }

        for (int i = 0; i < first.Parameters.Length; i++)
        {
            if (!SymbolEqualityComparer.Default.Equals(first.Parameters[i].Type, second.Parameters[i].Type))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<ISymbol?> ResolveAsync(string documentationId, Project project, CancellationToken cancellationToken)
    {
        Compilation? compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);

        return compilation is null ? null : DocumentationCommentId.GetFirstSymbolForDeclarationId(documentationId, compilation);
    }
}