using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using VisualStudioTranslator.Engine.LocalTranslation.Llama;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.LocalTranslation.Llama;

public sealed class GpuVerdictTests : IDisposable
{
    private const string Server = @"C:\llama\runtime\b12317-vulkan\llama-server.exe";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vst-gpu-verdict-" + Path.GetRandomFileName());
    private readonly FakeTimeProvider _time = new();
    private readonly GpuVerdict _verdict;

    public GpuVerdictTests() => _verdict = new GpuVerdict(Path.Combine(_directory, "gpu-unusable.txt"), _time);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private void Fail(int times)
    {
        for (int i = 0; i < times; i++)
        {
            _verdict.MarkUnusable(Server);
        }
    }

    private bool UnusableAfter(TimeSpan wait)
    {
        _time.Advance(wait);
        return _verdict.IsUnusable(Server);
    }

    [Fact]
    public void IsUnusable_NothingRecorded_IsFalse()
    {
        _verdict.IsUnusable(Server).Should().BeFalse();
    }

    [Fact]
    public void IsUnusable_AfterAFirstFailure_HoldsForADay()
    {
        Fail(1);

        UnusableAfter(TimeSpan.FromHours(23)).Should().BeTrue();
        UnusableAfter(TimeSpan.FromHours(1)).Should().BeFalse();
    }

    [Fact]
    public void IsUnusable_EachFailureThatFollows_DoublesThePause()
    {
        Fail(3);

        UnusableAfter(TimeSpan.FromDays(4) - TimeSpan.FromMinutes(1)).Should().BeTrue();
        UnusableAfter(TimeSpan.FromMinutes(1)).Should().BeFalse();
    }

    [Fact]
    public void IsUnusable_ManyFailures_PauseForAMonthAtMost()
    {
        Fail(20);

        UnusableAfter(TimeSpan.FromDays(30) - TimeSpan.FromMinutes(1)).Should().BeTrue();
        UnusableAfter(TimeSpan.FromMinutes(1)).Should().BeFalse();
    }

    [Fact]
    public void MarkUsable_ForgetsEveryFailure()
    {
        Fail(3);

        _verdict.MarkUsable();
        _verdict.IsUnusable(Server).Should().BeFalse();

        Fail(1);
        UnusableAfter(TimeSpan.FromDays(1)).Should().BeFalse();
    }

    [Fact]
    public void IsUnusable_AnotherBuildOfTheServer_StartsWithACleanSlate()
    {
        Fail(3);

        _verdict.IsUnusable(@"C:\llama\runtime\b13000-vulkan\llama-server.exe").Should().BeFalse();
    }

    [Fact]
    public void MarkUnusable_AnotherBuild_CountsFromOneAgain()
    {
        Fail(5);
        _verdict.MarkUnusable(@"C:\llama\runtime\b13000-vulkan\llama-server.exe");

        _time.Advance(TimeSpan.FromDays(1));

        _verdict.IsUnusable(@"C:\llama\runtime\b13000-vulkan\llama-server.exe").Should().BeFalse();
    }

    [Fact]
    public void IsUnusable_FileInAnotherShape_CountsForNothing()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "gpu-unusable.txt"), Server);

        _verdict.IsUnusable(Server).Should().BeFalse();
    }
}