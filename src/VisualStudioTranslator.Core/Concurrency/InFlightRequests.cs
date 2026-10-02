namespace VisualStudioTranslator.Core.Concurrency;

/// <summary>
/// Lets callers that ask for the same thing share one running operation, instead of each
/// starting its own. The operation is shared only while it runs: once it has finished (or
/// failed) the next request starts a fresh one, so a failure is never remembered.
/// </summary>
public sealed class InFlightRequests<TResult>
{
    private readonly object _gate = new();

    // Dictionary<string, ...> compares keys ordinally by default, which is what the keys need.
    private readonly Dictionary<string, Task<TResult>> _running = [];

    public Task<TResult> GetOrStart(string key, Func<Task<TResult>> start)
    {
        lock (_gate)
        {
            if (_running.TryGetValue(key, out Task<TResult> existing))
            {
                return existing;
            }

            // Task.Run, so a start that throws before it even returns its task still gives the
            // caller a faulted task to await, never an exception thrown from here.
            Task<TResult> task = Task.Run(start);
            _running[key] = task;

            // Runs inline as the task completes, ahead of any caller awaiting it, so by the time
            // an awaiter resumes the entry is already gone.
            _ = task.ContinueWith(
                completed => Forget(key, completed),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return task;
        }
    }

    private void Forget(string key, Task<TResult> completed)
    {
        // Reading Exception marks a failure as observed; whoever awaits the task still sees it.
        _ = completed.Exception;

        lock (_gate)
        {
            if (_running.TryGetValue(key, out Task<TResult> current) && ReferenceEquals(current, completed))
            {
                _running.Remove(key);
            }
        }
    }
}

public static class WaitExtensions
{
    /// <summary>
    /// Waits for <paramref name="task"/> for at most <paramref name="budget"/>. Returns
    /// <see langword="true"/> if it finished in time and <see langword="false"/> if the time ran
    /// out; in that case the task is left running, not cancelled. Cancelling
    /// <paramref name="cancellationToken"/> throws, because that is the caller giving up, not the time being up.
    /// </summary>
    public static async Task<bool> CompletesWithinAsync(this Task task, TimeSpan budget, CancellationToken cancellationToken)
    {
        if (task.IsCompleted)
        {
            return true;
        }

        using CancellationTokenSource delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task finished = await Task.WhenAny(task, Task.Delay(budget, delayCancellation.Token)).ConfigureAwait(false);

        // Stops the timer, so a task that finishes quickly does not leave a delay running.
        delayCancellation.Cancel();

        if (ReferenceEquals(finished, task))
        {
            return true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return false;
    }
}