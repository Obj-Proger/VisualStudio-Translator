using AwesomeAssertions;
using VisualStudioTranslator.Core.Presentation;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Presentation;

public sealed class PaneLayoutTests
{
    // About the width of a character of Segoe UI at 9 points.
    private const double Character = 6.5;

    [Theory]
    [InlineData(49, false)]
    [InlineData(50, true)]
    [InlineData(80, true)]
    public void UseColumns_FirstLayout_NeedsFiftyCharacters(double characters, bool expected)
    {
        PaneLayout.UseColumns(characters * Character, Character, inColumns: null).Should().Be(expected);
    }

    [Fact]
    public void UseColumns_InRows_StaysInRowsBetweenTheTwoLimits()
    {
        PaneLayout.UseColumns(47 * Character, Character, inColumns: false).Should().BeFalse();
    }

    [Fact]
    public void UseColumns_InColumns_StaysInColumnsBetweenTheTwoLimits()
    {
        PaneLayout.UseColumns(47 * Character, Character, inColumns: true).Should().BeTrue();
    }

    [Fact]
    public void UseColumns_InColumns_GoesToRowsBelowTheLowerLimit()
    {
        PaneLayout.UseColumns(43 * Character, Character, inColumns: true).Should().BeFalse();
    }

    [Fact]
    public void UseColumns_LargerFont_NeedsAWiderWindow()
    {
        PaneLayout.UseColumns(400, Character, inColumns: null).Should().BeTrue();
        PaneLayout.UseColumns(400, Character * 1.5, inColumns: null).Should().BeFalse();
    }

    [Fact]
    public void UseColumns_FontNotMeasuredYet_KeepsTheLayout()
    {
        PaneLayout.UseColumns(1000, 0, inColumns: true).Should().BeTrue();
        PaneLayout.UseColumns(1000, 0, inColumns: null).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, 0.5)]
    [InlineData(double.NaN, 0.5)]
    [InlineData(0.3, 0.3)]
    [InlineData(0.01, 0.15)]
    [InlineData(0.99, 0.85)]
    public void Share_KeepsWhatTheUserLeftWithinBounds(double? stored, double expected)
    {
        PaneLayout.Share(stored).Should().Be(expected);
    }
}