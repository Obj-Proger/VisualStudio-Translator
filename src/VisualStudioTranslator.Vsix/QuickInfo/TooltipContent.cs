using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text.Adornments;
using VisualStudioTranslator.Core.Documentation;

namespace VisualStudioTranslator.Vsix.QuickInfo;

/// <summary>One line of a tooltip as another source drew it, and the same line as the translation reads it.</summary>
/// <param name="Original">The runs as they are drawn, colours and links included.</param>
/// <param name="Runs">The same runs as text and code, one for each.</param>
internal sealed record TooltipLine(IReadOnlyList<ClassifiedTextRun> Original, IReadOnlyList<TooltipRun> Runs)
{
    public string Text => string.Concat(Original.Select(run => run.Text));
}

/// <summary>
/// Reads the lines out of what the other sources put in a tooltip. It is the editor's own description of the
/// tooltip (containers of classified text), the same for every programming language, so nothing here knows any:
/// <list type="bullet">
/// <item>a run coloured as text, a comment or natural language is prose, anything else (a keyword, a type's name,
/// punctuation) is code, which stays as it is;</item>
/// <item>a run that is a link (an error's code) is code too, so it still works after translation;</item>
/// <item>pictures and elements drawn by hand are not read: there is no telling text from code in them.</item>
/// </list>
/// </summary>
internal static class TooltipContent
{
    // The colours text is drawn in; "text" is the one Roslyn uses for documentation and compiler messages.
    private static readonly HashSet<string> ProseClassifications = new(StringComparer.OrdinalIgnoreCase)
    {
        "text",
        PredefinedClassificationTypeNames.NaturalLanguage,
        PredefinedClassificationTypeNames.Comment,
        PredefinedClassificationTypeNames.Other,
    };

    // The shapes already described in the log, so that each is described once.
    private static readonly HashSet<string> DescribedShapes = [];

    /// <summary>
    /// What a tooltip is made of, without its text: the kinds of element and the colours its runs are drawn in. It is
    /// what tells, from the log, why a tooltip in some language was not translated.
    /// </summary>
    /// <returns>The description, or <see langword="null"/> when this shape has been described before.</returns>
    public static string? DescribeOnce(IEnumerable<object> items)
    {
        SortedSet<string> elements = new(StringComparer.Ordinal);
        SortedSet<string> colours = new(StringComparer.Ordinal);

        foreach (object item in items)
        {
            Describe(item, elements, colours);
        }

        string shape = $"elements: {string.Join(", ", elements)}; colours: {string.Join(", ", colours)}";

        lock (DescribedShapes)
        {
            return DescribedShapes.Add(shape) ? shape : null;
        }
    }

    public static IReadOnlyList<TooltipLine> Read(IEnumerable<object> items)
    {
        List<TooltipLine> lines = [];

        foreach (object item in items)
        {
            Collect(item, lines);
        }

        return lines;
    }

    private static void Collect(object? element, List<TooltipLine> lines)
    {
        switch (element)
        {
            case ContainerElement container:
                foreach (object child in container.Elements)
                {
                    Collect(child, lines);
                }

                break;

            case ClassifiedTextElement text:
                AddLine([.. text.Runs], lines);
                break;

            case string text when !string.IsNullOrWhiteSpace(text):
                AddLine([new ClassifiedTextRun(PredefinedClassificationTypeNames.NaturalLanguage, text)], lines);
                break;
        }
    }

    private static void Describe(object? element, SortedSet<string> elements, SortedSet<string> colours)
    {
        if (element is null)
        {
            return;
        }

        elements.Add(element.GetType().FullName ?? element.GetType().Name);

        switch (element)
        {
            case ContainerElement container:
                foreach (object child in container.Elements)
                {
                    Describe(child, elements, colours);
                }

                break;

            case ClassifiedTextElement text:
                foreach (ClassifiedTextRun run in text.Runs)
                {
                    colours.Add((run.ClassificationTypeName ?? "(none)") + (run.NavigationAction is null ? string.Empty : " (link)"));
                }

                break;
        }
    }

    private static void AddLine(List<ClassifiedTextRun> runs, List<TooltipLine> lines)
    {
        if (runs.Count > 0)
        {
            lines.Add(new TooltipLine(runs, [.. runs.Select(run => new TooltipRun(run.Text, IsCode(run)))]));
        }
    }

    private static bool IsCode(ClassifiedTextRun run) =>
        run.NavigationAction is not null || !ProseClassifications.Contains(run.ClassificationTypeName ?? string.Empty);
}