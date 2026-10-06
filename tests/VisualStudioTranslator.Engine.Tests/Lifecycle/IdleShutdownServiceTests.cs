using AwesomeAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using VisualStudioTranslator.Engine.Lifecycle;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.Lifecycle;

public sealed class IdleShutdownServiceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // Real clocks with short periods: slow enough to be reliable, fast enough to keep the tests quick.
    private static readonly EngineLifetimeOptions Quick = new()
    {
        IdleTimeout = TimeSpan.FromMilliseconds(200),
        PollInterval = TimeSpan.FromMilliseconds(20),
    };

    private static IdleShutdownService Service(ClientTracker tracker, FakeLifetime lifetime) =>
        new(tracker, lifetime, Quick, TimeProvider.System, NullLogger<IdleShutdownService>.Instance);

    [Fact]
    public async Task NoClientForTheWholeTimeout_AsksTheApplicationToStop()
    {
        FakeLifetime lifetime = new();
        using IdleShutdownService service = Service(new ClientTracker(TimeProvider.System), lifetime);

        await service.StartAsync(Token);

        await lifetime.StopRequested.WaitAsync(TimeSpan.FromSeconds(10), Token);
        await service.StopAsync(Token);
    }

    [Fact]
    public async Task ConnectedClient_KeepsTheEngineRunning_UntilItLeaves()
    {
        ClientTracker tracker = new(TimeProvider.System);
        FakeLifetime lifetime = new();
        using IdleShutdownService service = Service(tracker, lifetime);
        tracker.ClientConnected();

        await service.StartAsync(Token);
        await Task.Delay(700, Token); // several times the timeout

        lifetime.StopRequested.IsCompleted.Should().BeFalse();

        tracker.ClientDisconnected();
        await lifetime.StopRequested.WaitAsync(TimeSpan.FromSeconds(10), Token);
        await service.StopAsync(Token);
    }

    [Fact]
    public async Task HostStoppingFirst_EndsTheWatchWithoutRequestingAnotherStop()
    {
        ClientTracker tracker = new(TimeProvider.System);
        tracker.ClientConnected();
        FakeLifetime lifetime = new();
        using IdleShutdownService service = Service(tracker, lifetime);

        await service.StartAsync(Token);
        await service.StopAsync(Token);

        lifetime.StopRequested.IsCompleted.Should().BeFalse();
    }

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        private readonly TaskCompletionSource _stopRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public Task StopRequested => _stopRequested.Task;

        public void StopApplication() => _stopRequested.TrySetResult();
    }
}