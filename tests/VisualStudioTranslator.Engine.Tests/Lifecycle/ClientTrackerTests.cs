using AwesomeAssertions;
using VisualStudioTranslator.Engine.Lifecycle;
using VisualStudioTranslator.Engine.Tests.Support;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.Lifecycle;

public sealed class ClientTrackerTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch.AddDays(100);

    [Fact]
    public void IdleTime_StartsWhenTheTrackerIsCreated()
    {
        ManualTime time = new(Start);
        ClientTracker tracker = new(time);

        tracker.IdleTime.Should().Be(TimeSpan.Zero);

        time.Now = Start.AddSeconds(30);
        tracker.IdleTime.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void IdleTime_IsZeroForAsLongAsAClientIsConnected()
    {
        ManualTime time = new(Start);
        ClientTracker tracker = new(time);

        tracker.ClientConnected();
        time.Now = Start.AddHours(1);

        tracker.Connected.Should().Be(1);
        tracker.IdleTime.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void IdleTime_RestartsFromTheMomentTheLastClientLeaves()
    {
        ManualTime time = new(Start);
        ClientTracker tracker = new(time);
        tracker.ClientConnected();

        time.Now = Start.AddHours(1);
        tracker.ClientDisconnected();
        time.Now = time.Now.AddSeconds(10);

        tracker.IdleTime.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void IdleTime_StaysZeroWhileAnyClientRemains()
    {
        ManualTime time = new(Start);
        ClientTracker tracker = new(time);
        tracker.ClientConnected();
        tracker.ClientConnected();

        tracker.ClientDisconnected();
        time.Now = Start.AddMinutes(5);

        tracker.Connected.Should().Be(1);
        tracker.IdleTime.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ClientDisconnected_WithNoneConnected_NeverGoesNegative()
    {
        ClientTracker tracker = new(new ManualTime(Start));

        tracker.ClientDisconnected();

        tracker.Connected.Should().Be(0);
    }
}