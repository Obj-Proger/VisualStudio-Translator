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
    /// Whether the run is drawn as code rather than as text: bold, as a parameter's name is, or in a colour of its own
    /// rather than the colour of the text around it, as names and keywords are.
    /// </summary>
    public static bool LooksLikeCode(Inline inline, TextBlock block) =>
        inline.FontWeight.ToOpenTypeWeight() > FontWeights.Normal.ToOpenTypeWeight()
        || !SameColour(inline.Foreground, block.Foreground);

    private static bool SameColour(Brush? one, Brush? other) =>
        ReferenceEquals(one, other)
        || (one is SolidColorBrush first && other is SolidColorBrush second && first.Color == second.Color);

    /// <summary>Whether the run is a link, or holds one.</summary>
    public static bool IsLink(Inline inline) => inline switch
    {
        Hyperlink => true,
        Span span => span.Inlines.Any(IsLink),
        _ => false,
    };
}