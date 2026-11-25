using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using VisualStudioTranslator.Core.Documentation;

namespace VisualStudioTranslator.Vsix.Symbols;

/// <summary>
/// Resolves the references in a piece of documentation to what the editor itself would show
/// for them: "T:Ns.Result`1" becomes "Result&lt;TValue&gt;", and "M:Ns.LanguageTag.Normalize(System.String)"
/// becomes "LanguageTag.Normalize(string)". Each part keeps its own kind, so the type, the member and the keywords
/// are coloured as in the original documentation above. The documentation id alone cannot say any of it; only the
/// compilation can.
/// </summary>
internal static class SymbolReferenceResolver
{
    // As the editor shows a reference in documentation: names only, with type parameters, the containing type for
    // members and the parameter types of methods, so a property reads "Service.BasePrice", a generic type
    // "Result<TValue>" and a method "LanguageTag.Normalize(string)".
    private static readonly SymbolDisplayFormat DisplayFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeContainingType | SymbolDisplayMemberOptions.IncludeParameters,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType,
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
                    resolved[target] = new ResolvedReference(
                        [.. symbol.ToDisplayParts(DisplayFormat).Select(part => new ResolvedPart(part.ToString(), KindOf(part)))]);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ActivityLog.LogWarning(nameof(SymbolReferenceResolver), $"Could not resolve references: {ex.Message}");
        }

        return resolved;
    }

    // A part names its symbol when it has one; otherwise its kind says what it is.
    private static ReferenceKind KindOf(SymbolDisplayPart part) => part.Symbol is { } symbol && IsNamePart(part.Kind)
        ? KindOf(symbol)
        : part.Kind switch
        {
            SymbolDisplayPartKind.Keyword => ReferenceKind.Keyword,
            SymbolDisplayPartKind.Punctuation or SymbolDisplayPartKind.Operator => ReferenceKind.Punctuation,
            SymbolDisplayPartKind.ClassName or SymbolDisplayPartKind.RecordClassName or SymbolDisplayPartKind.ErrorTypeName
                => ReferenceKind.Class,
            SymbolDisplayPartKind.StructName or SymbolDisplayPartKind.RecordStructName => ReferenceKind.Struct,
            SymbolDisplayPartKind.InterfaceName => ReferenceKind.Interface,
            SymbolDisplayPartKind.EnumName => ReferenceKind.Enum,
            SymbolDisplayPartKind.DelegateName => ReferenceKind.Delegate,
            SymbolDisplayPartKind.NamespaceName => ReferenceKind.Namespace,
            SymbolDisplayPartKind.TypeParameterName => ReferenceKind.TypeParameter,
            _ => ReferenceKind.Text,
        };

    private static bool IsNamePart(SymbolDisplayPartKind kind) =>
        kind is not (SymbolDisplayPartKind.Keyword or SymbolDisplayPartKind.Punctuation
            or SymbolDisplayPartKind.Operator or SymbolDisplayPartKind.Space or SymbolDisplayPartKind.Text);

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