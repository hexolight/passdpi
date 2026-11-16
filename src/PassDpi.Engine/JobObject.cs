using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PassDpi.Engine;

/// <summary>
/// A Windows job object with KILL_ON_JOB_CLOSE: if the service dies for any reason, Windows
/// kills goodbyedpi.exe too, which closes its WinDivert handle and restores normal networking.
/// </summary>
internal sealed unsafe partial class JobObject : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BASIC_LIMIT
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS { public ulong a, b, c, d, e, f; }

    [StructLayout(LayoutKind.Sequential)]
    private struct EXTENDED_LIMIT
    {
        public BASIC_LIMIT Basic;
        public IO_COUNTERS Io;
        public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true)]
    private static partial nint CreateJobObject(nint attr, nint name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(nint job, int cls, void* info, uint len);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(nint job, nint process);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint h);

    private nint _handle;

    public JobObject()
    {
        _handle = CreateJobObject(0, 0);
        if (_handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        var info = new EXTENDED_LIMIT();
        info.Basic.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if (!SetInformationJobObject(_handle, 9 /*ExtendedLimitInformation*/, &info, (uint)sizeof(EXTENDED_LIMIT)))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Assign(nint processHandle)
    {
        if (!AssignProcessToJobObject(_handle, processHandle))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Dispose()
    {
        if (_handle != 0) { CloseHandle(_handle); _handle = 0; }
    }
}
