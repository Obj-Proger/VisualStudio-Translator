using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace VisualStudioTranslator.Engine.LocalTranslation.Llama;

/// <summary>
/// Ties started programs to the life of the Engine through a Windows job object. The job is set up so
/// that Windows ends every program in it when the last handle to the job is closed, and the Engine holds
/// the only handle. So the programs end when this object is disposed, and also when the Engine ends in a
/// way no cleanup code of its own survives: a crash, or being ended from Task Manager. A model server
/// left behind would keep several gigabytes of memory for nothing.
/// </summary>
internal sealed partial class ProcessJob : IDisposable
{
    private const int ExtendedLimitInformationClass = 9;
    private const uint KillOnJobClose = 0x2000;

    private readonly JobHandle _handle;

    private ProcessJob(JobHandle handle) => _handle = handle;

    /// <exception cref="Win32Exception">Windows refused to create or set up the job.</exception>
    public static ProcessJob Create()
    {
        JobHandle handle = NativeMethods.CreateJobObject(0, 0);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new Win32Exception(error);
        }

        try
        {
            ExtendedLimitInformation limits = default;
            limits.BasicLimitInformation.LimitFlags = KillOnJobClose;

            if (!NativeMethods.SetInformationJobObject(
                handle, ExtendedLimitInformationClass, ref limits, (uint)Marshal.SizeOf<ExtendedLimitInformation>()))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
        catch
        {
            handle.Dispose();
            throw;
        }

        return new ProcessJob(handle);
    }

    /// <summary>
    /// Puts a started program in the job. Programs it starts later join the job by themselves.
    /// </summary>
    /// <exception cref="Win32Exception">Windows refused to put the program in the job.</exception>
    /// <exception cref="ObjectDisposedException">The job has already been closed.</exception>
    public void Add(Process process)
    {
        if (!NativeMethods.AssignProcessToJobObject(_handle, process.SafeHandle))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    /// <summary>Closes the job, which ends every program still in it.</summary>
    public void Dispose() => _handle.Dispose();

    private sealed class JobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        // Public because the generated interop code creates it.
        public JobHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
    }

#pragma warning disable CS0649 // Only the flags are set; every other field stays zero, which means "no limit".

    // The layouts below mirror JOBOBJECT_EXTENDED_LIMIT_INFORMATION and the structures it holds. Sequential
    // layout with pointer-sized fields gives the right size and offsets on 32-bit and 64-bit Windows alike.
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

#pragma warning restore CS0649

    private static partial class NativeMethods
    {
        [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true)]
        public static partial JobHandle CreateJobObject(nint jobAttributes, nint name);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetInformationJobObject(
            JobHandle job, int informationClass, ref ExtendedLimitInformation information, uint length);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool AssignProcessToJobObject(JobHandle job, SafeProcessHandle process);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CloseHandle(nint handle);
    }
}