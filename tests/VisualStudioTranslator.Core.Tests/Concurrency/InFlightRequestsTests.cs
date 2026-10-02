using AwesomeAssertions;
using VisualStudioTranslator.Core.Concurrency;
using Xunit;

namespace VisualStudioTranslator.Core.Tests.Concurrency;

public sealed class InFlightRequestsTests
{
    private static TaskCompletionSource<int> NewCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task GetOrStart_SameKeyWhileRunning_SharesOneOperation()
    {
        InFlightRequests<int> requests = new();
        TaskCompletionSource<int> completion = NewCompletion();
        int starts = 0;

        Task<int> first = requests.GetOrStart("k", () => { Interlocked.Increment(ref starts); return completion.Task; });
        Task<int> second = requests.GetOrStart("k", () => { Interlocked.Increment(ref starts); return completion.Task; });

        first.Should().BeSameAs(second);

        completion.SetResult(42);
        (await first).Should().Be(42);
        (await second).Should().Be(42);
        starts.Should().Be(1);
    }

    [Fact]
    public async Task GetOrStart_DifferentKeys_RunSeparately()
    {
        InFlightRequests<int> requests = new();

        Task<int> a = requests.GetOrStart("a", () => Task.FromResult(1));
        Task<int> b = requests.GetOrStart("b", () => Task.FromResult(2));

        (await a).Should().Be(1);
        (await b).Should().Be(2);
    }

    [Fact]
    public async Task GetOrStart_AfterTheOperationFinished_StartsANewOne()
    {
        InFlightRequests<int> requests = new();
        int starts = 0;

        await requests.GetOrStart("k", () => Task.FromResult(Interlocked.Increment(ref starts)));
        int second = await requests.GetOrStart("k", () => Task.FromResult(Interlocked.Increment(ref starts)));

        second.Should().Be(2);
    }

    [Fact]
    public async Task GetOrStart_FailedOperation_IsNotRemembered()
    {
        InFlightRequests<int> requests = new();

        Func<Task> failing = () => requests.GetOrStart("k", () => throw new InvalidOperationException("boom"));
        await failing.Should().ThrowAsync<InvalidOperationException>();

        (await requests.GetOrStart("k", () => Task.FromResult(7))).Should().Be(7);
    }

    [Fact]
    public async Task GetOrStart_StartThatThrowsImmediately_GivesAFaultedTaskNotAnException()
    {
        InFlightRequests<int> requests = new();

        // If GetOrStart let the exception escape, this line itself would throw and fail the test.
        Task<int> task = requests.GetOrStart("k", () => throw new InvalidOperationException("boom"));

        Func<Task> awaiting = () => task;
        await awaiting.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CompletesWithinAsync_AlreadyCompleted_IsTrue()
    {
        (await Task.CompletedTask.CompletesWithinAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken))
            .Should().BeTrue();
    }

    [Fact]
    public async Task CompletesWithinAsync_FinishesInTime_IsTrue()
    {
        TaskCompletionSource<int> completion = NewCompletion();
        Task<bool> waiting = completion.Task.CompletesWithinAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        completion.SetResult(1);

        (await waiting).Should().BeTrue();
    }

    [Fact]
    public async Task CompletesWithinAsync_TimeRunsOut_IsFalseAndTheTaskKeepsRunning()
    {
        TaskCompletionSource<int> completion = NewCompletion();

        bool completed = await completion.Task.CompletesWithinAsync(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);

        completed.Should().BeFalse();
        completion.Task.IsCompleted.Should().BeFalse();
        completion.SetResult(1);
        (await completion.Task).Should().Be(1);
    }

    [Fact]
    public async Task CompletesWithinAsync_CancelledByTheCaller_Throws()
    {
        TaskCompletionSource<int> completion = NewCompletion();
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        Func<Task> act = () => completion.Task.CompletesWithinAsync(TimeSpan.FromSeconds(10), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}