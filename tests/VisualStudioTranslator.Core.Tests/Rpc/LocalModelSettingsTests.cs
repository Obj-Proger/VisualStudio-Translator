using AwesomeAssertions;
using VisualStudioTranslator.Core.Rpc;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Rpc;

public sealed class LocalModelSettingsTests
{
    [Fact]
    public void Defaults_AreTheModelTheMachineSuitsOnAGraphicsCard()
    {
        LocalModelSettings settings = new();

        settings.Model.Should().Be(LocalModelChoice.Automatic);
        settings.UseGraphicsCard.Should().BeTrue();
    }

    [Fact]
    public void ModelTags_NameEveryChoiceInTheOrderOfItsValue()
    {
        LocalModelChoice[] choices = Enum.GetValues<LocalModelChoice>();

        LocalModelSettings.ModelTags.Should().HaveCount(choices.Length);
        choices.Select(choice => LocalModelSettings.ParseModel(LocalModelSettings.ModelTags[(int)choice]))
            .Should().Equal(choices);
    }

    [Theory]
    [InlineData("quality", LocalModelChoice.Quality)]
    [InlineData("Light", LocalModelChoice.Light)]
    [InlineData("auto", LocalModelChoice.Automatic)]
    public void ParseModel_ReadsTheStoredTag(string tag, LocalModelChoice expected)
    {
        LocalModelSettings.ParseModel(tag).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("huge")]
    public void ParseModel_NothingOrATagFromAnotherVersion_IsAutomatic(string? tag)
    {
        LocalModelSettings.ParseModel(tag).Should().Be(LocalModelChoice.Automatic);
    }

    [Fact]
    public void Settings_WithTheSameValues_AreEqual()
    {
        new LocalModelSettings { Model = LocalModelChoice.Light, UseGraphicsCard = false }
            .Should().Be(new LocalModelSettings { Model = LocalModelChoice.Light, UseGraphicsCard = false });
    }
}