using AwesomeAssertions;
using VisualStudioTranslator.Engine.Lifecycle;
using Xunit;

namespace VisualStudioTranslator.Engine.Tests.Lifecycle;

public sealed class SingleInstanceGuardTests
{
    // A fresh name per test, so tests cannot interfere with each other or with a real Engine.
    private static string NewName() => $"Local\\vst-test-{Guid.NewGuid():N}";

    [Fact]
    public void TryAcquire_FirstCaller_GetsTheGuard()
    {
        using SingleInstanceGuard? guard = SingleInstanceGuard.TryAcquire(NewName());

        guard.Should().NotBeNull();
    }

    [Fact]
    public void TryAcquire_WhileAnotherHoldsIt_ReturnsNull()
    {
        string name = NewName();
        using SingleInstanceGuard? first = SingleInstanceGuard.TryAcquire(name);

        SingleInstanceGuard.TryAcquire(name).Should().BeNull();
    }

    [Fact]
    public void TryAcquire_AfterTheHolderIsDisposed_Succeeds()
    {
        string name = NewName();
        SingleInstanceGuard.TryAcquire(name)!.Dispose();

        using SingleInstanceGuard? second = SingleInstanceGuard.TryAcquire(name);

        second.Should().NotBeNull();
    }

    [Fact]
    public async Task Dispose_FromAnotherThread_DoesNotThrow()
    {
        // The reason this is a semaphore: the thread that took it is not the one that lets go of it.
        SingleInstanceGuard guard = SingleInstanceGuard.TryAcquire(NewName())!;

        Func<Task> act = () => Task.Run(guard.Dispose, TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Dispose_Twice_DoesNotThrow()
    {
        SingleInstanceGuard guard = SingleInstanceGuard.TryAcquire(NewName())!;

        guard.Dispose();
        Action act = guard.Dispose;

        act.Should().NotThrow();
    }
}