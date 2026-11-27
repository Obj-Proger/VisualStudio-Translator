using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using TranslatedPiece = VisualStudioTranslator.Core.Documentation.TranslatedPiece;

namespace VisualStudioTranslator.Vsix.QuickInfo;

/// <summary>
/// The experimental way of showing a translation: in place of the original lines, in the tooltip itself, rather than
/// below them. Quick Info offers no way to change what another source shows, so this goes by what is on screen, and
/// as carefully as that allows:
/// <list type="bullet">
/// <item>only inside the tooltip this element is in, never beyond it;</item>
/// <item>only a line whose text block holds exactly the original line, run for run, and which no other block holds;
/// if any line has no such block, nothing is changed at all, and the translation is shown below as usual;</item>
/// <item>the code in a line stays the very same runs, colours and links included, and the translated text takes
/// the look of the line's own text;</item>
/// <item>the original runs are kept, so <see cref="Toggle"/> puts them back and the translation again.</item>
/// </list>
/// </summary>
internal sealed class InPlaceTranslation
{
    private readonly IReadOnlyList<Swap> _swaps;

    private InPlaceTranslation(IReadOnlyList<Swap> swaps)
    {
        _swaps = swaps;
    }

    /// <summary>Whether the tooltip shows the translation, rather than the original.</summary>
    public bool ShowsTranslation { get; private set; } = true;

    /// <summary>Puts the translation in place of every line, or, if any line cannot be found, changes nothing.</summary>
    /// <param name="own">An element of this extension's in the tooltip, which tells which tooltip it is.</param>
    /// <returns>The change made, or <see langword="null"/> when nothing was changed.</returns>
    public static InPlaceTranslation? TryApply(DependencyObject own, IReadOnlyList<TranslatedLine> lines)
    {
        List<TextBlock> blocks = [.. TooltipVisuals.TextBlocks(own)];
        HashSet<TextBlock> taken = [];
        List<Swap> swaps = [];

        foreach (TranslatedLine line in lines)
        {
            List<TextBlock> matches = [.. blocks.Where(block => !taken.Contains(block) && Holds(block, line.Original))];

            if (matches.Count != 1)
            {
                return null;
            }

            TextBlock block = matches[0];
            List<Inline> original = [.. block.Inlines];

            taken.Add(block);
            swaps.Add(new Swap(block, original, Translated(original, line)));
        }

        foreach (Swap swap in swaps)
        {
            swap.Show(translated: true);
        }

        return new InPlaceTranslation(swaps);
    }

    /// <summary>Shows the original where the translation is, and the other way round.</summary>
    public void Toggle()
    {
        ShowsTranslation = !ShowsTranslation;

        foreach (Swap swap in _swaps)
        {
            swap.Show(ShowsTranslation);
        }
    }

    private static bool Holds(TextBlock block, TooltipLine line)
    {
        if (block.Inlines.Count != line.Original.Count)
        {
            return false;
        }

        int index = 0;

        foreach (Inline inline in block.Inlines)
        {
            if (!string.Equals(TooltipVisuals.TextOf(inline), line.Original[index++].Text, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static List<Inline> Translated(List<Inline> original, TranslatedLine line)
    {
        Inline? look = original.Where((_, index) => !line.Original.Runs[index].IsCode).FirstOrDefault();
        List<Inline> translated = [];

        foreach (TranslatedPiece piece in line.Pieces)
        {
            if (piece.IsCode)
            {
                translated.AddRange(original.Skip(piece.FirstCodeRun).Take(piece.CodeRunCount));
            }
            else if (piece.Text.Length > 0)
            {
                translated.Add(LookingLike(look, piece.Text));
            }
        }

        return translated;
    }

    private static Run LookingLike(Inline? look, string text)
    {
        Run run = new(text);

        if (look is not null)
        {
            run.Foreground = look.Foreground;
            run.FontFamily = look.FontFamily;
            run.FontSize = look.FontSize;
            run.FontStyle = look.FontStyle;
            run.FontWeight = look.FontWeight;
            run.TextDecorations = look.TextDecorations;
        }

        return run;
    }

    // One text block, and the runs it holds either way. A run belongs to one block at a time, which is why the
    // block is emptied before the other runs go in; the code runs are in both lists.
    private sealed record Swap(TextBlock Block, IReadOnlyList<Inline> Original, IReadOnlyList<Inline> Translation)
    {
        public void Show(bool translated)
        {
            Block.Inlines.Clear();
            Block.Inlines.AddRange(translated ? Translation : Original);
        }
    }
}