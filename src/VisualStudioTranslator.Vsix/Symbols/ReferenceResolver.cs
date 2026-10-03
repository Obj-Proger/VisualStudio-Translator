using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using VisualStudioTranslator.Core.Documentation;

namespace VisualStudioTranslator.Vsix.Symbols;

/// <summary>
/// Resolves the references in a piece of documentation to what the editor itself would show
/// for them: "T:Ns.Result`1" becomes "Result&lt;TValue&gt;", and the kind of symbol it is decides
/// the colour. The documentation id alone cannot say either; only the compilation can.
/// </summary>
internal static class ReferenceResolver
{
    // Names only, with type parameters and the containing type for members, so a property
    // reads "Service.BasePrice" and a generic type "Result<TValue>". Parameter lists are left
    // out: a reference in prose names a symbol, it does not show its signature.
    private static readonly SymbolDisplayFormat DisplayFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeContainingType,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    /// <summary>
    /// Whatever cannot be resolved is simply absent from the result, and the renderer falls back to
    /// a guess. A failure here must never cost the user the tooltip, so nothing is thrown.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, ResolvedReference>> ResolveAsync(
        ITextSnapshot snapshot, IReadOnlyList<string> targets, CancellationToken cancellationToken)
    {
        Dictionary<string, ResolvedReference> resolved = [];

        if (targets.Count == 0)
        {
            return resolved;
        }

        try
        {
            Document? document = snapshot.GetOpenDocumentInCurrentContextWithChanges();
            Compilation? compilation = document is null
                ? null
                : await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);

            if (compilation is null)
            {
                return resolved;
            }

            foreach (string target in targets)
            {
                ISymbol? symbol = DocumentationCommentId.GetFirstSymbolForDeclarationId(target, compilation);

                if (symbol != null)
                {
                    resolved[target] = new ResolvedReference(symbol.ToDisplayString(DisplayFormat), KindOf(symbol));
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ActivityLog.LogWarning(nameof(ReferenceResolver), $"Could not resolve references: {ex.Message}");
        }

        return resolved;
    }

    private static ReferenceKind KindOf(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol { TypeKind: TypeKind.Struct } => ReferenceKind.Struct,
        INamedTypeSymbol { TypeKind: TypeKind.Interface } => ReferenceKind.Interface,
        INamedTypeSymbol { TypeKind: TypeKind.Enum } => ReferenceKind.Enum,
        INamedTypeSymbol { TypeKind: TypeKind.Delegate } => ReferenceKind.Delegate,
        INamedTypeSymbol => ReferenceKind.Class,
        IMethodSymbol => ReferenceKind.Method,
        IPropertySymbol => ReferenceKind.Property,
        IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } => ReferenceKind.EnumMember,
        IFieldSymbol { IsConst: true } => ReferenceKind.Constant,
        IFieldSymbol => ReferenceKind.Field,
        IEventSymbol => ReferenceKind.Event,
        ITypeParameterSymbol => ReferenceKind.TypeParameter,
        IParameterSymbol => ReferenceKind.Parameter,
        INamespaceSymbol => ReferenceKind.Namespace,
        _ => ReferenceKind.Unknown,
    };
}