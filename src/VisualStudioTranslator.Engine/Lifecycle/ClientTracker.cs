namespace VisualStudioTranslator.Engine.Lifecycle;

/// <summary>
/// Counts the clients connected right now and remembers when the last one went. It exists so the
/// Engine can tell "nobody needs me any more" from "nobody has asked for anything lately":
/// a client holding a connection open is a user who still has Visual Studio running, however
/// quiet, and a long model load or translation must never look like idleness.
/// </summary>
internal sealed class ClientTracker(TimeProvider time)
{
    private readonly Lock _gate = new();
    private int _connected;

    // Starting counts as activity: a freshly launched Engine gets a full idle period to be connected to.
    private DateTimeOffset _lastActivity = time.GetUtcNow();

    public int Connected
    {
        get
        {
            lock (_gate)
            {
                return _connected;
            }
        }
    }

    /// <summary>How long no client has been connected; zero while there is at least one.</summary>
    public TimeSpan IdleTime
    {
        get
        {
            lock (_gate)
            {
                return _connected > 0 ? TimeSpan.Zero : time.GetUtcNow() - _lastActivity;
            }
        }
    }

    public void ClientConnected()
    {
        lock (_gate)
        {
            _connected++;
            _lastActivity = time.GetUtcNow();
        }
    }

    public void ClientDisconnected()
    {
        lock (_gate)
        {
            if (_connected > 0)
            {
                _connected--;
            }

            _lastActivity = time.GetUtcNow();
        }
    }
}