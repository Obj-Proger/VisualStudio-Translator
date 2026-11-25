using AwesomeAssertions;
using VisualStudioTranslator.Core.Documentation;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Documentation;

public sealed class CommentTextTests
{
    [Fact]
    public void Strip_LineComments_LoseTheirMarkers()
    {
        CommentText.Strip("// Gets the name\n    // of the user.").Should().Be("Gets the name\nof the user.");
    }

    [Fact]
    public void Strip_BlockComment_LosesItsMarkersAndTheStarsBeforeEachLine()
    {
        CommentText.Strip("/*\n * Opens the file.\n * Closes it.\n */").Should().Be("Opens the file.\nCloses it.");
    }

    [Fact]
    public void Strip_VisualBasicComment_LosesItsMarker()
    {
        CommentText.Strip("''' Returns the count.").Should().Be("Returns the count.");
    }

    [Fact]
    public void Strip_DocumentationComment_KeepsTheProseAndWhatReferencesStandFor()
    {
        string selection =
            "/// <summary>Returns the <see cref=\"T:System.String\"/> for <paramref name=\"id\"/>.</summary>\n" +
            "/// <param name=\"id\">The <c>key</c> &amp; more.</param>";

        CommentText.Strip(selection).Should().Be("Returns the System.String for id.\n\n\n\n\nid: The key & more.");
    }

    [Fact]
    public void Strip_TextThatIsNotAComment_ComesBackAsItWas()
    {
        const string Text = "x = a * b; // not a comment line\n<summary>kept</summary>";

        CommentText.Strip(Text).Should().Be(Text);
    }
}