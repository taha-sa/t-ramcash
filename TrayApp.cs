using TSuite.Design;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Runtime;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TSuite.Diagnostics;
using TSuite.Link;
using TSuite.Update;

namespace TRamCash;

/// <summary>
/// Uygulamanin kalici cekirdegi: tepsi simgesi, kisayol, 5 sn'lik hafif arka plan kontrolu, otomatik temizlik,
/// T-Link sunucusu ve guncelleme. Ana pencere (MainForm) yalnizca gorunurken var olur; gizlenince Dispose edilir.
/// </summary>
public sealed class TrayApp : ApplicationContext
{
    public const string AppId = "t-ramcash";
    /// <summary>t-ramcash.pub (ECDSA P-256 SPKI, base64). Ozel anahtar repo disinda: C:\Users\hp\.t-suite\keys\</summary>
    const string UpdatePublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEEZJTP4gCOd19RAqIVGvPFVlWuimLqO8QKJdTnJnBQ2iPiCYcKMpCy93XhcOdoTH9mVE/kq9NCiwTtWxIm+1R2A==";

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSizeEx(IntPtr hProcess, IntPtr min, IntPtr max, uint flags);

    const int HOTKEY_ID = 9000;
    const uint MOD_CONTROL = 0x0002, VK_F1 = 0x70;

    public static string Version { get; } = typeof(TrayApp).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.1.0";

    public AppSettings Settings { get; }
    public MemoryCleaner.MemorySnapshot Snap;
    public string StatusText { get; private set; } = Strings.Ready;
    public bool IsCleaning { get; private set; }
    public bool HotkeyOk;

    public event Action CleaningChanged;
    public event Action<CleanResult> CleanCompleted;

    public CleanMode ManualMode
    {
        get => Settings.ManualMode == "Full" ? CleanMode.Full : CleanMode.Smart;
        set { Settings.ManualMode = value == CleanMode.Full ? "Full" : "Smart"; Settings.Save(); }
    }

    readonly SynchronizationContext _sync = new WindowsFormsSynchronizationContext();
    readonly HotkeyWindow _hot = new();
    readonly NotifyIcon _tray;
    readonly ContextMenuStrip _menu;
    readonly ToolStripMenuItem _menuHeader;
    readonly System.Windows.Forms.Timer _tick = new() { Interval = 5000 };
    readonly AppRegistry _registry = new();
    readonly EventWaitHandle _showEvent;
    readonly RegisteredWaitHandle _showWaitHandle;
    readonly Font _menuHeaderFont;
    LinkServer _link;
    MainForm _main;
    Icon _trayIcon;
    uint _lastIconPct = uint.MaxValue;
    long _lastCleanTick = Environment.TickCount64;
    bool _dialogOpen;
    volatile bool _exiting;
    TaskCompletionSource<CleanResult> _activeClean;   // calisan ram.clean yaniti; cikista iptal edilir

