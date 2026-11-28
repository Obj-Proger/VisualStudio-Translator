using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace VisualStudioTranslator.Vsix.QuickInfo;

/// <summary>
/// The text blocks a tooltip is drawn with, as they are on screen: for the tooltips whose content cannot be read any
/// other way, and for putting a translation in place. Only the tooltip a given element of this extension is in is
/// looked through, never anything beyond it.
/// </summary>
internal static class TooltipVisuals
{
    /// <summary>
    /// The text blocks of the tooltip <paramref name="own"/> is in, in the order they are drawn, but for those in
    /// <paramref name="own"/> itself and those on buttons, which are Visual Studio's own words ("Show potential fixes"),
    /// not the tooltip's.
    /// </summary>
    public static IEnumerable<TextBlock> TextBlocks(DependencyObject own)
    {
        DependencyObject root = own;
        while (VisualTreeHelper.GetParent(root) is { } parent)
        {
            root = parent;
        }

        return Within(root, own);
    }

    /// <summary>
    /// The text blocks under <paramref name="root"/>, such as a popup another component draws, in the order they are
    /// drawn, but for those in <paramref name="excluded"/> and those on buttons.
    /// </summary>
    public static IEnumerable<TextBlock> Within(DependencyObject root, DependencyObject? excluded = null)
    {
        Stack<DependencyObject> pending = new();
        pending.Push(root);

        while (pending.Count > 0)
        {
            DependencyObject current = pending.Pop();
            if (ReferenceEquals(current, excluded) || current is ButtonBase or TextBoxBase)
            {
                continue;
            }

            if (current is TextBlock block)
            {
                yield return block;
            }

            for (int index = VisualTreeHelper.GetChildrenCount(current) - 1; index >= 0; index--)
            {
                pending.Push(VisualTreeHelper.GetChild(current, index));
            }
        }
    }

    /// <summary>The text of a run of a text block, or <see langword="null"/> for what is not text, such as a picture.</summary>
    public static string? TextOf(Inline inline) => inline switch
    {
        Run run => run.Text,
        LineBreak => "\n",
        Span span => TextOf(span.Inlines),
        _ => null,
    };

    private static string? TextOf(InlineCollection inlines)
    {
        StringBuilder text = new();

        foreach (Inline inline in inlines)
        {
            if (TextOf(inline) is not { } part)
            {
                return null;
            }

            text.Append(part);
        }

        return text.ToString();
    }

    /// <summary>
    /// The colour the text of a popup is drawn in: the one most of its letters have, since the documentation is longer
    /// than the signature above it. Taken from the runs rather than from the text blocks, because some language
    /// services colour every run, the text too, with a colour a little other than the popup's own.
    /// </summary>
    public static Color? TextColour(IEnumerable<TextBlock> blocks)
    {
        Dictionary<Color, int> letters = [];

        foreach (TextBlock block in blocks)
        {
            foreach (Inline inline in block.Inlines)
            {
                if (inline.Foreground is SolidColorBrush brush && TextOf(inline) is { } text)
                {
                    letters.TryGetValue(brush.Color, out int count);
                    letters[brush.Color] = count + text.Count(char.IsLetter);
                }
            }
        }

        return letters.Count == 0 ? null : letters.OrderByDescending(pair => pair.Value).First().Key;
    }

    /// <summary>
    /// Whether the run is drawn as code rather than as text: bold, as a parameter's name is, or in a colour other than
    /// <paramref name="text"/>, the colour of the popup's text (<see cref="TextColour"/>), as names and keywords are.
    /// </summary>
    public static bool LooksLikeCode(Inline inline, Color? text) =>
        inline.FontWeight.ToOpenTypeWeight() > FontWeights.Normal.ToOpenTypeWeight()
        || (text is { } colour && inline.Foreground is SolidColorBrush brush && brush.Color != colour);

    /// <summary>Whether the run is a link, or holds one.</summary>
    public static bool IsLink(Inline inline) => inline switch
    {
        Hyperlink => true,
        Span span => span.Inlines.Any(IsLink),
        _ => false,
    };
}