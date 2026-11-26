using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text.Adornments;
using VisualStudioTranslator.Core.Documentation;
using VisualStudioTranslator.Vsix.Resources;

namespace VisualStudioTranslator.Vsix.QuickInfo;

/// <summary>A translated line of a tooltip, with the caption it stood under, if any ("Returns:").</summary>
internal sealed record TranslatedLine(TooltipLine Original, IReadOnlyList<TranslatedPiece> Pieces, TooltipLine? Caption)
{
    public string Text => string.Concat(Pieces.Select(piece => piece.Text));
}

/// <summary>Turns translated lines into the elements Quick Info knows how to draw.</summary>
internal static class TranslatedElements
{
    // The colour of control keywords ("if", "return"), so the label is clearly not part of the original text.
    private const string ControlKeyword = "keyword - control";

    /// <summary>
    /// The translated lines under a label, in the order the original tooltip has them. Code is drawn from the original
    /// runs, so it keeps its colours and links; the translated text takes the look of the line's own text.
    /// </summary>
    public static ContainerElement Create(IReadOnlyList<TranslatedLine> lines)
    {
        List<object> elements = [new ClassifiedTextElement(new ClassifiedTextRun(ControlKeyword, UiText.TranslationLabel))];

        foreach (TranslatedLine line in lines)
        {
            ClassifiedTextElement translated = new(Runs(line));

            // A caption stays as Visual Studio wrote it, already in its language, on its own line above.
            elements.Add(line.Caption is null
                ? translated
                : new ContainerElement(ContainerElementStyle.Stacked, new ClassifiedTextElement(line.Caption.Original), translated));
        }

        return new ContainerElement(ContainerElementStyle.Stacked | ContainerElementStyle.VerticalPadding, elements);
    }

    /// <summary>The runs of a translated line, ready to draw.</summary>
    public static IReadOnlyList<ClassifiedTextRun> Runs(TranslatedLine line)
    {
        IReadOnlyList<ClassifiedTextRun> original = line.Original.Original;
        ClassifiedTextRun look = original.Where((_, index) => !line.Original.Runs[index].IsCode).FirstOrDefault()
            ?? new ClassifiedTextRun(PredefinedClassificationTypeNames.NaturalLanguage, string.Empty);
        List<ClassifiedTextRun> runs = [];

        foreach (TranslatedPiece piece in line.Pieces)
        {
            if (piece.IsCode)
            {
                runs.AddRange(original.Skip(piece.FirstCodeRun).Take(piece.CodeRunCount));
            }
            else if (piece.Text.Length > 0)
            {
                runs.Add(new ClassifiedTextRun(look.ClassificationTypeName, piece.Text, look.Style));
            }
        }

        return runs;
    }
}