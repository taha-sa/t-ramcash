using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Principal;
using TSuite.Link;

namespace TRamCash;

public enum CleanMode { Smart, Full }

public readonly record struct CleanStep(string Name, double FreedMb, long Ms, bool Ok);

public sealed class CleanResult
{
    public double FreedMb { get; init; }
    public long DurationMs { get; init; }
    public CleanMode Mode { get; init; }
    public List<CleanStep> PerStep { get; init; } = new();
    public bool IsAdmin { get; init; }
    /// <summary>Adim hatasi / yetki eksigi durumunda Turkce aciklama; sorun yoksa null.</summary>
    public string Note { get; init; }
}

public static class MemoryCleaner
{
    #region Win32 / NT API Declarations

    [StructLayout(LayoutKind.Sequential)]
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
    }

    [StructLayout(LayoutKind.Sequential)]
    struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    struct LUID_AND_ATTRIBUTES { public LUID Luid; public uint Attributes; }

    [StructLayout(LayoutKind.Sequential)]
    struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID_AND_ATTRIBUTES Privileges; }

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORY_COMBINE_INFORMATION_EX
    {
        public IntPtr Handle;
        public UIntPtr PagesCombined;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public UIntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    const uint TOKEN_QUERY = 0x0008;
    const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    const uint PROCESS_SET_QUOTA = 0x0100;
    const uint TH32CS_SNAPPROCESS = 0x2;

    const int SystemMemoryListInformation = 80;
    const int SystemCombinePhysicalMemoryInformation = 130;
    const int SystemRegistryReconciliationInformation = 155;

    enum MemoryCommand
    {
        EmptyWorkingSets = 2,
        FlushModifiedList = 3,
        PurgeStandbyList = 4,
        PurgeLowPriorityStandbyList = 5,
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
    static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CloseHandle(IntPtr hObject);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetSystemFileCacheSize(UIntPtr min, UIntPtr max, uint flags);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

    [DllImport("ntdll.dll")]
    static extern int NtSetSystemInformation(int SystemInformationClass, ref int SystemInformation, int SystemInformationLength);

    [DllImport("ntdll.dll")]
    static extern int NtSetSystemInformation(int SystemInformationClass, ref MEMORY_COMBINE_INFORMATION_EX SystemInformation, int SystemInformationLength);

    [DllImport("ntdll.dll")]
    static extern int NtSetSystemInformation(int SystemInformationClass, IntPtr SystemInformation, int SystemInformationLength);

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

    /// <summary>Heap tahsisi yapmadan anlik bellek durumunu doldurur (tick basina sifir allocation).</summary>
    public static void ReadSnapshot(ref MemorySnapshot snap)
    {
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Unsafe.SizeOf<MEMORYSTATUSEX>() };
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

        var perf = new PERFORMANCE_INFORMATION { cb = (uint)Unsafe.SizeOf<PERFORMANCE_INFORMATION>() };
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
    }

    public static MemorySnapshot GetSnapshot()
    {
        var s = new MemorySnapshot();
        ReadSnapshot(ref s);
        return s;
    }

    static ulong AvailPhys()
    {
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Unsafe.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref mem) ? mem.ullAvailPhys : 0;
    }

    /// <summary>
    /// Akilli: dusuk oncelikli standby + (haric tutulmayan) surec working set'leri + combine page lists.
    /// Tam: yukaridakilerin hepsi + sistem WS + modified list + tam standby + dosya onbellegi + registry reconciliation.
    /// Tam modda sistem duzeyi adimlar ayarlardaki isaretlere de saygi duyar.
    /// </summary>
    static readonly object _cleanLock = new();

    public static CleanResult Clean(AppSettings settings, CleanMode mode = CleanMode.Full)
    {
        lock (_cleanLock)
        {
            // Tek birlesik temizlik: yetki varsa tum etkin sistem adimlari + SeDebugPrivilege (yalniz temizlik suresince).
            bool dbg = IsAdministrator && SetPrivilege("SeDebugPrivilege", true);
            try { return CleanCore(settings); }
            finally { if (dbg) SetPrivilege("SeDebugPrivilege", false); }
        }
    }

    static CleanResult CleanCore(AppSettings settings)
    {
        var sw = Stopwatch.StartNew();
        ulong before = AvailPhys();
        ulong lastAvail = before;
        var steps = new List<CleanStep>(7);

        bool admin = IsAdministrator;
        bool privOk = admin && EnablePrivilege("SeProfileSingleProcessPrivilege");
        bool quotaOk = admin && EnablePrivilege("SeIncreaseQuotaPrivilege");

        void Step(string name, Func<bool> action)
        {
            ulong a = lastAvail;
            long t0 = sw.ElapsedMilliseconds;
            bool ok;
            try { ok = action(); } catch { ok = false; }
            ulong b = AvailPhys();
            lastAvail = b;
            steps.Add(new CleanStep(name, Math.Max(0, ((long)b - (long)a) / 1048576.0), sw.ElapsedMilliseconds - t0, ok));
        }

        if (settings.CleanProcessWorkingSets)
            Step(Strings.ProcessSets, () => { EmptyProcessWorkingSets(settings); return true; });

        if (privOk)
        {
            // Kullanıcının özel dışlama listesi yoksa çekirdek düzeyinde tüm çalışma kümelerini (sistem + korumalı süreçler)
            // Mem Reduct ile aynı NtSetSystemInformation(MemoryEmptyWorkingSets) çağrısıyla temizle.
            bool hasCustomExclusions = settings.ExcludedProcesses != null && settings.ExcludedProcesses.Count > 0;
            if (settings.CleanSystemWorkingSet && !hasCustomExclusions)
                Step(Strings.ProcessSets, () => NtMemCmd(MemoryCommand.EmptyWorkingSets));

            if (settings.CleanModifiedPageList)
                Step(Strings.ModifiedPages, () => NtMemCmd(MemoryCommand.FlushModifiedList));

            if (settings.CleanStandbyList)
                Step(Strings.Standby, () => NtMemCmd(MemoryCommand.PurgeStandbyList));

            if (settings.CleanStandbyLowPriority)
                Step(Strings.LowPriority, () => NtMemCmd(MemoryCommand.PurgeLowPriorityStandbyList));

            if (quotaOk)
                Step(Strings.FileCache, () => SetSystemFileCacheSize(UIntPtr.MaxValue, UIntPtr.MaxValue, 0));

            if (Environment.OSVersion.Version.Build >= 9600) // Win 8.1+
                Step(Strings.CombinePages, CombinePageLists);

            if (Environment.OSVersion.Version.Build >= 17134) // Win10 1803+
                Step(Strings.RegistryCache, () => NtSetSystemInformation(SystemRegistryReconciliationInformation, IntPtr.Zero, 0) >= 0);
        }

        ulong after = lastAvail;
        string note = null;
        List<string> failedNames = null;
        foreach (var st in steps)
        {
            if (!st.Ok) (failedNames ??= new List<string>(4)).Add(st.Name);
        }
        if (failedNames != null)
            note = Strings.Format(Strings.FailedSteps, failedNames.Count, string.Join(", ", failedNames));

        return new CleanResult
        {
            FreedMb = Math.Max(0, ((long)after - (long)before) / 1048576.0),
            DurationMs = sw.ElapsedMilliseconds,
            Mode = CleanMode.Full,
            PerStep = steps,
            IsAdmin = admin,
            Note = note
        };
    }

    static bool NtMemCmd(MemoryCommand cmd)
    {
        int c = (int)cmd;
        return NtSetSystemInformation(SystemMemoryListInformation, ref c, sizeof(int)) >= 0;
    }

    static bool CombinePageLists()
    {
        var info = new MEMORY_COMBINE_INFORMATION_EX();
        return NtSetSystemInformation(SystemCombinePhysicalMemoryInformation, ref info, Unsafe.SizeOf<MEMORY_COMBINE_INFORMATION_EX>()) >= 0;
    }

    /// <summary>Hic hariç tutulmayan sürecler: kendimiz, ön plan, T-Suite uygulamaları, ayardaki hariç listesi, PID &lt;= 4.</summary>
    static readonly object _exLock = new();
    static HashSet<string> _exCache;
    static HashSet<uint> _exPids;
    static long _exTick;

    /// <summary>Ayar kaydedilince cagrilir; bir sonraki temizlikte liste yeniden olusur.</summary>
    public static void InvalidateExcludeCache()
    {
        lock (_exLock) { _exCache = null; _exPids = null; }
    }

    static HashSet<string> BuildExcludedNames(AppSettings settings, out HashSet<uint> excludedPids)
    {
        HashSet<string> cachedNames;
        HashSet<uint> cachedPids;
        lock (_exLock)
        {
            // Yavas kisimlar (manifestler, kullanici listesi) 30 sn onbellekte; on plan PID her temizlikte taze okunur.
            if (_exCache == null || _exPids == null || Environment.TickCount64 - _exTick >= 30000)
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var pids = new HashSet<uint> { (uint)Environment.ProcessId };

                if (settings.ExcludedProcesses != null)
                    foreach (var n in settings.ExcludedProcesses) names.Add(NormalizeName(n));

                try
                {
                    foreach (var m in new AppRegistry().ReadAll())
                    {
                        if (!string.IsNullOrEmpty(m.ExePath)) names.Add(Path.GetFileNameWithoutExtension(m.ExePath));
                        if (m.Pid > 0) pids.Add((uint)m.Pid);
                    }
                }
                catch { }
                _exCache = names;
                _exPids = pids;
                _exTick = Environment.TickCount64;
            }
            cachedNames = _exCache;
            cachedPids = _exPids;
        }

        // Onbellegi kirletmemek icin kopya; on plan PID her seferinde taze.
        excludedPids = new HashSet<uint>(cachedPids);
        try
        {
            IntPtr fg = GetForegroundWindow();
            if (fg != IntPtr.Zero && GetWindowThreadProcessId(fg, out uint fgPid) != 0 && fgPid != 0)
                excludedPids.Add(fgPid);
        }
        catch { }
        return cachedNames;
    }

    public static string NormalizeName(string n)
    {
        n = (n ?? "").Trim();
        return n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n;
    }

    /// <summary>Toolhelp ile süreçleri sayar (Process nesnesi üretmez), her tutamaç finally'de kapatılır.</summary>
    /// <summary>Tam modda, kullanici dislamalarina ek olarak, working set'i hic bosaltilmayacak yerlesik sistem surecleri.</summary>
    static readonly HashSet<string> BuiltinSkipNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "dwm", "audiodg", "csrss", "winlogon", "explorer", "ctfmon", "sihost", "taskhostw"
    };

    static void EmptyProcessWorkingSets(AppSettings settings)
    {
        var excludedNames = BuildExcludedNames(settings, out var excludedPids);
        IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return;
        try
        {
            var pe = new PROCESSENTRY32 { dwSize = (uint)Unsafe.SizeOf<PROCESSENTRY32>() };
            if (!Process32FirstW(snap, ref pe)) return;
            var all = new List<(uint pid, string name)>(300);
            var children = new Dictionary<uint, List<uint>>(128);
            do
            {
                uint pid = pe.th32ProcessID;
                uint ppid = pe.th32ParentProcessID;
                all.Add((pid, pe.szExeFile));
                if (pid > 4 && ppid != 0 && ppid != pid)
                {
                    if (!children.TryGetValue(ppid, out var list))
                        children[ppid] = list = new List<uint>(4);
                    list.Add(pid);
                }
            }
            while (Process32NextW(snap, ref pe));

            // Ön plan ve hariç tutulan süreçlerin alt süreçlerini (torunlar dahil) O(N) BFS ile koru.
            var pending = new Queue<uint>(excludedPids);
            while (pending.Count > 0)
            {
                if (!children.TryGetValue(pending.Dequeue(), out var kids)) continue;
                foreach (uint kid in kids)
                    if (excludedPids.Add(kid)) pending.Enqueue(kid);
            }

            foreach (var (pid, name) in all)
            {
                if (pid <= 4 || excludedPids.Contains(pid)) continue;
                string norm = NormalizeName(name);
                if (excludedNames.Contains(norm) || BuiltinSkipNames.Contains(norm)) continue;

                IntPtr h = OpenProcess(PROCESS_SET_QUOTA | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (h == IntPtr.Zero) continue; // korumalı / sistem süreci: sessizce geç
                try { EmptyWorkingSet(h); }
                finally { CloseHandle(h); }
            }
        }
        finally { CloseHandle(snap); }
    }

    static bool EnablePrivilege(string privilegeName) => SetPrivilege(privilegeName, true);

    static bool SetPrivilege(string privilegeName, bool enable)
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
                Privileges = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = enable ? SE_PRIVILEGE_ENABLED : 0u }
            };
            if (!AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero)) return false;
            return Marshal.GetLastWin32Error() != 1300; // ERROR_NOT_ALL_ASSIGNED: yetki verilmedi
        }
        finally { CloseHandle(hToken); }
    }

    static bool CheckIsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }
}
