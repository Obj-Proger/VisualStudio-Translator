using AwesomeAssertions;
using VisualStudioTranslator.Engine.LocalTranslation.Llama;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.LocalTranslation.Llama;

public sealed class GpuRankingTests : IDisposable
{
    private const string Server = @"C:\llama\runtime\b12317-vulkan\llama-server.exe";

    private static readonly GpuDevice Nvidia = new("Vulkan0", "NVIDIA GeForce RTX 4070 Laptop GPU", 7963);
    private static readonly GpuDevice Radeon = new("Vulkan1", "AMD Radeon 780M Graphics", 48899);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vst-gpu-ranking-" + Path.GetRandomFileName());
    private readonly GpuRanking _ranking;

    public GpuRankingTests() => _ranking = new GpuRanking(Path.Combine(_directory, "gpu-ranking.txt"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Get_NothingSaved_IsNull()
    {
        _ranking.Get(Server, [Nvidia, Radeon]).Should().BeNull();
    }

    [Fact]
    public void Get_AfterSave_GivesTheCardsInTheSavedOrder()
    {
        _ranking.Save(Server, [Radeon, Nvidia]);

        _ranking.Get(Server, [Nvidia, Radeon]).Should().Equal(Radeon, Nvidia);
    }

    [Fact]
    public void Get_CardsListedUnderNewIds_AreStillMatchedByName()
    {
        _ranking.Save(Server, [Nvidia, Radeon]);

        IReadOnlyList<GpuDevice>? ranked = _ranking.Get(Server, [Radeon with { Id = "Vulkan0" }, Nvidia with { Id = "Vulkan1" }]);

        ranked!.Select(device => device.Id).Should().Equal("Vulkan1", "Vulkan0");
    }

    [Fact]
    public void Get_AnotherBuildOfTheServer_IsNull()
    {
        _ranking.Save(Server, [Nvidia, Radeon]);

        _ranking.Get(@"C:\llama\runtime\b13000-vulkan\llama-server.exe", [Nvidia, Radeon]).Should().BeNull();
    }

    [Fact]
    public void Get_CardAddedOrTakenAway_IsNull()
    {
        _ranking.Save(Server, [Nvidia, Radeon]);

        _ranking.Get(Server, [Nvidia]).Should().BeNull();
        _ranking.Get(Server, [Nvidia, Radeon, new GpuDevice("Vulkan2", "Intel Arc", 8000)]).Should().BeNull();
    }

    [Fact]
    public void Get_TwoCardsOfTheSameModel_ComeOutAsTwo()
    {
        GpuDevice first = new("Vulkan0", "NVIDIA GeForce RTX 4090", 24000);
        GpuDevice second = first with { Id = "Vulkan1" };
        _ranking.Save(Server, [second, first]);

        _ranking.Get(Server, [first, second]).Should().HaveCount(2).And.OnlyHaveUniqueItems();
    }
}