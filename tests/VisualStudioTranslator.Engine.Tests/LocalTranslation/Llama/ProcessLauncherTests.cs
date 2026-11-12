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

    // Pings this machine for a minute, printing as it goes: long-lived, with output to drain, and with a
    // program of its own (ping) started under the interpreter.
    private static readonly ServerStartInfo LongRunning = new(Shell, ["/c", "ping", "-n", "60", "127.0.0.1"], NoVariables);
    private static readonly ServerStartInfo ShortLived = new(Shell, ["/c", "exit", "0"], NoVariables);

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
        using ProcessLauncher launcher = new();
        using IServerProcess process = launcher.Start(ShortLived);

        (await ExitsWithinAsync(process, TimeSpan.FromSeconds(15))).Should().BeTrue();
    }

    [Fact]
    public async Task Kill_EndsAProgramThatIsStillRunning()
    {
        using ProcessLauncher launcher = new();
        using IServerProcess process = launcher.Start(LongRunning);

        process.HasExited.Should().BeFalse();

        process.Kill();

        (await ExitsWithinAsync(process, TimeSpan.FromSeconds(15))).Should().BeTrue();
    }

    [Fact]
    public async Task Kill_ProgramThatAlreadyEnded_DoesNothing()
    {
        using ProcessLauncher launcher = new();
        using IServerProcess process = launcher.Start(ShortLived);
        (await ExitsWithinAsync(process, TimeSpan.FromSeconds(15))).Should().BeTrue();

        Action act = process.Kill;

        act.Should().NotThrow();
    }

    [Fact]
    public void Start_ProgramThatDoesNotExist_Throws()
    {
        using ProcessLauncher launcher = new();
        string missing = Path.Combine(Path.GetTempPath(), "vst-no-such-program-" + Path.GetRandomFileName() + ".exe");

        Action act = () => launcher.Start(new ServerStartInfo(missing, [], NoVariables));

        act.Should().Throw<Win32Exception>();
    }

    [Fact]
    public async Task Dispose_EndsTheProgramsItStarted()
    {
        // What happens here on Dispose is what Windows does by itself when the Engine dies:
        // the last handle to the job is closed, and the programs in it are ended.
        ProcessLauncher launcher = new();
        using IServerProcess first = launcher.Start(LongRunning);
        using IServerProcess second = launcher.Start(LongRunning);

        launcher.Dispose();

        (await ExitsWithinAsync(first, TimeSpan.FromSeconds(15))).Should().BeTrue();
        (await ExitsWithinAsync(second, TimeSpan.FromSeconds(15))).Should().BeTrue();
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNothing()
    {
        ProcessLauncher launcher = new();
        launcher.Dispose();

        Action act = launcher.Dispose;

        act.Should().NotThrow();
    }

    [Fact]
    public void Start_AfterDispose_ThrowsAndStartsNothing()
    {
        ProcessLauncher launcher = new();
        launcher.Dispose();

        Action act = () => launcher.Start(LongRunning);

        act.Should().Throw<ObjectDisposedException>();
    }
}