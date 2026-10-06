namespace VisualStudioTranslator.Engine.Lifecycle;

/// <summary>
/// Makes sure only one Engine runs per user. The first to start takes a named semaphore and
/// keeps it; any later one finds it taken and has nothing to do. A semaphore rather than a mutex,
/// because a mutex belongs to the thread that took it, and the Engine's main method carries on
/// from a different thread after its first await, so releasing it there would throw. A semaphore
/// has no such owner. If the Engine dies, the system destroys the semaphore with its last handle,
/// so a crashed Engine never leaves the next one locked out.
/// </summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    private readonly Semaphore _semaphore;
    private bool _released;

    private SingleInstanceGuard(Semaphore semaphore) => _semaphore = semaphore;

    /// <param name="name">A system-wide object name; the "Local\" prefix scopes it to the current logon session.</param>
    /// <returns>The guard, or <see langword="null"/> when another Engine already holds it.</returns>
    public static SingleInstanceGuard? TryAcquire(string name)
    {
        Semaphore semaphore = new(initialCount: 1, maximumCount: 1, name, out _);

        if (!semaphore.WaitOne(TimeSpan.Zero))
        {
            semaphore.Dispose();
            return null;
        }

        return new SingleInstanceGuard(semaphore);
    }

    public void Dispose()
    {
        if (!_released)
        {
            _released = true;
            _semaphore.Release();
        }

        _semaphore.Dispose();
    }
}