using System.ComponentModel;
using System.Diagnostics;

namespace VisualStudioTranslator.Engine.LocalTranslation.Llama;

/// <summary>Everything needed to start the model server as a separate program.</summary>
internal sealed record ServerStartInfo(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment);

/// <summary>A started model server, as much of it as the Engine needs: is it alive, and make it stop.</summary>
internal interface IServerProcess : IDisposable
{
    bool HasExited { get; }

    /// <summary>Ends the program if it is still running. Safe to call more than once.</summary>
    void Kill();
}

/// <summary>
/// Starts the model server. Behind an interface so that the logic around it (start once, wait until
/// ready, notice a crash, stop) can be tested without a real server and a model of several gigabytes.
/// </summary>
internal interface IServerProcessLauncher
{
    IServerProcess Start(ServerStartInfo info);
}

/// <summary>
/// Starts the server as an operating-system process with no window, tied to the Engine through a
/// <see cref="ProcessJob"/>: whatever it starts ends when it is disposed or when the Engine ends.
/// </summary>
internal sealed class ProcessLauncher : IServerProcessLauncher, IDisposable
{
    private readonly Lock _sync = new();
    private ProcessJob? _job;
    private bool _disposed;

    public IServerProcess Start(ServerStartInfo info)
    {
        // Taken before the program starts, so that a job Windows refuses leaves nothing running.
        ProcessJob job = Job();

        ProcessStartInfo start = new(info.ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(info.ExecutablePath) ?? string.Empty,
        };

        foreach (string argument in info.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (KeyValuePair<string, string> variable in info.Environment)
        {
            start.Environment[variable.Key] = variable.Value;
        }

        Process process = new() { StartInfo = start };

        try
        {
            // What the server prints is read and dropped. Left unread, it would fill the pipe and stall
            // the server; kept, it would be a second log to guard against holding the user's text.
            process.OutputDataReceived += static (_, _) => { };
            process.ErrorDataReceived += static (_, _) => { };

            process.Start();
        }
        catch
        {
            process.Dispose();
            throw;
        }

        try
        {
            // Between the start and this line the program is not yet tied to the Engine; an Engine that
            // dies in that instant leaves it behind. Windows offers no way to close that gap for a
            // program started through Process, and it is a few microseconds wide.
            job.Add(process);
        }
        catch
        {
            // A server the Engine cannot take down with it must not run at all.
            EndQuietly(process, TimeSpan.Zero);
            process.Dispose();
            throw;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return new ServerProcess(process);
    }

    /// <summary>Ends every program this launcher started that is still running.</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _job?.Dispose();
            _job = null;
        }
    }

    private ProcessJob Job()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // Made on first use, so that a refusal from Windows surfaces as a failed start the caller
            // already handles, not as a failure to create the launcher.
            return _job ??= ProcessJob.Create();
        }
    }

    private static void EndQuietly(Process process, TimeSpan wait)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(wait);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // It ended by itself in the meantime, which is what was wanted.
        }
    }

    private sealed class ServerProcess(Process process) : IServerProcess
    {
        private static readonly TimeSpan KillWait = TimeSpan.FromSeconds(5);

        public bool HasExited
        {
            get
            {
                try
                {
                    return process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    // Asked after the process was let go of: whatever it was, it is not ours to use any more.
                    return true;
                }
            }
        }

        public void Kill() => EndQuietly(process, KillWait);

        public void Dispose() => process.Dispose();
    }
}