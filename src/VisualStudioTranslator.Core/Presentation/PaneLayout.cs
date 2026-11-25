namespace VisualStudioTranslator.Core.Presentation;

/// <summary>
/// Whether two texts that are read side by side, such as a text and its translation, fit in two columns or should
/// stand one above the other. Measured in characters of the font they are shown in, not in pixels, so the choice is
/// the same whatever the font size and the display scaling.
/// <list type="bullet">
/// <item>Columns once each can hold <see cref="ColumnsFrom"/> characters a line: the lower end of the 50 to 75
/// characters that read comfortably. Narrower columns read worse than full-width rows.</item>
/// <item>Back to rows only below <see cref="RowsBelow"/>, so resizing a window near the limit does not make the
/// layout flicker between the two.</item>
/// </list>
/// </summary>
public static class PaneLayout
{
    public const double ColumnsFrom = 50;

    public const double RowsBelow = 44;

    /// <summary>The narrowest a text may be made with the splitter, in characters.</summary>
    public const double MinColumnCharacters = 20;

    /// <summary>The lowest a text may be made with the splitter, in lines.</summary>
    public const double MinRowLines = 3;

    /// <summary>
    /// Whether to show the texts in columns.
    /// </summary>
    /// <param name="columnWidth">How wide each column would be, after margins and the gap between them.</param>
    /// <param name="characterWidth">The average width of a character of the font.</param>
    /// <param name="inColumns">The layout now, or <see langword="null"/> before there has been one.</param>
    public static bool UseColumns(double columnWidth, double characterWidth, bool? inColumns)
    {
        if (characterWidth <= 0 || double.IsNaN(columnWidth))
        {
            return inColumns ?? false;
        }

        double characters = columnWidth / characterWidth;

        return inColumns == true ? characters >= RowsBelow : characters >= ColumnsFrom;
    }

    /// <summary>
    /// The share of the room the first text keeps, from what the user left it at: an unknown or broken value gives
    /// half, and no text is squeezed to nothing.
    /// </summary>
    public static double Share(double? stored) =>
        stored is { } share && !double.IsNaN(share) ? Math.Min(0.85, Math.Max(0.15, share)) : 0.5;
}