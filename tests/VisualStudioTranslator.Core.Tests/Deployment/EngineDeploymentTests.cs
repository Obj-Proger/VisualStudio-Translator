using AwesomeAssertions;
using VisualStudioTranslator.Core.Deployment;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Deployment;

public sealed class EngineDeploymentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vst-deploy-" + Path.GetRandomFileName());
    private readonly string _source;
    private readonly string _deployments;

    public EngineDeploymentTests()
    {
        _source = Path.Combine(_root, "extension", "Engine");
        _deployments = Path.Combine(_root, "engine");
        Directory.CreateDirectory(Path.Combine(_source, "runtimes"));
        File.WriteAllText(Path.Combine(_source, EngineDeployment.ExecutableName), "engine");
        File.WriteAllText(Path.Combine(_source, "runtimes", "native.dll"), "native");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Deploy_CopiesEveryFileAndReturnsTheCopysExecutable()
    {
        string executable = EngineDeployment.Deploy(_source, _deployments, "1.2.3.4");

        executable.Should().StartWith(Path.Combine(_deployments, "1.2.3.4-")).And.EndWith(EngineDeployment.ExecutableName);
        File.ReadAllText(executable).Should().Be("engine");
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(executable)!, "runtimes", "native.dll")).Should().Be("native");
    }

    [Fact]
    public void Deploy_Again_UsesTheSameCopyWithoutCopying()
    {
        string first = EngineDeployment.Deploy(_source, _deployments, "1.0");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(first)!, "marker.txt"), "still here");

        string second = EngineDeployment.Deploy(_source, _deployments, "1.0");

        second.Should().Be(first);
        File.Exists(Path.Combine(Path.GetDirectoryName(second)!, "marker.txt")).Should().BeTrue();
    }

    [Fact]
    public void Deploy_ChangedFilesWithTheSameVersion_GetAFolderOfTheirOwnAndTheOldOneGoes()
    {
        string first = EngineDeployment.Deploy(_source, _deployments, "1.0");
        File.WriteAllText(Path.Combine(_source, EngineDeployment.ExecutableName), "engine, rebuilt");

        string second = EngineDeployment.Deploy(_source, _deployments, "1.0");

        second.Should().NotBe(first);
        File.ReadAllText(second).Should().Be("engine, rebuilt");
        Directory.Exists(Path.GetDirectoryName(first)).Should().BeFalse();
    }

    [Fact]
    public void Deploy_LeavesNoTemporaryFolderBehind()
    {
        EngineDeployment.Deploy(_source, _deployments, "1.0");

        Directory.GetDirectories(_deployments).Should().ContainSingle();
    }

    [Fact]
    public void Deploy_KeepsAFreshTemporaryFolderOfAnotherStartButRemovesAnAbandonedOne()
    {
        string fresh = Path.Combine(_deployments, ".tmp-fresh");
        string abandoned = Path.Combine(_deployments, ".tmp-abandoned");
        Directory.CreateDirectory(fresh);
        Directory.CreateDirectory(abandoned);
        Directory.SetCreationTimeUtc(abandoned, DateTime.UtcNow.AddDays(-1));

        EngineDeployment.Deploy(_source, _deployments, "1.0");

        Directory.Exists(fresh).Should().BeTrue();
        Directory.Exists(abandoned).Should().BeFalse();
    }

    [Fact]
    public void Deploy_IncompleteSource_Throws()
    {
        File.Delete(Path.Combine(_source, EngineDeployment.ExecutableName));

        Action deploy = () => EngineDeployment.Deploy(_source, _deployments, "1.0");

        deploy.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void FolderName_VersionWithCharactersAFolderCannotHave_IsMadeSafe()
    {
        EngineDeployment.FolderName(_source, "1.0/beta:2").Should().StartWith("1.0_beta_2-");
    }
}