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

        Stack<DependencyObject> pending = new();
        pending.Push(root);

        while (pending.Count > 0)
        {
            DependencyObject current = pending.Pop();
            if (ReferenceEquals(current, own) || current is ButtonBase or TextBoxBase)
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

    /// <summary>Whether the run is a link, or holds one.</summary>
    public static bool IsLink(Inline inline) => inline switch
    {
        Hyperlink => true,
        Span span => span.Inlines.Any(IsLink),
        _ => false,
    };
}