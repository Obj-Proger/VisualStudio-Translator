namespace VisualStudioTranslator.Core.Providers.Abstractions;

/// <summary>
/// One text for a provider to translate, with what helps to understand it. Only <see cref="Text"/> is translated;
/// the rest is a hint a provider may use or leave, and the translation is cached by the text alone, so a hint can
/// make a translation better but never makes it a different one to look up.
/// </summary>
/// <param name="Text">The segment as the pipeline protects it: plain text with placeholder tokens.</param>
public sealed record SourceText(string Text)
{
    private static readonly IReadOnlyDictionary<int, string> NoCodes = new Dictionary<int, string>();

    /// <summary>
    /// The code each stand-alone token stands for, by the token's number: <c>sourceFolder</c> for <c>⟦0⟧</c>. Without
    /// it a model reads "Ensures a copy of ⟦0⟧ exists in ⟦1⟧" with no idea what the copy is of.
    /// </summary>
    public IReadOnlyDictionary<int, string> Codes { get; init; } = NoCodes;

    /// <summary>
    /// The paragraphs around the text in the same document, as plain text with code as it is written, or
    /// <see langword="null"/> when the text is all there is. Like the text itself, it is the user's code comment.
    /// </summary>
    public string? Context { get; init; }

    public static implicit operator SourceText(string text) => FromString(text);

    /// <summary>A text with nothing known about it but itself.</summary>
    public static SourceText FromString(string text) => new(text);
}