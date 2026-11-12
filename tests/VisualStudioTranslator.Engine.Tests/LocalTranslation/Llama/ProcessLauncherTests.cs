using System.ComponentModel;
using AwesomeAssertions;
using VisualStudioTranslator.Engine.LocalTranslation.Llama;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.LocalTranslation.Llama;

// Starts real programs: the command interpreter every Windows has stands in for the model server.
public sealed class ProcessLauncherTests
{
    private static readonly string Shell = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
    private static readonly Dictionary<string, string> NoVariables = [];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<bool> ExitsWithinAsync(IServerProcess process, TimeSpan limit)
    {
        DateTime deadline = DateTime.UtcNow + limit;

        while (!process.HasExited && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, Token);
        }

        return process.HasExited;
    }

    [Fact]
    public async Task Start_RunsTheProgramUntilItEnds()
    {
        using IServerProcess process = new ProcessLauncher().Start(new ServerStartInfo(Shell, ["/c", "exit", "0"], NoVariables));

        (await ExitsWithinAsync(process, TimeSpan.FromSeconds(15))).Should().BeTrue();
    }

    [Fact]
    public async Task Kill_EndsAProgramThatIsStillRunning()
    {
        // Pings this machine for a minute, printing as it goes: long-lived, and with output to drain.
        using IServerProcess process = new ProcessLauncher().Start(
            new ServerStartInfo(Shell, ["/c", "ping", "-n", "60", "127.0.0.1"], NoVariables));

        process.HasExited.Should().BeFalse();

        process.Kill();

        (await ExitsWithinAsync(process, TimeSpan.FromSeconds(15))).Should().BeTrue();
    }

    [Fact]
    public async Task Kill_ProgramThatAlreadyEnded_DoesNothing()
    {
        using IServerProcess process = new ProcessLauncher().Start(new ServerStartInfo(Shell, ["/c", "exit", "0"], NoVariables));
        (await ExitsWithinAsync(process, TimeSpan.FromSeconds(15))).Should().BeTrue();

        Action act = process.Kill;

        act.Should().NotThrow();
    }

    [Fact]
    public void Start_ProgramThatDoesNotExist_Throws()
    {
        string missing = Path.Combine(Path.GetTempPath(), "vst-no-such-program-" + Path.GetRandomFileName() + ".exe");

        Action act = () => new ProcessLauncher().Start(new ServerStartInfo(missing, [], NoVariables));

        act.Should().Throw<Win32Exception>();
    }
}