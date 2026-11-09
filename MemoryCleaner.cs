using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace TRamCash;

public class MemoryCleaner
{
    #region Win32 / NT API Declarations

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public static MEMORYSTATUSEX Create()
        {
            var res = new MEMORYSTATUSEX();
            res.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            return res;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PERFORMANCE_INFORMATION
    {
        public uint cb;
        public UIntPtr CommitTotal;
        public UIntPtr CommitLimit;
        public UIntPtr CommitPeak;
        public UIntPtr PhysicalTotal;
        public UIntPtr PhysicalAvailable;
        public UIntPtr SystemCache;
        public UIntPtr KernelTotal;
        public UIntPtr KernelPaged;
        public UIntPtr KernelNonpaged;
        public UIntPtr PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;

        public static PERFORMANCE_INFORMATION Create()
        {
            var res = new PERFORMANCE_INFORMATION();
            res.cb = (uint)Marshal.SizeOf(typeof(PERFORMANCE_INFORMATION));
            return res;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID_AND_ATTRIBUTES Privileges;
    }

    const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    const uint TOKEN_QUERY = 0x0008;
    const uint SE_PRIVILEGE_ENABLED = 0x00000002;

    const uint PROCESS_QUERY_INFORMATION = 0x0400;
    const uint PROCESS_SET_QUOTA = 0x0100;

    const int SystemMemoryListInformation = 80; // 0x50

    public enum SYSTEM_MEMORY_LIST_COMMAND
    {
        MemoryCaptureAccessedBits = 0,
        MemoryCaptureAndResetAccessedBits = 1,
        MemoryEmptyWorkingSets = 2,
        MemoryFlushModifiedList = 3,
        MemoryPurgeStandbyList = 4,
        MemoryPurgeLowPriorityStandbyList = 5,
        MemoryCommandMax = 6
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetPerformanceInfo(ref PERFORMANCE_INFORMATION pPerformanceInformation, uint cb);

    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CloseHandle(IntPtr hObject);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

    [DllImport("ntdll.dll")]
    static extern int NtSetSystemInformation(int SystemInformationClass, ref int SystemInformation, int SystemInformationLength);

    #endregion

    public struct MemorySnapshot
    {
        public ulong TotalPhysicalBytes;
        public ulong AvailablePhysicalBytes;
        public ulong UsedPhysicalBytes;
        public uint MemoryLoadPercent;

        public ulong TotalPageFileBytes;
        public ulong AvailablePageFileBytes;
        public ulong UsedPageFileBytes;
        public uint PageFileLoadPercent;

        public ulong SystemCacheBytes;
        public ulong CommitTotalBytes;
        public ulong CommitLimitBytes;
        public uint CommitLoadPercent;

        public uint ProcessCount;
        public uint ThreadCount;
        public uint HandleCount;
    }

    public static bool IsAdministrator { get; } = CheckIsAdministrator();

    public static MemorySnapshot GetSnapshot()
    {
        var snap = new MemorySnapshot();

        var mem = MEMORYSTATUSEX.Create();
        if (GlobalMemoryStatusEx(ref mem))
        {
            snap.TotalPhysicalBytes = mem.ullTotalPhys;
            snap.AvailablePhysicalBytes = mem.ullAvailPhys;
            snap.UsedPhysicalBytes = mem.ullTotalPhys > mem.ullAvailPhys ? mem.ullTotalPhys - mem.ullAvailPhys : 0;
            snap.MemoryLoadPercent = mem.dwMemoryLoad;

            snap.TotalPageFileBytes = mem.ullTotalPageFile;
            snap.AvailablePageFileBytes = mem.ullAvailPageFile;
            snap.UsedPageFileBytes = mem.ullTotalPageFile > mem.ullAvailPageFile ? mem.ullTotalPageFile - mem.ullAvailPageFile : 0;
            snap.PageFileLoadPercent = snap.TotalPageFileBytes > 0 
                ? (uint)((snap.UsedPageFileBytes * 100) / snap.TotalPageFileBytes) 
                : 0;
        }

        var perf = PERFORMANCE_INFORMATION.Create();
        if (GetPerformanceInfo(ref perf, perf.cb))
        {
            ulong pageSize = perf.PageSize.ToUInt64();
            snap.SystemCacheBytes = perf.SystemCache.ToUInt64() * pageSize;
            snap.CommitTotalBytes = perf.CommitTotal.ToUInt64() * pageSize;
            snap.CommitLimitBytes = perf.CommitLimit.ToUInt64() * pageSize;
            snap.CommitLoadPercent = snap.CommitLimitBytes > 0
                ? (uint)((snap.CommitTotalBytes * 100) / snap.CommitLimitBytes)
                : 0;

            snap.ProcessCount = perf.ProcessCount;
            snap.ThreadCount = perf.ThreadCount;
            snap.HandleCount = perf.HandleCount;
        }

        return snap;
    }

    public static double Clean(AppSettings settings)
    {
        var before = GetSnapshot();

        // Ayrıcalıkları yükselt
        EnablePrivilege("SeProfileSingleProcessPrivilege");
        EnablePrivilege("SeIncreaseQuotaPrivilege");
        EnablePrivilege("SeDebugPrivilege");

        // 1. Süreç çalışma kümeleri
        if (settings.CleanProcessWorkingSets)
        {
            CleanAllProcessWorkingSets();
        }

        // 2. Sistem çalışma kümesi
        if (settings.CleanSystemWorkingSet)
        {
            int cmd = (int)SYSTEM_MEMORY_LIST_COMMAND.MemoryEmptyWorkingSets;
            NtSetSystemInformation(SystemMemoryListInformation, ref cmd, sizeof(int));
        }

        // 3. Değiştirilmiş sayfalar (Modified list)
        if (settings.CleanModifiedPageList)
        {
            int cmd = (int)SYSTEM_MEMORY_LIST_COMMAND.MemoryFlushModifiedList;
            NtSetSystemInformation(SystemMemoryListInformation, ref cmd, sizeof(int));
        }

        // 4. Bekleme listesi (Standby list)
        if (settings.CleanStandbyList)
        {
            int cmd = (int)SYSTEM_MEMORY_LIST_COMMAND.MemoryPurgeStandbyList;
            NtSetSystemInformation(SystemMemoryListInformation, ref cmd, sizeof(int));
        }

        // 5. Düşük öncelikli bekleme listesi
        if (settings.CleanStandbyLowPriority)
        {
            int cmd = (int)SYSTEM_MEMORY_LIST_COMMAND.MemoryPurgeLowPriorityStandbyList;
            NtSetSystemInformation(SystemMemoryListInformation, ref cmd, sizeof(int));
        }

        var after = GetSnapshot();
        long diff = (long)after.AvailablePhysicalBytes - (long)before.AvailablePhysicalBytes;
        return Math.Max(0, diff / (1024.0 * 1024.0)); // MB cinsinden
    }

    static void CleanAllProcessWorkingSets()
    {
        var procs = Process.GetProcesses();
        int currentPid = Environment.ProcessId;

        foreach (var p in procs)
        {
            try
            {
                // Sistem, boşta (Idle) ve kendi sürecimizi atla
                if (p.Id <= 4 || p.Id == currentPid)
                    continue;

                IntPtr hProcess = OpenProcess(PROCESS_SET_QUOTA | PROCESS_QUERY_INFORMATION, false, p.Id);
                if (hProcess != IntPtr.Zero)
                {
                    EmptyWorkingSet(hProcess);
                    CloseHandle(hProcess);
                }
            }
            catch
            {
            }
            finally
            {
                p.Dispose();
            }
        }
    }

    static bool EnablePrivilege(string privilegeName)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr hToken))
            return false;

        try
        {
            if (!LookupPrivilegeValue(null, privilegeName, out LUID luid))
                return false;

            var tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Privileges = new LUID_AND_ATTRIBUTES
                {
                    Luid = luid,
                    Attributes = SE_PRIVILEGE_ENABLED
                }
            };

            return AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            CloseHandle(hToken);
        }
    }

    static bool CheckIsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
