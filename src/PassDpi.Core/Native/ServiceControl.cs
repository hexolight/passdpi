using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PassDpi.Core.Native;

/// <summary>Thin wrappers over the Service Control Manager API (advapi32).</summary>
public static unsafe partial class ServiceControl
{
    private const uint SC_MANAGER_CONNECT = 0x0001;
    private const uint SC_MANAGER_CREATE_SERVICE = 0x0002;
    private const uint SERVICE_QUERY_CONFIG = 0x0001;
    private const uint SERVICE_CHANGE_CONFIG = 0x0002;
    private const uint SERVICE_QUERY_STATUS = 0x0004;
    private const uint SERVICE_START = 0x0010;
    private const uint SERVICE_STOP = 0x0020;
    private const uint DELETE = 0x00010000;
    private const uint READ_CONTROL = 0x00020000;
    private const uint WRITE_DAC = 0x00040000;
    private const uint SERVICE_WIN32_OWN_PROCESS = 0x10;
    private const uint SERVICE_AUTO_START = 0x2;
    private const uint SERVICE_DEMAND_START = 0x3;
    private const uint SERVICE_ERROR_NORMAL = 0x1;
    private const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
    private const uint SERVICE_CONFIG_DESCRIPTION = 1;
    private const uint SERVICE_CONFIG_FAILURE_ACTIONS = 2;
    private const uint SC_STATUS_PROCESS_INFO = 0;
    private const uint DACL_SECURITY_INFORMATION = 0x4;
    private const int ERROR_SERVICE_DOES_NOT_EXIST = 1060;
    private const int ERROR_SERVICE_NOT_ACTIVE = 1062;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public uint dwServiceType, dwCurrentState, dwControlsAccepted, dwWin32ExitCode,
            dwServiceSpecificExitCode, dwCheckPoint, dwWaitHint;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS_PROCESS
    {
        public uint dwServiceType, dwCurrentState, dwControlsAccepted, dwWin32ExitCode,
            dwServiceSpecificExitCode, dwCheckPoint, dwWaitHint, dwProcessId, dwServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QUERY_SERVICE_CONFIGW
    {
        public uint dwServiceType, dwStartType, dwErrorControl;
        public char* lpBinaryPathName, lpLoadOrderGroup;
        public uint dwTagId;
        public char* lpDependencies, lpServiceStartName, lpDisplayName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SC_ACTION { public uint Type; public uint Delay; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_FAILURE_ACTIONSW
    {
        public uint dwResetPeriod;
        public char* lpRebootMsg;
        public char* lpCommand;
        public uint cActions;
        public SC_ACTION* lpsaActions;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_DESCRIPTIONW { public char* lpDescription; }

    [LibraryImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint OpenSCManager(string? machine, string? db, uint access);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint OpenService(nint scm, string name, uint access);

    [LibraryImport("advapi32.dll", EntryPoint = "CreateServiceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateService(nint scm, string name, string display, uint access, uint type,
        uint start, uint error, string path, string? group, nint tag, string? deps, string? user, string? pwd);

    [LibraryImport("advapi32.dll", EntryPoint = "ChangeServiceConfigW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ChangeServiceConfig(nint svc, uint type, uint start, uint error, string? path,
        string? group, nint tag, string? deps, string? user, string? pwd, string? display);

    [LibraryImport("advapi32.dll", EntryPoint = "ChangeServiceConfig2W", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ChangeServiceConfig2(nint svc, uint level, void* info);

    [LibraryImport("advapi32.dll", EntryPoint = "QueryServiceConfigW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryServiceConfig(nint svc, void* buf, uint size, out uint needed);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryServiceStatusEx(nint svc, uint level, void* buf, uint size, out uint needed);

    [LibraryImport("advapi32.dll", EntryPoint = "StartServiceW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool StartService(nint svc, uint argc, nint argv);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ControlService(nint svc, uint control, out SERVICE_STATUS status);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteService(nint svc);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseServiceHandle(nint h);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetServiceObjectSecurity(nint svc, uint info, void* sd);

    [LibraryImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertSddl(string sddl, uint rev, out nint sd, out uint size);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint p);

    // ---------------------------------------------------------------- helpers

    private sealed class Handle : IDisposable
    {
        public nint Value;
        public Handle(nint v) => Value = v;
        public bool IsValid => Value != 0;
        public void Dispose() { if (Value != 0) { CloseServiceHandle(Value); Value = 0; } }
    }

    private static Handle OpenScm(uint access)
    {
        var h = new Handle(OpenSCManager(null, null, access));
        if (!h.IsValid) throw new Win32Exception(Marshal.GetLastWin32Error());
        return h;
    }

    private static Handle? TryOpen(string name, uint access)
    {
        using var scm = OpenScm(SC_MANAGER_CONNECT);
        var h = new Handle(OpenService(scm.Value, name, access));
        if (h.IsValid) return h;
        var err = Marshal.GetLastWin32Error();
        if (err == ERROR_SERVICE_DOES_NOT_EXIST) return null;
        throw new Win32Exception(err);
    }

    public static bool Exists(string name)
    {
        try { using var h = TryOpen(name, SERVICE_QUERY_STATUS); return h != null; }
        catch (Win32Exception) { return true; } // exists but we can't open it
    }

    /// <summary>Returns the PID of a running service, or 0.</summary>
    public static uint GetProcessId(string name)
    {
        using var h = TryOpen(name, SERVICE_QUERY_STATUS);
        if (h == null) return 0;
        SERVICE_STATUS_PROCESS s;
        if (!QueryServiceStatusEx(h.Value, SC_STATUS_PROCESS_INFO, &s, (uint)sizeof(SERVICE_STATUS_PROCESS), out _))
            return 0;
        return s.dwCurrentState == 4 /*RUNNING*/ ? s.dwProcessId : 0;
    }

    /// <summary>1=stopped 2=start pending 3=stop pending 4=running; 0 = not installed.</summary>
    public static uint GetState(string name)
    {
        using var h = TryOpen(name, SERVICE_QUERY_STATUS);
        if (h == null) return 0;
        SERVICE_STATUS_PROCESS s;
        return QueryServiceStatusEx(h.Value, SC_STATUS_PROCESS_INFO, &s, (uint)sizeof(SERVICE_STATUS_PROCESS), out _)
            ? s.dwCurrentState : 0;
    }

    public static string? GetBinaryPath(string name)
    {
        using var h = TryOpen(name, SERVICE_QUERY_CONFIG);
        if (h == null) return null;
        QueryServiceConfig(h.Value, null, 0, out var need);
        var buf = Marshal.AllocHGlobal((int)need);
        try
        {
            if (!QueryServiceConfig(h.Value, (void*)buf, need, out _)) return null;
            var cfg = (QUERY_SERVICE_CONFIGW*)buf;
            return new string(cfg->lpBinaryPathName);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public static bool IsAutoStart(string name)
    {
        using var h = TryOpen(name, SERVICE_QUERY_CONFIG);
        if (h == null) return false;
        QueryServiceConfig(h.Value, null, 0, out var need);
        var buf = Marshal.AllocHGlobal((int)need);
        try
        {
            if (!QueryServiceConfig(h.Value, (void*)buf, need, out _)) return false;
            return ((QUERY_SERVICE_CONFIGW*)buf)->dwStartType == SERVICE_AUTO_START;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public static void SetAutoStart(string name, bool auto)
    {
        using var h = TryOpen(name, SERVICE_CHANGE_CONFIG) ?? throw new InvalidOperationException("Service not installed");
        if (!ChangeServiceConfig(h.Value, SERVICE_NO_CHANGE, auto ? SERVICE_AUTO_START : SERVICE_DEMAND_START,
                SERVICE_NO_CHANGE, null, null, 0, null, null, null, null))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public static void Start(string name)
    {
        using var h = TryOpen(name, SERVICE_START) ?? throw new InvalidOperationException("Service not installed");
        if (!StartService(h.Value, 0, 0))
        {
            var err = Marshal.GetLastWin32Error();
            if (err != 1056 /*ALREADY_RUNNING*/) throw new Win32Exception(err);
        }
    }

    public static void Stop(string name, TimeSpan wait)
    {
        using var h = TryOpen(name, SERVICE_STOP | SERVICE_QUERY_STATUS);
        if (h == null) return;
        if (!ControlService(h.Value, 1 /*STOP*/, out _))
        {
            var err = Marshal.GetLastWin32Error();
            if (err != ERROR_SERVICE_NOT_ACTIVE) throw new Win32Exception(err);
        }
        var deadline = DateTime.UtcNow + wait;
        while (DateTime.UtcNow < deadline)
        {
            if (GetState(name) is 0 or 1) return;
            Thread.Sleep(200);
        }
    }

    public static void Delete(string name)
    {
        using var h = TryOpen(name, DELETE);
        if (h == null) return;
        if (!DeleteService(h.Value))
        {
            var err = Marshal.GetLastWin32Error();
            if (err != 1072 /*MARKED_FOR_DELETE*/) throw new Win32Exception(err);
        }
    }

    /// <summary>
    /// Creates (or reconfigures) the PassDPI service as LocalSystem, sets its description,
    /// restart-on-failure actions and a DACL that additionally lets <paramref name="ownerSid"/>
    /// start it and query its status (but not stop, reconfigure or delete it).
    /// </summary>
    public static void InstallOrUpdate(string name, string display, string description,
        string exePath, bool autoStart, string ownerSid)
    {
        using var scm = OpenScm(SC_MANAGER_CONNECT | SC_MANAGER_CREATE_SERVICE);
        var quoted = "\"" + exePath + "\"";
        var start = autoStart ? SERVICE_AUTO_START : SERVICE_DEMAND_START;
        const uint access = SERVICE_CHANGE_CONFIG | SERVICE_QUERY_STATUS | SERVICE_START | WRITE_DAC | READ_CONTROL;

        var h = new Handle(OpenService(scm.Value, name, access));
        if (h.IsValid)
        {
            if (!ChangeServiceConfig(h.Value, SERVICE_WIN32_OWN_PROCESS, start, SERVICE_ERROR_NORMAL,
                    quoted, null, 0, null, "LocalSystem", null, display))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        else
        {
            h = new Handle(CreateService(scm.Value, name, display, access, SERVICE_WIN32_OWN_PROCESS, start,
                SERVICE_ERROR_NORMAL, quoted, null, 0, null, null /*LocalSystem*/, null));
            if (!h.IsValid) throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        using (h)
        {
            fixed (char* d = description)
            {
                var desc = new SERVICE_DESCRIPTIONW { lpDescription = d };
                ChangeServiceConfig2(h.Value, SERVICE_CONFIG_DESCRIPTION, &desc);
            }

            // Restart after 5s, 10s, then 30s; reset failure count after a day.
            var actions = stackalloc SC_ACTION[3];
            actions[0] = new SC_ACTION { Type = 1, Delay = 5000 };
            actions[1] = new SC_ACTION { Type = 1, Delay = 10000 };
            actions[2] = new SC_ACTION { Type = 1, Delay = 30000 };
            var fa = new SERVICE_FAILURE_ACTIONSW { dwResetPeriod = 86400, cActions = 3, lpsaActions = actions };
            ChangeServiceConfig2(h.Value, SERVICE_CONFIG_FAILURE_ACTIONS, &fa);

            SetDacl(h.Value, BuildServiceSddl(ownerSid));
        }
    }

    /// <summary>
    /// Windows' default service DACL plus one ACE for the installing user:
    /// RP = start, LC = query status, LO = interrogate, CC = query config, RC = read control.
    /// </summary>
    internal static string BuildServiceSddl(string ownerSid)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(ownerSid, @"^S-1-[0-9-]{1,180}$"))
            throw new ArgumentException("Invalid SID", nameof(ownerSid));
        return "D:" +
               "(A;;CCLCSWRPWPDTLOCRRC;;;SY)" +
               "(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)" +
               "(A;;CCLCSWLOCRRC;;;IU)" +
               "(A;;CCLCSWLOCRRC;;;SU)" +
               $"(A;;CCLCRPLORC;;;{ownerSid})";
    }

    private static void SetDacl(nint svc, string sddl)
    {
        if (!ConvertSddl(sddl, 1, out var sd, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (!SetServiceObjectSecurity(svc, DACL_SECURITY_INFORMATION, (void*)sd))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { LocalFree(sd); }
    }
}
