using AwesomeAssertions;
using VisualStudioTranslator.Engine.LocalTranslation.Llama;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.LocalTranslation.Llama;

public sealed class GpuDevicesTests
{
    // What llama-server b12317 printed on a laptop with a discrete and a built-in card.
    private const string TwoCards =
        "Available devices:\r\n" +
        "  Vulkan0: NVIDIA GeForce RTX 4070 Laptop GPU (7963 MiB, 7195 MiB free)\r\n" +
        "  Vulkan1: AMD Radeon 780M Graphics (48899 MiB, 46454 MiB free)\r\n";

    [Fact]
    public void Parse_ListOfCards_GivesEachWithItsIdNameAndMemory()
    {
        GpuDevices.Parse(TwoCards).Should().Equal(
            new GpuDevice("Vulkan0", "NVIDIA GeForce RTX 4070 Laptop GPU", 7963),
            new GpuDevice("Vulkan1", "AMD Radeon 780M Graphics", 48899));
    }

    [Fact]
    public void Parse_LinesAroundTheList_AreIgnored()
    {
        string output = "ggml_vulkan: Found 2 Vulkan devices:\n" + TwoCards + "load_backend: loaded CPU backend\n";

        GpuDevices.Parse(output).Should().HaveCount(2);
    }

    [Fact]
    public void Parse_NoCards_IsAnEmptyList()
    {
        GpuDevices.Parse("Available devices:\n").Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("error: unknown argument: --list-devices")]
    public void Parse_NotAListOfDevices_IsNull(string output)
    {
        GpuDevices.Parse(output).Should().BeNull();
    }
}