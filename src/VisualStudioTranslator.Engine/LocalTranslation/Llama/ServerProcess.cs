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

/// <summary>Starts the server as an operating-system process with no window.</summary>
internal sealed class ProcessLauncher : IServerProcessLauncher
{
    public IServerProcess Start(ServerStartInfo info)
    {
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
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch
        {
            process.Dispose();
            throw;
        }

        return new ServerProcess(process);
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

        public void Kill()
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(KillWait);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                // It ended by itself in the meantime, which is what was wanted.
            }
        }

        public void Dispose() => process.Dispose();
    }
}