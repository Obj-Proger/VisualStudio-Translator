using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Microsoft.VisualStudio.Text.Adornments;
using TooltipRun = VisualStudioTranslator.Core.Documentation.TooltipRun;

namespace VisualStudioTranslator.Vsix.QuickInfo;

/// <summary>
/// The lines of a tooltip read off the screen, for content Quick Info does not describe: an extension that draws its
/// tooltip with an element of its own, as the web editors do for HTML and CSS. Its runs carry no colours that tell
/// code from text, so every run is text but for links, which stay as they are; names in quotes and web addresses are
/// kept out of the translation as always (<see cref="VisualStudioTranslator.Core.Documentation.TooltipText"/>).
/// <list type="bullet">
/// <item>Lines already read from the content Quick Info describes are not read twice.</item>
/// <item>A text block holding anything other than text, such as a picture, is left alone: there would be no telling
/// where in the translation to put it back.</item>
/// </list>
/// </summary>
internal static class ScreenText
{
    private const string Text = "text";

    public static IReadOnlyList<TooltipLine> Read(DependencyObject own, IReadOnlyList<TooltipLine> known)
    {
        HashSet<string> read = [.. known.Select(line => line.Text)];
        List<TooltipLine> lines = [];

        foreach (TextBlock block in TooltipVisuals.TextBlocks(own))
        {
            if (!block.IsVisible || Line(block) is not { } line || string.IsNullOrWhiteSpace(line.Text) || !read.Add(line.Text))
            {
                continue;
            }

            lines.Add(line);
        }

        return lines;
    }

    private static TooltipLine? Line(TextBlock block)
    {
        List<ClassifiedTextRun> original = [];
        List<TooltipRun> runs = [];

        foreach (Inline inline in block.Inlines)
        {
            if (TooltipVisuals.TextOf(inline) is not { } text)
            {
                return null;
            }

            original.Add(new ClassifiedTextRun(Text, text));
            runs.Add(new TooltipRun(text, TooltipVisuals.IsLink(inline)));
        }

        return runs.Count > 0 ? new TooltipLine(original, runs) : null;
    }
}