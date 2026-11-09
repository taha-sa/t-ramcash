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
    public string StatusText { get; private set; } = "Sistem izleniyor • Hazır";
    public bool IsCleaning { get; private set; }

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
    readonly Bitmap _iconBmp = new(32, 32);
    readonly Graphics _iconG;
    readonly Font _iconFont = new("Segoe UI", 15.5f, FontStyle.Bold);
    readonly StringFormat _iconSf = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
    readonly AppRegistry _registry = new();
    readonly EventWaitHandle _showEvent;
    LinkServer _link;
    MainForm _main;
    Icon _trayIcon;
    uint _lastIconPct = uint.MaxValue;
    DateTime _lastCleanUtc = DateTime.UtcNow;
    bool _dialogOpen;
    bool _exiting;

    public TrayApp(EventWaitHandle showEvent)
    {
        _showEvent = showEvent;
        Settings = AppSettings.Load();
        MemoryCleaner.ReadSnapshot(ref Snap);

        _iconG = Graphics.FromImage(_iconBmp);
        _iconG.SmoothingMode = SmoothingMode.AntiAlias;
        _iconG.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        _menuHeader = new ToolStripMenuItem("T-RamCash") { ForeColor = Color.DarkGoldenrod };
        _menuHeader.Font = new Font(SystemFonts.MenuFont, FontStyle.Bold);
        _menu = new ContextMenuStrip();
        _menu.Items.Add(_menuHeader);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Pencereyi Göster / Gizle", null, (s, e) => ToggleMain());
        _menu.Items.Add("Akıllı Temizle", null, (s, e) => RequestClean(CleanMode.Smart, false));
        _menu.Items.Add("Tam Temizle", null, (s, e) => RequestClean(CleanMode.Full, false));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Görev Yöneticisi", null, (s, e) => OpenTaskManager());
        _menu.Items.Add("Ayarlar...", null, (s, e) => OpenSettings(_main));
        _menu.Items.Add("Hakkında...", null, (s, e) => OpenAbout(_main));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Çıkış", null, (s, e) => FullExit());
        _menu.Opening += (s, e) =>
        {
            double used = Snap.UsedPhysicalBytes / 1073741824.0, total = Snap.TotalPhysicalBytes / 1073741824.0;
            _menuHeader.Text = $"T-RamCash: %{Snap.MemoryLoadPercent:00} RAM ({used:F1} GB / {total:F1} GB)";
        };

        _tray = new NotifyIcon { Text = "T-RamCash", ContextMenuStrip = _menu, Visible = true };
        _tray.DoubleClick += (s, e) => ToggleMain();
        UpdateTrayIcon();

        _hot.Create();
        RegisterHotKey(_hot.Handle, HOTKEY_ID, MOD_CONTROL, VK_F1);
        _hot.HotKey += () => RequestClean(ManualMode, false);

        _tick.Tick += (s, e) => OnTick();
        _tick.Start();

        // Ikinci kopya acilinca "pencereyi goster" sinyali
        ThreadPool.RegisterWaitForSingleObject(_showEvent, (st, to) => _sync.Post(_ => ShowMain(), null), null, -1, false);

        StartLink();
        ScheduleUpdateCheck();

        if (Settings.StartMinimized) ScheduleTrim(); else ShowMain();
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

    void AutoCleanCheck()
    {
        if (IsCleaning) return;
        var now = DateTime.UtcNow;
        double since = (now - _lastCleanUtc).TotalSeconds;
        if (since < 10) return; // son temizlikten sonraki 10 sn icinde otomatik temizlik yok

        if (Settings.AutoCleanByRatio && Snap.MemoryLoadPercent >= Settings.AutoCleanRatioThreshold && since > 60)
        { RequestClean(CleanMode.Smart, true); return; }

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
            _iconG.Clear(Color.FromArgb(14, 14, 18));
            _iconG.DrawRectangle(Gdi_Pen, 1, 1, 29, 29);
            _iconG.DrawString(pct.ToString(), _iconFont, Brushes.White, new RectangleF(0, 0, 32, 32), _iconSf);

            IntPtr hIcon = _iconBmp.GetHicon();
            Icon newIcon;
            try { using var tmp = Icon.FromHandle(hIcon); newIcon = (Icon)tmp.Clone(); }
            finally { DestroyIcon(hIcon); }

            var old = _trayIcon;
            _tray.Icon = newIcon;
            _trayIcon = newIcon;
            old?.Dispose();
            _tray.Text = "T-RamCash - %" + pct + " RAM";
        }
        catch { }
    }

    static readonly Pen Gdi_Pen = new(Color.FromArgb(212, 160, 23), 1.5f);

    #endregion

    #region Temizlik

    /// <summary>Sonucu beklemeden temizlik baslatir (mesgulse sessizce yoksayar).</summary>
    public void RequestClean(CleanMode mode, bool auto)
        => CleanAsync(mode, auto).ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);

    public Task<CleanResult> CleanAsync(CleanMode mode, bool auto)
    {
        var tcs = new TaskCompletionSource<CleanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _sync.Post(_ =>
        {
            if (IsCleaning || _exiting) { tcs.TrySetException(new InvalidOperationException("busy")); return; }
            IsCleaning = true;
            StatusText = "Temizleniyor...";
            CleaningChanged?.Invoke();
            Task.Run(() =>
            {
                CleanResult r = null;
                try { r = MemoryCleaner.Clean(Settings, mode); } catch { }
                _sync.Post(__ =>
                {
                    Finish(r);
                    if (r != null) tcs.TrySetResult(r); else tcs.TrySetException(new InvalidOperationException("clean failed"));
                }, null);
            });
        }, null);
        return tcs.Task;
    }

    void Finish(CleanResult r)
    {
        IsCleaning = false;
        _lastCleanUtc = DateTime.UtcNow;
        MemoryCleaner.ReadSnapshot(ref Snap);
        UpdateTrayIcon();
        if (r != null)
        {
            StatusText = $"{r.FreedMb:F1} MB serbest";
            _link?.Publish("ram.cleaned", LinkJson.Obj(("freedMb", r.FreedMb), ("mode", r.Mode == CleanMode.Full ? "full" : "smart")));
            if (_main == null && Settings.ShowCleanNotification)
                _tray.ShowBalloonTip(3000, "T-RamCash", $"Bellek temizlendi!\n{r.FreedMb:F1} MB RAM serbest bırakıldı.", ToolTipIcon.Info);
        }
        else StatusText = "Temizlik başarısız";
        CleaningChanged?.Invoke();
        if (r != null) CleanCompleted?.Invoke(r);
        if (_main == null) ScheduleTrim();
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
        if (f != null && !f.IsDisposed) f.Close();
    }

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
            using var frm = new SettingsForm(Settings);
            if (frm.ShowDialog(owner) == DialogResult.OK && _main != null) _main.ApplySettings();
        }
        finally { _dialogOpen = false; if (_main == null) ScheduleTrim(); }
    }

    public void OpenAbout(IWin32Window owner)
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try { using var frm = new AboutForm(); frm.ShowDialog(owner); }
        finally { _dialogOpen = false; if (_main == null) ScheduleTrim(); }
    }

    static void OpenTaskManager()
    {
        try { Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true })?.Dispose(); } catch { }
    }

    public void FullExit()
    {
        if (_exiting) return;
        _exiting = true;
        _tick.Stop();
        UnregisterHotKey(_hot.Handle, HOTKEY_ID);
        _hot.Destroy();
        try { _registry.Remove(AppId); } catch { }
        try { _link?.DisposeAsync().AsTask().Wait(500); } catch { }
        _tray.Visible = false;
        _tray.Dispose();
        _trayIcon?.Dispose();
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
            string[] commands = ["ram.clean", "app.show", "app.version"];
            _link = new LinkServer(AppId, Version, events, commands);
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
            AppDomain.CurrentDomain.ProcessExit += (s, e) => { try { _registry.Remove(AppId); } catch { } };
        }
        catch { /* T-Link opsiyonel: hata uygulamayi etkilemez */ }
    }

    #endregion

    #region T-Update

    void ScheduleUpdateCheck()
    {
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                var checker = new UpdateChecker(new UpdateOptions { AppId = AppId, CurrentVersion = Version, PublicKeyBase64 = UpdatePublicKey });
                var ctl = new UpdateController(checker);
                if (!await ctl.CheckAsync().ConfigureAwait(false) || ctl.Info == null) return;
                var info = ctl.Info;
                _link?.Publish("app.update.available", LinkJson.Obj(("version", info.NewVersion), ("notes", info.Notes)));
                ctl.ExitRequested += () => _sync.Post(_ => FullExit(), null);
                _sync.Post(_ => new UpdateCard(ctl, Theme).Show(), null);
            }
            catch { /* ag yoksa sessizce gec */ }
        });
    }

    static readonly UpdateTheme Theme = new(
        Bg: Color.FromArgb(14, 14, 18), Surface: Color.FromArgb(20, 20, 26), Border: Color.FromArgb(34, 34, 44),
        Accent: Color.FromArgb(212, 160, 23), AccentHover: Color.FromArgb(228, 176, 36),
        Text: Color.FromArgb(240, 240, 245), TextDim: Color.FromArgb(145, 145, 158))
    { OnAccent = Color.Black };

    #endregion

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