    public TrayApp(EventWaitHandle showEvent, bool startInTray = false)
    {
        _showEvent = showEvent;
        Settings = AppSettings.Load();
        Settings.ApplyStartupRegistration();
        MemoryCleaner.ReadSnapshot(ref Snap);

        _menuHeader = new ToolStripMenuItem("T-RamCash") { ForeColor = TSuiteTokens.ColorAccentTRamCashDarkBase };
        _menuHeaderFont = new Font(SystemFonts.MenuFont, FontStyle.Bold);
        _menuHeader.Font = _menuHeaderFont;
        _menu = new ContextMenuStrip {
            BackColor = TSuiteTokens.ColorNeutralDarkSurface1,
            ForeColor = TSuiteTokens.ColorNeutralDarkText,
            Renderer = new ToolStripProfessionalRenderer(new SuiteMenuColors())
        };
        _menu.Items.Add(_menuHeader);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Strings.ToggleWindow, null, (s, e) => ToggleMain());
        _menu.Items.Add(Strings.CleanSmart, null, (s, e) => RequestClean(CleanMode.Smart, false));
        _menu.Items.Add(Strings.CleanDeep, null, (s, e) => RequestClean(CleanMode.Full, false));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Strings.TaskManager, null, (s, e) => OpenTaskManager());
        _menu.Items.Add(Strings.SettingsMenu, null, (s, e) => OpenSettings(_main));
        _menu.Items.Add(Strings.CheckUpdates, null, (s, e) => CheckUpdatesManually());
        if (!MemoryCleaner.IsAdministrator)
            _menu.Items.Add(Strings.RestartAdmin, null, (s, e) => RestartAsAdmin());
        _menu.Items.Add(Strings.AboutMenu, null, (s, e) => OpenAbout(_main));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Strings.Exit, null, (s, e) => FullExit());
        _menu.Opening += (s, e) =>
        {
            double used = Snap.UsedPhysicalBytes / 1073741824.0, total = Snap.TotalPhysicalBytes / 1073741824.0;
            _menuHeader.Text = $"T-RamCash: {Localization.Percent(Snap.MemoryLoadPercent)} RAM ({Strings.Format(Strings.MemoryPair, used, total)})";
        };

        _tray = new NotifyIcon { Text = "T-RamCash", ContextMenuStrip = _menu, Visible = true };
        _tray.DoubleClick += (s, e) => ToggleMain();
        UpdateTrayIcon();

        _hot.Create();
        HotkeyOk = RegisterHotKey(_hot.Handle, HOTKEY_ID, MOD_CONTROL, VK_F1);
        _hot.HotKey += () => RequestClean(ManualMode, false);

        _tick.Tick += (s, e) => OnTick();
        _tick.Start();

        // Ikinci kopya acilinca "pencereyi goster" sinyali
        _showWaitHandle = ThreadPool.RegisterWaitForSingleObject(_showEvent, (st, to) => { if (!_exiting) _sync.Post(_ => ShowMain(), null); }, null, -1, false);

        StartLink();
        StartUpdateFlow();

        if (startInTray || Settings.StartMinimized) ScheduleTrim(); else ShowMain();
        _sync.Post(_ =>
        {
            if (_exiting || _dialogOpen || CrashReporter.GetPendingReport() is not { } report) return;
            _dialogOpen = true;
            try
            {
                using var dialog = new LocalReportDialog(report);
                _openDialog = dialog;
                dialog.ShowDialog(_main);
            }
            finally { _openDialog = null; _dialogOpen = false; }
        }, null);
    }

    #region Arka plan kontrolu (5 sn)

    void OnTick()
    {
        MemoryCleaner.ReadSnapshot(ref Snap); // sifir allocation
        UpdateTrayIcon();
        AutoCleanCheck();

        if (_link != null && _link.HasSubscriber("ram.status"))
        {
            _link.Publish("ram.status", LinkJson.Obj(
                ("usedMb", (long)(Snap.UsedPhysicalBytes >> 20)), ("totalMb", (long)(Snap.TotalPhysicalBytes >> 20)),
                ("usedPct", (int)Snap.MemoryLoadPercent), ("standbyMb", (long)(Snap.SystemCacheBytes >> 20))));
        }
    }

    // Oran tetikli oto-temizlikte geri cekilme: yetmezse 60 -> 120 -> 300 sn; basarili dususte sifirlanir.
    static readonly int[] RatioBackoffSec = { 60, 120, 300 };
    int _ratioStep;
    long _ratioNextTick;

    void AutoCleanCheck()
    {
        if (IsCleaning || _exiting) return;
        long now = Environment.TickCount64;
        double since = (now - _lastCleanTick) / 1000.0;
        if (since < 10) return; // son temizlikten sonraki 10 sn icinde otomatik temizlik yok

        if (Settings.AutoCleanByRatio && Snap.MemoryLoadPercent >= Settings.AutoCleanRatioThreshold && now >= _ratioNextTick)
        { RequestClean(CleanMode.Smart, true, ratioRun: true); return; }

        if (Settings.AutoCleanByInterval && since >= Settings.AutoCleanIntervalMinutes * 60.0)
            RequestClean(CleanMode.Smart, true); // otomatik her zaman Akilli
    }

    void UpdateTrayIcon()
    {
        uint pct = Snap.MemoryLoadPercent;
        if (pct == _lastIconPct) return;
        _lastIconPct = pct;
        try
        {
            // Sistem kucuk-ikon boyutunda dogrudan ciz (16/20/24/32), kucultme yok.
            using var bmp = TrayIconRender.Render(TrayIconRender.IconSize(), pct, out _);
            IntPtr hIcon = bmp.GetHicon();
            Icon newIcon;
            try { using var tmp = Icon.FromHandle(hIcon); newIcon = (Icon)tmp.Clone(); }
            finally { DestroyIcon(hIcon); }

            var old = _trayIcon;
            _tray.Icon = newIcon;
            _trayIcon = newIcon;
            old?.Dispose();
            _tray.Text = "T-RamCash - " + Localization.Percent(pct) + " RAM";
        }
        catch { }
    }

    #endregion

    #region Temizlik

    /// <summary>Sonucu beklemeden temizlik baslatir (mesgulse sessizce yoksayar).</summary>
    public void RequestClean(CleanMode mode, bool auto, bool ratioRun = false)
        => CleanAsync(mode, auto, ratioRun).ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);

    public Task<CleanResult> CleanAsync(CleanMode mode, bool auto, bool ratioRun = false)
    {
        bool isRatio = auto && ratioRun;
        var tcs = new TaskCompletionSource<CleanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_exiting) { tcs.TrySetException(new InvalidOperationException("exiting")); return tcs.Task; }
        _sync.Post(_ =>
        {
            if (IsCleaning || _exiting) { tcs.TrySetException(new InvalidOperationException("busy")); return; }
            IsCleaning = true;
            _activeClean = tcs;
            StatusText = Strings.CleaningStatus;
            CleaningChanged?.Invoke();
            Task.Run(() =>
            {
                CleanResult r = null; string err = null;
                try { r = MemoryCleaner.Clean(Settings, mode); } catch (Exception ex) { err = ex.Message; }
                if (_exiting) { tcs.TrySetException(new InvalidOperationException("exiting")); return; }
                _sync.Post(__ =>
                {
                    _activeClean = null;
                    if (_exiting) { IsCleaning = false; tcs.TrySetException(new InvalidOperationException("exiting")); return; }
                    Finish(r, auto, isRatio, err);
                    if (r != null) tcs.TrySetResult(r); else tcs.TrySetException(new InvalidOperationException("clean failed"));
                }, null);
            });
        }, null);
        return tcs.Task;
    }

    void Finish(CleanResult r, bool auto, bool ratioRun, string err)
    {
        IsCleaning = false;
        _lastCleanTick = Environment.TickCount64;
        MemoryCleaner.ReadSnapshot(ref Snap);
        UpdateTrayIcon();

        if (ratioRun)
        {
            bool stillHigh = Snap.MemoryLoadPercent >= Settings.AutoCleanRatioThreshold;
            bool little = r == null || r.FreedMb < 100;
            if (stillHigh && little) _ratioStep = Math.Min(_ratioStep + 1, RatioBackoffSec.Length - 1);
            else _ratioStep = 0;
            _ratioNextTick = Environment.TickCount64 + RatioBackoffSec[_ratioStep] * 1000L;
        }

        bool mainHidden = _main == null || _main.IsDisposed || !_main.Visible || _main.WindowState == FormWindowState.Minimized;
        if (r != null)
        {
            StatusText = r.Note ?? Strings.Format(Strings.FreedStatus, r.FreedMb);
            _link?.Publish("ram.cleaned", LinkJson.Obj(("freedMb", r.FreedMb), ("mode", r.Mode == CleanMode.Full ? "full" : "smart")));
            bool wantBalloon = auto ? Settings.AutoCleanNotification : Settings.ShowCleanNotification;
            if (mainHidden && (wantBalloon || (!auto && r.Note != null)))
            {
                string body = r.Note != null
                    ? Strings.Format(Strings.FreedWarning, r.Note, r.FreedMb)
                    : Strings.Format(Strings.FreedBalloon, r.FreedMb);
                _tray.ShowBalloonTip(3000, "T-RamCash", body, r.Note != null ? ToolTipIcon.Warning : ToolTipIcon.Info);
            }
        }
        else
        {
            StatusText = Strings.CleanFailed;
            if (!auto) _tray.ShowBalloonTip(4000, "T-RamCash", Strings.CleanFailed, ToolTipIcon.Error);
        }
        CleaningChanged?.Invoke();
        if (r != null) CleanCompleted?.Invoke(r);
        if (_main == null) ScheduleTrim();
    }

    /// <summary>Uygulamayi yonetici olarak yeniden baslatir (UAC onayi); kullanici reddederse hicbir sey olmaz.</summary>
    public void RestartAsAdmin()
    {
        try
        {
            string exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            using var p = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            if (p != null) FullExit();
        }
        catch (System.ComponentModel.Win32Exception) { /* UAC reddedildi */ }
        catch { }
    }

    #endregion

    #region Pencere yasam dongusu

    public void ToggleMain()
    {
        if (_main != null && _main.Visible && _main.WindowState != FormWindowState.Minimized) HideMain();
        else ShowMain();
    }

    public void ShowMain()
    {
        if (_exiting) return;
        if (_main == null || _main.IsDisposed)
        {
            _main = new MainForm(this);
            _main.FormClosed += (s, e) => { _main = null; ScheduleTrim(); };
        }
        if (!_main.Visible) _main.Show();
        if (_main.WindowState == FormWindowState.Minimized) _main.WindowState = FormWindowState.Normal;
        _main.BringToFront();
        _main.Activate();
    }

    /// <summary>Formu Dispose eder; GC + working set kucultme FormClosed'dan sonra yapilir.</summary>
    public void HideMain()
    {
        var f = _main;
        if (f == null || f.IsDisposed) return;
        // Ayarlar/Hakkinda modal acikken sahibi (ana form) kapatmak diyalogu sahipsiz birakir: once diyalogu iptal et,
        // modal dongu bitince ana formu kapat.
        var dlg = _openDialog;
        if (dlg != null && !dlg.IsDisposed)
        {
            try { dlg.DialogResult = DialogResult.Cancel; dlg.Close(); } catch { }
            _hideDialogTimer?.Dispose();
            int retries = 0;
            var t = new System.Windows.Forms.Timer { Interval = 80 };
            _hideDialogTimer = t;
            t.Tick += (s, e) =>
            {
                if (_exiting || ++retries > 25) { t.Dispose(); if (ReferenceEquals(_hideDialogTimer, t)) _hideDialogTimer = null; return; }
                if (_openDialog != null && !_openDialog.IsDisposed) return; // diyalog hala kapaniyor: bir sonraki tikte dene
                t.Dispose();
                if (ReferenceEquals(_hideDialogTimer, t)) _hideDialogTimer = null;
                var m = _main;
                if (m != null && !m.IsDisposed) m.Close();
            };
            t.Start();
            return;
        }
        f.Close();
    }

    Form _openDialog;
    System.Windows.Forms.Timer _hideDialogTimer;
    System.Windows.Forms.Timer _trimTimer;

    void ScheduleTrim()
    {
        _trimTimer?.Dispose();
        _trimTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _trimTimer.Tick += (s, e) =>
        {
            _trimTimer.Dispose(); _trimTimer = null;
            if (_main == null) TrimSelf();
        };
        _trimTimer.Start();
    }

    /// <summary>Yonetilen yigini (LOH dahil) sikistirir, sonra kendi working set'ini kucultur.</summary>
    public static void TrimSelf()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        SetProcessWorkingSetSizeEx(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1), 0);
    }

    public void OpenSettings(IWin32Window owner)
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try
        {
            using var frm = new SettingsForm(Settings, CheckUpdatesManually);
            _openDialog = frm;
            if (frm.ShowDialog(owner) == DialogResult.OK && _main != null) _main.ApplySettings();
        }
        finally { _openDialog = null; _dialogOpen = false; if (_main == null) ScheduleTrim(); }
    }

    public void OpenAbout(IWin32Window owner)
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try { using var frm = new AboutForm(); _openDialog = frm; frm.ShowDialog(owner); }
        finally { _openDialog = null; _dialogOpen = false; if (_main == null) ScheduleTrim(); }
    }

    static void OpenTaskManager()
    {
        try { Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true })?.Dispose(); } catch { }
    }

    public void FullExit()
    {
        if (_exiting) return;
        _exiting = true;
        // Cikista mesaj dongusu duracak; calisan temizligin sonucu artik islenmeyecegi icin bekleyen istegi bitir.
        var pendingClean = _activeClean; _activeClean = null;
        pendingClean?.TrySetException(new InvalidOperationException("exiting"));
        _tick.Stop();
        _tick.Dispose();
        _trimTimer?.Dispose();
        _trimTimer = null;
        _hideDialogTimer?.Dispose();
        _hideDialogTimer = null;
        try { _showWaitHandle?.Unregister(null); } catch { }
        try { _updateLoopCts?.Cancel(); _updateLoopCts?.Dispose(); } catch { }
        UnregisterHotKey(_hot.Handle, HOTKEY_ID);
        _hot.Destroy();
        try { _registry.MarkStopped(AppId); } catch { }
        try { _link?.DisposeAsync().AsTask().Wait(500); } catch { }
        try { if (_updateCard != null && !_updateCard.IsDisposed) _updateCard.Close(); } catch { }
        try { if (_openDialog != null && !_openDialog.IsDisposed) _openDialog.Close(); } catch { }
        _tray.Visible = false;
        _tray.Dispose();
        _trayIcon?.Dispose();
        _menu.Dispose();
        _menuHeaderFont?.Dispose();
        _main?.Close();
        ExitThread();
    }

    #endregion

    #region T-Link

    void StartLink()
    {
        try
        {
            string[] events = ["ram.status", "ram.cleaned", "app.update.available"];
            string[] commands = ["ram.clean", "app.show", "app.version", .. UpdateLink.Commands];
            _link = new LinkServer(AppId, Version, events, commands);
            // Standart güncelleme komutları (T-Bridge "Hepsini güncelle"): app.update.check / app.update.install
            _updateCtl = new UpdateController(new UpdateChecker(MakeUpdateOptions()));
            WireUpdateCtl(_updateCtl);
            UpdateLink.Register(_link, _updateCtl);
            _link.OnCommand("ram.clean", async (d, ct) =>
            {
                var mode = CleanMode.Smart;
                if (d is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String
                    && string.Equals(m.GetString(), "full", StringComparison.OrdinalIgnoreCase)) mode = CleanMode.Full;
                var r = await CleanAsync(mode, false);
                return LinkJson.Obj(("freedMb", r.FreedMb), ("mode", mode == CleanMode.Full ? "full" : "smart"), ("durationMs", r.DurationMs));
            });
            _link.OnCommand("app.show", (d, ct) => { _sync.Post(_ => ShowMain(), null); return Task.FromResult<JsonElement?>(LinkJson.Obj()); });
            _link.OnCommand("app.version", (d, ct) => Task.FromResult<JsonElement?>(LinkJson.Obj(("version", Version), ("protocol", LinkProtocol.Version))));
            _link.Start();

            _registry.CleanupStale();
            _registry.Write(new AppManifest
            {
                Id = AppId, Name = "T-RamCash", Version = Version, ExePath = Environment.ProcessPath ?? "",
                Pid = Environment.ProcessId, Events = events, Commands = commands,
                ReleasesRepo = "taha-sa/t-ramcash-releases", Icon = System.IO.Path.Combine(AppContext.BaseDirectory, "app.ico")
            });
            AppDomain.CurrentDomain.ProcessExit += (s, e) => { try { _registry.MarkStopped(AppId); } catch { } };
        }
        catch { /* T-Link opsiyonel: hata uygulamayi etkilemez */ }
    }

    #endregion

    #region T-Update

    static readonly System.Net.Http.HttpClient UpdateHttp = new() { Timeout = TimeSpan.FromSeconds(30) };
    UpdateController _updateCtl;
    LocalUpdateDialog _updateCard;
    volatile bool _updateCardOpen;

    public static UpdateOptions MakeUpdateOptions() => new() { AppId = AppId, CurrentVersion = Version, PublicKeyBase64 = UpdatePublicKey, Http = UpdateHttp };
    static UpdateOptions MakeUpdateOptions(TimeSpan minInterval) => new() { AppId = AppId, CurrentVersion = Version, PublicKeyBase64 = UpdatePublicKey, MinInterval = minInterval, Http = UpdateHttp };

    void EnsureUpdateCard()
    {
        EnsureUpdateCard(_updateCtl);
    }

    void EnsureUpdateCard(UpdateController ctl)
    {
        if (_exiting || ctl == null || ctl.Info == null) return;
        if (_updateCard != null && !_updateCard.IsDisposed) { _updateCard.Activate(); return; }
        _updateCard = new LocalUpdateDialog(ctl);
        _updateCardOpen = true;
        _updateCard.FormClosed += (s, e) => { _updateCard = null; _updateCardOpen = false; if (!_exiting) ScheduleTrim(); };
        _updateCard.Show();
    }

    void WireUpdateCtl(UpdateController ctl)
    {
        ctl.ExitRequested += () => { if (!_exiting) _sync.Post(_ => FullExit(), null); };
        ctl.StateChanged += () =>
        {
            if (!_exiting && ctl.State is UpdateState.Downloading) _sync.Post(_ => EnsureUpdateCard(ctl), null);
        };
    }

    CancellationTokenSource _updateLoopCts;
    int _updateChecking;

    /// <summary>Acilista bekleyen guncellemeyi (agsiz) gosterir, 30 sn sonra ve her 12 saatte bir sessizce denetler.</summary>
    void StartUpdateFlow()
    {
        _updateLoopCts = new CancellationTokenSource();
        var ct = _updateLoopCts.Token;
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                await RunUpdateCheckAsync(false, true).ConfigureAwait(false); // bekleyen + ilk kontrol
                using var timer = new PeriodicTimer(TimeSpan.FromHours(12));
                while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                    await RunUpdateCheckAsync(false, false).ConfigureAwait(false);
            }
            catch { /* iptal / ag yok */ }
        });
    }

    /// <summary>Elle denetim: zorlamali; sonucu balonla bildirir.</summary>
    public void CheckUpdatesManually() => _ = RunUpdateCheckAsync(true, false);

    /// <summary>UpdateChecker/Controller yalniz kontrol suresince yasar; ortak HttpClient ile soket sizintisi onlenir.</summary>
    async Task RunUpdateCheckAsync(bool manual, bool loadPending)
    {
        if (_exiting || _updateCardOpen) return;
        if (_updateCtl != null && _updateCtl.IsBusy) return;
        if (Interlocked.CompareExchange(ref _updateChecking, 1, 0) != 0) return;
        bool keepAlive = false;
        try
        {
            var ctl = new UpdateController(new UpdateChecker(MakeUpdateOptions(TimeSpan.FromHours(11))));
            WireUpdateCtl(ctl);
            bool found = loadPending && ctl.LoadPending();
            if (!found) found = await ctl.CheckNow(force: manual).ConfigureAwait(false) && ctl.Info != null;
            if (_exiting) return;
            if (found)
            {
                var info = ctl.Info;
                _link?.Publish("app.update.available", LinkJson.Obj(("version", info.NewVersion), ("notes", info.Notes)));
                _sync.Post(_ => EnsureUpdateCard(ctl), null);
                keepAlive = true; // kart acik kaldigi surece denetleyici gerekli; kart kapaninca birakilir
            }
            else if (manual)
            {
                bool failed = ctl.State == UpdateState.Failed || !string.IsNullOrEmpty(ctl.Error);
                _sync.Post(_ => { if (!_exiting) _tray.ShowBalloonTip(3000, "T-RamCash",
                    failed ? Strings.UpdateCheckFailed : Strings.UpToDate,
                    failed ? ToolTipIcon.Warning : ToolTipIcon.Info); }, null);
            }
        }
        catch
        {
            if (manual && !_exiting) _sync.Post(_ => { if (!_exiting) _tray.ShowBalloonTip(3000, "T-RamCash", Strings.UpdateCheckFailed, ToolTipIcon.Warning); }, null);
        }
        finally
        {
            Interlocked.Exchange(ref _updateChecking, 0);
            if (!keepAlive && !_exiting) _sync.Post(_ => { if (!_exiting && _main == null) ScheduleTrim(); }, null);
        }
    }

    internal static readonly UpdateTheme Theme = new(
        Bg: TSuiteTokens.ColorNeutralDarkBg, Surface: TSuiteTokens.ColorNeutralDarkSurface1, Border: TSuiteTokens.ColorNeutralDarkBorder,
        Accent: TSuiteTokens.ColorAccentTRamCashDarkBase, AccentHover: TSuiteTokens.ColorAccentTRamCashDarkHover,
        Text: TSuiteTokens.ColorNeutralDarkText, TextDim: TSuiteTokens.ColorNeutralDarkTextMuted)
    { OnAccent = TSuiteTokens.ColorAccentTRamCashDarkOnAccent };

    #endregion

    sealed class SuiteMenuColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected => TSuiteTokens.ColorNeutralDarkSurface3;
        public override Color MenuItemBorder => TSuiteTokens.ColorAccentTRamCashDarkBase;
        public override Color MenuBorder => TSuiteTokens.ColorNeutralDarkBorder;
        public override Color ImageMarginGradientBegin => TSuiteTokens.ColorNeutralDarkSurface1;
        public override Color ImageMarginGradientMiddle => TSuiteTokens.ColorNeutralDarkSurface1;
        public override Color ImageMarginGradientEnd => TSuiteTokens.ColorNeutralDarkSurface1;
        public override Color SeparatorDark => TSuiteTokens.ColorNeutralDarkBorder;
        public override Color SeparatorLight => TSuiteTokens.ColorNeutralDarkSurface1;
    }

    sealed class HotkeyWindow : NativeWindow
    {
        public event Action HotKey;
        public void Create() => CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); // HWND_MESSAGE
        public void Destroy() { if (Handle != IntPtr.Zero) DestroyHandle(); }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == HOTKEY_ID) { HotKey?.Invoke(); return; }
            base.WndProc(ref m);
        }
    }
}
