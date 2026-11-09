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
    public static CleanResult Clean(AppSettings settings, CleanMode mode)
    {
        var sw = Stopwatch.StartNew();
        ulong before = AvailPhys();
        var steps = new List<CleanStep>(8);
        bool full = mode == CleanMode.Full;

        bool privOk = EnablePrivilege("SeProfileSingleProcessPrivilege");
        EnablePrivilege("SeIncreaseQuotaPrivilege");
        EnablePrivilege("SeDebugPrivilege");
        bool admin = IsAdministrator;

        void Step(string name, Func<bool> action)
        {
            ulong a = AvailPhys();
            long t0 = sw.ElapsedMilliseconds;
            bool ok;
            try { ok = action(); } catch { ok = false; }
            ulong b = AvailPhys();
            steps.Add(new CleanStep(name, ((long)b - (long)a) / 1048576.0, sw.ElapsedMilliseconds - t0, ok));
        }

        if (!full || settings.CleanProcessWorkingSets)
            Step("Süreç çalışma kümeleri", () => { EmptyProcessWorkingSets(settings); return true; });

        // NOT: NtSetSystemInformation(MemoryEmptyWorkingSets) bilerek KULLANILMIYOR: tüm süreçleri
        // dışlamaları yok sayarak boşaltır. Tam mod da dışlamalara saygı duyan süreç başına döngüyü kullanır.

        if (full && settings.CleanModifiedPageList)
            Step("Değiştirilmiş sayfalar", () => NtMemCmd(MemoryCommand.FlushModifiedList));

        if (full && settings.CleanStandbyList)
            Step("Bekleme listesi", () => NtMemCmd(MemoryCommand.PurgeStandbyList));

        if (!full || settings.CleanStandbyLowPriority)
            Step("Düşük öncelikli bekleme", () => NtMemCmd(MemoryCommand.PurgeLowPriorityStandbyList));

        if (full)
            Step("Sistem dosya önbelleği", () => SetSystemFileCacheSize(UIntPtr.MaxValue, UIntPtr.MaxValue, 0));

        if (Environment.OSVersion.Version.Build >= 9600) // Win 8.1+
            Step("Sayfa birleştirme", CombinePageLists);

        if (full && Environment.OSVersion.Version.Build >= 17134) // Win10 1803+
            Step("Registry önbelleği", () => NtSetSystemInformation(SystemRegistryReconciliationInformation, IntPtr.Zero, 0) >= 0);

        ulong after = AvailPhys();
        string note = null;
        int failed = steps.FindAll(x => !x.Ok).Count;
        if (full && (!admin || !privOk))
            note = "Tam mod için yönetici gerekir: yalnızca kısmen temizlendi";
        else if (failed > 0)
            note = $"{failed} adım başarısız: " + string.Join(", ", steps.FindAll(x => !x.Ok).ConvertAll(x => x.Name));
        return new CleanResult
        {
            FreedMb = Math.Max(0, ((long)after - (long)before) / 1048576.0),
            DurationMs = sw.ElapsedMilliseconds,
            Mode = mode,
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
    static HashSet<string>? _exCache;
    static HashSet<uint>? _exPids;
    static long _exTick;

    /// <summary>Ayar kaydedilince cagrilir; bir sonraki temizlikte liste yeniden olusur.</summary>
    public static void InvalidateExcludeCache() => _exCache = null;

    static HashSet<string> BuildExcludedNames(AppSettings settings, out HashSet<uint> excludedPids)
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

        // Onbellegi kirletmemek icin kopya; on plan PID her seferinde taze.
        excludedPids = new HashSet<uint>(_exPids);
        try
        {
            IntPtr fg = GetForegroundWindow();
            if (fg != IntPtr.Zero && GetWindowThreadProcessId(fg, out uint fgPid) != 0 && fgPid != 0)
                excludedPids.Add(fgPid);
        }
        catch { }
        return _exCache;
    }

    public static string NormalizeName(string n)
    {
        n = (n ?? "").Trim();
        return n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n;
    }

    /// <summary>Toolhelp ile süreçleri sayar (Process nesnesi üretmez), her tutamaç finally'de kapatılır.</summary>
    static void EmptyProcessWorkingSets(AppSettings settings)
    {
        var excludedNames = BuildExcludedNames(settings, out var excludedPids);
        IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return;
        try
        {
            var pe = new PROCESSENTRY32 { dwSize = (uint)Unsafe.SizeOf<PROCESSENTRY32>() };
            if (!Process32FirstW(snap, ref pe)) return;
            var all = new List<(uint pid, uint ppid, string name)>(300);
            do { all.Add((pe.th32ProcessID, pe.th32ParentProcessID, pe.szExeFile)); }
            while (Process32NextW(snap, ref pe));

            // Ön plan sürecin alt süreçlerini (torunlar dahil) de koru.
            var pending = new Queue<uint>(excludedPids);
            while (pending.Count > 0)
            {
                uint parent = pending.Dequeue();
                foreach (var e in all)
                    if (e.ppid == parent && e.pid > 4 && excludedPids.Add(e.pid)) pending.Enqueue(e.pid);
            }

            foreach (var (pid, _, name) in all)
            {
                if (pid <= 4 || excludedPids.Contains(pid)) continue;
                if (excludedNames.Contains(NormalizeName(name))) continue;

                IntPtr h = OpenProcess(PROCESS_SET_QUOTA | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (h == IntPtr.Zero) continue; // korumalı / sistem süreci: sessizce geç
                try { EmptyWorkingSet(h); }
                finally { CloseHandle(h); }
            }
        }
        finally { CloseHandle(snap); }
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
                Privileges = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = SE_PRIVILEGE_ENABLED }
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
