using TSuite.Design;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TRamCash;

/// <summary>
/// Ana pencere. Yalnizca gorunurken yasar: gizlenince TrayApp tarafindan Dispose edilir.
/// Tum GDI+ nesneleri Gdi onbelleginden gelir; boyama sirasinda nesne/string uretilmez.
/// </summary>
public class MainForm : Form
{
    #region Win32

    [DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
    static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

    const int WM_NCLBUTTONDOWN = 0xA1;
    const int HT_CAPTION = 0x2;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_MINIMIZEBOX = 0x00020000;
            const int WS_SYSMENU = 0x00080000;
            const int CS_DROPSHADOW = 0x00020000;
            CreateParams cp = base.CreateParams;
            cp.Style |= WS_MINIMIZEBOX | WS_SYSMENU;
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }

    #endregion

    // T-Suite neutral palette and T-RamCash violet accent.
    static readonly Color BgDark = TSuiteTokens.ColorNeutralDarkBg;
    static readonly Color CardBg = TSuiteTokens.ColorNeutralDarkSurface1;
    static readonly Color CardBorder = TSuiteTokens.ColorNeutralDarkBorder;
    static readonly Color Accent = TSuiteTokens.ColorAccentTRamCashDarkBase;
    static readonly Color AccentHover = TSuiteTokens.ColorAccentTRamCashDarkHover;
    static readonly Color TextWhite = TSuiteTokens.ColorNeutralDarkText;
    static readonly Color TextMuted = TSuiteTokens.ColorNeutralDarkTextMuted;
    static readonly Color TextDim = TSuiteTokens.ColorNeutralDarkTextFaint;
    static readonly Color Surface = TSuiteTokens.ColorNeutralDarkSurface2;

    const int HNone = 0, HClose = 1, HMin = 2, HSettings = 3, HAbout = 4, HClean = 5, HSmart = 6, HFull = 7, HAdmin = 8, HModeHelp = 9;
    // Tab sirasi
    static readonly int[] FocusOrder = { HAbout, HSettings, HMin, HClose, HSmart, HFull, HModeHelp, HClean };
    int _focus = HNone;
    ToolTip _tip, _modeTip;
    static string ModeHelpText => Strings.ModeHelp;

    readonly TrayApp _app;
    readonly System.Windows.Forms.Timer _refresh = new() { Interval = 1000 };
    readonly System.Windows.Forms.Timer _anim = new() { Interval = 16 };
    readonly Stopwatch _clock = Stopwatch.StartNew();
    Bitmap _logo;
    RcLayout _L;
    int S(int v) => _L.S(v);
    Icon _icon;

    // Yerlesim
    Rectangle _rcClose, _rcMin, _rcSettings, _rcAbout, _rcCleanBtn, _rcToggle, _rcSmart, _rcFull, _rcAdmin;
    int _hover = HNone;

    // Onbellekli metinler (yalnizca veri degisince yeniden uretilir)
    string _sPct = "", _sUsed = "", _sFree = "", _sPage = "", _sPagePct = "", _sCache = "", _sInfo = "";
    ulong _kAvail; uint _kLoad, _kProc, _kPageLoad; string _kStatus; bool _kCleaning;

    // Animasyon durumu
    readonly float[] _seg = new float[2];   // 0 = Akilli, 1 = Tam secili orani
    long _lastAnimMs;
    string _toastText; long _toastStart = -1; Rectangle _toastRect;
    const int ToastIn = 150, ToastHold = 3000, ToastOut = 150;

    public MainForm(TrayApp app)
    {
        _app = app;
        Gdi.Acquire();

        try
        {
            SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            _L = RcLayout.Compute(DeviceDpi);
            Size = new Size(_L.W, _L.H);
            BackColor = BgDark;
            TopMost = app.Settings.AlwaysOnTop;
            Text = "T-RamCash";
            ShowInTaskbar = true;

            try
            {
                string icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "app.ico");
                if (System.IO.File.Exists(icoPath)) _icon = new Icon(icoPath, 32, 32);
                if (_icon != null) Icon = _icon;
            }
            catch { }

            SetLayoutRects();
            AccessibleName = "T-RamCash"; AccessibleRole = AccessibleRole.Window;

            _seg[0] = app.ManualMode == CleanMode.Smart ? 1f : 0f;
            _seg[1] = 1f - _seg[0];

            ApplyRegion();
            RebuildLogo();

            _refresh.Tick += (s, e) => RefreshData();
            _anim.Tick += (s, e) => AnimTick();
            _app.CleaningChanged += OnCleaningChanged;
            _app.CleanCompleted += OnCleanCompleted;

            RefreshData();
            UpdateTimers();
        }
        catch { Gdi.Release(); throw; }
    }

    void SetLayoutRects()
    {
        _rcClose = _L.Close; _rcMin = _L.Min; _rcSettings = _L.Settings; _rcAbout = _L.About;
        _rcToggle = _L.Toggle; _rcSmart = _L.Smart; _rcFull = _L.Full; _rcCleanBtn = _L.CleanBtn; _rcAdmin = _L.Admin;
    }

    void ApplyRegion()
    {
        int rr = S(16);
        IntPtr h = CreateRoundRectRgn(0, 0, Width + 1, Height + 1, rr, rr);
        if (h == IntPtr.Zero) return;
        using var ptr = new RegionHandle(h);
        var old = Region;
        Region = Region.FromHrgn(ptr.Handle);
        old?.Dispose();
    }

    void RebuildLogo()
    {
        _logo?.Dispose();
        int ls = S(20);
        _logo = new Bitmap(ls, ls, PixelFormat.Format32bppPArgb);
        using var lg = Graphics.FromImage(_logo);
        DrawCustomLogo(lg, 0, 0, ls);
    }

    // PerMonitorV2: monitor DPI degisince tum yerlesim, onbellekler ve pencere boyutu yeniden hesaplanir.
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (IsHandleCreated) BeginInvoke(new Action(ApplyDpi)); else ApplyDpi();
    }

    void ApplyDpi()
    {
        if (IsDisposed) return;
        _L = RcLayout.Compute(DeviceDpi);
        SetLayoutRects();
        _modeTip?.Hide(this);
        Gdi.ResetScaled();
        if (Size != new Size(_L.W, _L.H)) Size = new Size(_L.W, _L.H);
        ApplyRegion();
        RebuildLogo();
        _toastStart = -1;
        _toastRect = Rectangle.Empty;
        Invalidate();
    }

    sealed class RegionHandle : IDisposable
    {
        public IntPtr Handle { get; }
        public RegionHandle(IntPtr h) { Handle = h; }
        public void Dispose() => DeleteObject(Handle);
    }

    bool _gdiReleased;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _app.CleaningChanged -= OnCleaningChanged;
            _app.CleanCompleted -= OnCleanCompleted;
            _refresh.Dispose();
            _anim.Dispose();
            _logo?.Dispose();
            _tip?.Dispose();
            _modeTip?.Dispose();
        }
        base.Dispose(disposing);
        if (disposing)
        {
            _icon?.Dispose();
            if (!_gdiReleased) { _gdiReleased = true; Gdi.Release(); }
        }
    }

    public void ApplySettings()
    {
        TopMost = _app.Settings.AlwaysOnTop;
        RefreshData();
    }

    #region Zamanlayicilar

    // Gizliyken / kucukken 1 sn'lik UI zamanlayicisi tamamen durur.
    void UpdateTimers()
    {
        bool active = Visible && WindowState != FormWindowState.Minimized;
        if (active && !_refresh.Enabled) { RefreshData(); _refresh.Start(); }
        else if (!active && _refresh.Enabled) _refresh.Stop();
        if (!active) _anim.Stop(); else if (NeedsAnim()) EnsureAnim();
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); UpdateTimers(); }
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateTimers();
        if (WindowState == FormWindowState.Minimized && IsHandleCreated) BeginInvoke(new Action(_app.HideMain)); // kucultunce tepsiye gizle
    }

    #endregion

    #region Veri & metin onbellegi

    void RefreshData()
    {
        MemoryCleaner.ReadSnapshot(ref _app.Snap);
        ref var s = ref _app.Snap;
        bool cleaning = _app.IsCleaning;
        string status = _app.StatusText;

        if (s.AvailablePhysicalBytes >> 20 == _kAvail && s.MemoryLoadPercent == _kLoad && s.ProcessCount == _kProc
            && s.PageFileLoadPercent == _kPageLoad && ReferenceEquals(status, _kStatus) && cleaning == _kCleaning)
            return; // degisiklik yok: yeniden boyama yok

        _kAvail = s.AvailablePhysicalBytes >> 20; _kLoad = s.MemoryLoadPercent; _kProc = s.ProcessCount;
        _kPageLoad = s.PageFileLoadPercent; _kStatus = status; _kCleaning = cleaning;

        const double GB = 1024.0 * 1024.0 * 1024.0;
        _sPct = Localization.Percent(s.MemoryLoadPercent);
        _sUsed = Strings.Format(Strings.MemoryPair, s.UsedPhysicalBytes / GB, s.TotalPhysicalBytes / GB);
        _sFree = Strings.Format(Strings.Available, s.AvailablePhysicalBytes / GB);
        _sPagePct = Localization.Percent(s.PageFileLoadPercent);
        _sPage = Strings.Format(Strings.MemoryPair, s.UsedPageFileBytes / GB, s.TotalPageFileBytes / GB);
        _sCache = Strings.Format(Strings.CacheSize, s.SystemCacheBytes / (1024.0 * 1024.0));
        _sInfo = Strings.Format(Strings.StatusProcesses, status, s.ProcessCount);
        Invalidate(_L.InfoInv); // bilgi satiri
        Invalidate(_L.CardsInv); // bellek kartlari
    }

    #endregion

    #region Olaylar (TrayApp)

    void OnCleaningChanged()
    {
        _kStatus = null; // metni yenile
        RefreshData();
        Invalidate();
        if (_app.IsCleaning) EnsureAnim();
    }

    void OnCleanCompleted(CleanResult r)
    {
        double mb = r.FreedMb;
        string freed = mb < 1 ? Strings.NoMemory
            : mb >= 1024 ? Strings.Format(Strings.FreedGb, mb / 1024.0) : Strings.Format(Strings.FreedMb, mb);
        _toastText = r.Note != null
            ? $"{freed} · {r.Note}"
            : Strings.Format(Strings.ToastResult, freed, r.Mode == CleanMode.Full ? Strings.Deep : Strings.Smart, r.DurationMs);
        var sz = TextRenderer.MeasureText(_toastText, Gdi.GetFont("Segoe UI", 9f, FontStyle.Regular));
        int w = Math.Min(Width - S(32), sz.Width + S(32));
        var oldToast = _toastRect;
        _toastRect = new Rectangle((Width - w) / 2, Height - S(16) - S(34), w, S(34));
        _toastStart = _clock.ElapsedMilliseconds;
        EnsureAnim();
        if (!oldToast.IsEmpty) Invalidate(Rectangle.Inflate(oldToast, S(4), S(14)));
        Invalidate(Rectangle.Inflate(_toastRect, S(4), S(14)));
    }

    #endregion

    #region Animasyon (yalnizca gerekirken calisir)

    bool NeedsAnim()
    {
        if (_app.IsCleaning || _toastStart >= 0) return true;
        float t0 = _app.ManualMode == CleanMode.Smart ? 1f : 0f;
        return _seg[0] != t0 || _seg[1] != 1f - t0;
    }

    void EnsureAnim()
    {
        if (!_anim.Enabled && Visible) { _lastAnimMs = _clock.ElapsedMilliseconds; _anim.Start(); }
    }

    void AnimTick()
    {
        long now = _clock.ElapsedMilliseconds;
        float step = (now - _lastAnimMs) / 120f; // 120 ms renk gecisi
        _lastAnimMs = now;
        float t0 = _app.ManualMode == CleanMode.Smart ? 1f : 0f;
        _seg[0] = MoveToward(_seg[0], t0, step);
        _seg[1] = MoveToward(_seg[1], 1f - t0, step);

        bool toastEnding = _toastStart >= 0 && now - _toastStart > ToastIn + ToastHold + ToastOut;
        if (toastEnding) _toastStart = -1;

        Invalidate(Rectangle.Inflate(_rcToggle, S(2), S(2)));
        Invalidate(Rectangle.Inflate(_rcCleanBtn, S(2), S(2)));
        if (!_toastRect.IsEmpty) Invalidate(Rectangle.Inflate(_toastRect, S(4), S(14)));
        if (!NeedsAnim()) _anim.Stop();
    }

    static float MoveToward(float v, float target, float step)
        => v < target ? Math.Min(target, v + step) : Math.Max(target, v - step);

    #endregion

    #region Boyama

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        g.DrawRectangle(Gdi.GetPen(CardBorder), 0, 0, Width - 1, Height - 1);

        DrawHorizontalHeader(g);
        DrawColumn(g, _L.Card1, Strings.PhysicalMemory, _sPct, _sUsed, _app.Snap.MemoryLoadPercent, _sFree);
        DrawColumn(g, _L.Card2, Strings.VirtualCache, _sPagePct, _sPage, _app.Snap.PageFileLoadPercent, _sCache);
        DrawColumnAction(g, _L.Card3);
        if (_toastStart >= 0) DrawToast(g);
        DrawFocus(g);
    }

    void DrawHorizontalHeader(Graphics g)
    {
        g.DrawImageUnscaled(_logo, S(20), S(12));
        g.DrawString("T-RamCash", Gdi.GetFont("Segoe UI", 10.5F, FontStyle.Bold), Gdi.GetBrush(Accent), S(48), S(12));
        DrawFit(g, _sInfo, 8.5F, FontStyle.Regular, TextDim, _L.InfoText, Gdi.VCenterLeft);

        DrawNormalBtn(g, _rcAbout, HAbout, _hover == HAbout);
        DrawNormalBtn(g, _rcSettings, HSettings, _hover == HSettings);
        DrawNormalBtn(g, _rcMin, HMin, _hover == HMin);
        DrawNormalBtn(g, _rcClose, HClose, _hover == HClose);

        g.DrawLine(Gdi.GetPen(CardBorder), S(20), S(44), Width - S(20), S(44));
    }

    void DrawCustomLogo(Graphics g, int x, int y, int s)
    {
        using var image = BrandAssets.LoadLogo("t-ramcash");
        g.DrawImage(image, x, y, s, s);
    }

    static GraphicsPath CreateRoundedRectanglePath(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2f;
        var arc = new RectangleF(rect.X, rect.Y, d, d);
        path.AddArc(arc, 180, 90);
        arc.X = rect.Right - d; path.AddArc(arc, 270, 90);
        arc.Y = rect.Bottom - d; path.AddArc(arc, 0, 90);
        arc.X = rect.Left; path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    // Baslik dugmesi simgeleri: yazi tipi glifi yerine vektor cizim (Segoe UI Symbol yoksa bile gorunur).
    void DrawNormalBtn(Graphics g, Rectangle rc, int id, bool hover)
    {
        if (hover) g.FillRectangle(Gdi.GetBrush(Surface), rc);
        var col = hover ? Accent : TextMuted;
        float lw = Math.Max(1.25f, S(2) * 0.7f);
        var pen = Gdi.GetPen(col, lw, round: true);
        float cx = rc.X + rc.Width / 2f, cy = rc.Y + rc.Height / 2f;
        float u = S(1);
        switch (id)
        {
            case HClose:
                g.DrawLine(pen, cx - 4.5f * u, cy - 4.5f * u, cx + 4.5f * u, cy + 4.5f * u);
                g.DrawLine(pen, cx - 4.5f * u, cy + 4.5f * u, cx + 4.5f * u, cy - 4.5f * u);
                break;
            case HMin:
                g.DrawLine(pen, cx - 5f * u, cy, cx + 5f * u, cy);
                break;
            case HAbout:
                g.DrawEllipse(pen, cx - 6f * u, cy - 6f * u, 12f * u, 12f * u);
                g.DrawLine(pen, cx, cy - 0.5f * u, cx, cy + 3f * u);
                g.FillEllipse(Gdi.GetBrush(col), cx - 1f * u, cy - 3.8f * u, 2f * u, 2f * u);
                break;
            case HModeHelp:
                g.DrawEllipse(pen, cx - 7f * u, cy - 7f * u, 14f * u, 14f * u);
                using (var question = new GraphicsPath())
                {
                    question.AddBezier(cx - 2.5f * u, cy - 2.5f * u, cx - 2.5f * u, cy - 6f * u, cx + 5f * u, cy - 4f * u, cx + 2f * u, cy - 0.5f * u);
                    question.AddLine(cx + 2f * u, cy - 0.5f * u, cx, cy + 1.5f * u);
                    g.DrawPath(pen, question);
                }
                g.FillEllipse(Gdi.GetBrush(col), cx - 0.8f * u, cy + 3.5f * u, 1.6f * u, 1.6f * u);
                break;
            case HSettings:
                using (var gp = GearPath(cx, cy, 6.5f * u, 4.6f * u, 8)) g.DrawPath(pen, gp);
                g.DrawEllipse(pen, cx - 2f * u, cy - 2f * u, 4f * u, 4f * u);
                break;
        }
    }

    static GraphicsPath GearPath(float cx, float cy, float ro, float ri, int teeth)
    {
        var path = new GraphicsPath();
        var pts = new PointF[teeth * 4];
        double step = Math.PI * 2 / teeth;
        for (int i = 0; i < teeth; i++)
        {
            double a = i * step - step / 2;
            double[] ang = { a + step * 0.12, a + step * 0.30, a + step * 0.62, a + step * 0.80 };
            float[] rad = { ri, ro, ro, ri };
            for (int k = 0; k < 4; k++)
                pts[i * 4 + k] = new PointF(cx + (float)(Math.Cos(ang[k]) * rad[k]), cy + (float)(Math.Sin(ang[k]) * rad[k]));
        }
        path.AddPolygon(pts);
        return path;
    }

    /// <summary>Metni kutuya sigdirarak cizer: sigmazsa yazi tipi 0,5 pt adimlarla kuculur (en az 6 pt).</summary>
    void DrawFit(Graphics g, string text, float pt, FontStyle st, Color c, RectangleF box, StringFormat fmt)
    {
        if (string.IsNullOrEmpty(text)) return;
        var f = Gdi.FitFont(g, text, "Segoe UI", pt, st, box.Width);
        g.DrawString(text, f, Gdi.GetBrush(c), box, fmt);
    }

    void DrawColumn(Graphics g, Rectangle card, string title, string pct, string sub, uint load, string foot)
    {
        int x = card.X, y = card.Y, w = card.Width;
        FillCard(g, card);
        int pad = S(16);
        float inner = w - 2 * pad;
        DrawFit(g, title, 8F, FontStyle.Bold, TextMuted, new RectangleF(x + pad, y + S(16), inner, S(18)), Gdi.TopLeft);
        DrawFit(g, pct, 24F, FontStyle.Bold, TextWhite, new RectangleF(x + S(14), y + S(36), inner + S(2), S(48)), Gdi.TopLeft);
        DrawFit(g, sub, 8.5F, FontStyle.Regular, TextMuted, new RectangleF(x + pad, y + S(88), inner, S(22)), Gdi.TopLeft);

        int barW = w - 2 * pad, barY = y + S(114), barH = Math.Max(2, S(4));
        g.FillRectangle(Gdi.GetBrush(TSuiteTokens.ColorNeutralDarkSurface3), x + pad, barY, barW, barH);
        int fillW = (int)((barW * Math.Min(100u, load)) / 100.0);
        if (fillW > 0) g.FillRectangle(Gdi.GetBrush(Accent), x + pad, barY, fillW, barH);

        DrawFit(g, foot, 8.5F, FontStyle.Regular, TextDim, new RectangleF(x + pad, y + S(140), inner, S(22)), Gdi.TopLeft);
    }

    void DrawColumnAction(Graphics g, Rectangle card)
    {
        int x = card.X, y = card.Y, w = card.Width;
        int pad = S(16);
        float inner = w - 2 * pad;
        FillCard(g, card);
        bool admin = MemoryCleaner.IsAdministrator;

        DrawFit(g, admin ? Strings.AdminMode : Strings.StandardMode, 8F, FontStyle.Bold, admin ? Accent : TextMuted,
            new RectangleF(x + pad, y + S(16), inner, S(18)), Gdi.TopLeft);
        DrawFit(g, admin ? Strings.FullPermission : Strings.NeedAdmin, 7.5F, FontStyle.Regular, TextDim,
            new RectangleF(x + pad, y + S(34), inner, S(14)), Gdi.TopLeft);

        if (!admin)
            DrawFit(g, Strings.AdminLink, 7F, FontStyle.Underline, _hover == HAdmin ? TextWhite : Accent,
                new RectangleF(x + pad, y + S(48), inner, S(16)), Gdi.TopLeft);
        DrawToggle(g);
        DrawCleanButton(g);

        DrawFit(g, _app.HotkeyOk ? Strings.Hotkey : Strings.HotkeyBusy, 8F, FontStyle.Regular, TextDim,
            new RectangleF(x, y + S(148), w, S(32)), Gdi.HCenter);
    }

    // Segmentli anahtar: [ Akıllı | Tam ]
    void DrawToggle(Graphics g)
    {
        g.FillPath(Gdi.GetBrush(BgDark), Gdi.RoundedPath(_rcToggle, S(8)));
        g.DrawPath(Gdi.GetPen(CardBorder), Gdi.RoundedPath(_rcToggle, S(8)));
        DrawSegment(g, _rcSmart, Strings.Smart, _seg[0], _hover == HSmart);
        DrawSegment(g, _rcFull, Strings.Deep, _seg[1], _hover == HFull);
        DrawNormalBtn(g, _L.ModeHelp, HModeHelp, _hover == HModeHelp);
    }

    void DrawSegment(Graphics g, Rectangle r, string text, float t, bool hover)
    {
        var inner = Rectangle.Inflate(r, -S(3), -S(3));
        if (t > 0.01f)
        {
            var path = Gdi.RoundedPath(inner, S(6));
            if (t >= 0.999f)
                g.FillPath(Gdi.GetBrush(Accent), path);
            else
            {
                using var b = new SolidBrush(Color.FromArgb((int)(255 * t), Accent));
                g.FillPath(b, path);
            }
        }
        Color unselected = hover ? TextWhite : TextMuted;
        if (t <= 0.001f || t >= 0.999f)
            g.DrawString(text, Gdi.GetFont("Segoe UI", 8.5F, FontStyle.Bold), Gdi.GetBrush(t >= 0.999f ? TSuiteTokens.ColorAccentTRamCashDarkOnAccent : unselected), r, Gdi.Center);
        else
        {
            using var tb = new SolidBrush(Gdi.Lerp(unselected, TSuiteTokens.ColorAccentTRamCashDarkOnAccent, t));
            g.DrawString(text, Gdi.GetFont("Segoe UI", 8.5F, FontStyle.Bold), tb, r, Gdi.Center);
        }
    }

    void DrawCleanButton(Graphics g)
    {
        var r = _rcCleanBtn;
        var path = Gdi.RoundedPath(r, S(8));
        if (_app.IsCleaning)
        {
            g.FillPath(Gdi.GetBrush(Surface), path);
            g.DrawPath(Gdi.GetPen(CardBorder), path);
            g.DrawString(Strings.Cleaning, Gdi.GetFont("Segoe UI", 9.5F, FontStyle.Bold), Gdi.GetBrush(Accent), r, Gdi.Center);

            // Animated accent underline.
            float phase = (_clock.ElapsedMilliseconds % 1100) / 1100f;
            int span = r.Width - S(24), segW = span / 3;
            int sx = r.X + S(12) + (int)((span + segW) * phase) - segW;
            int x0 = Math.Max(sx, r.X + S(12)), x1 = Math.Min(sx + segW, r.X + S(12) + span);
            float lw = Math.Max(2f, S(2));
            g.DrawLine(Gdi.GetPen(CardBorder, lw), r.X + S(12), r.Bottom - S(10), r.X + S(12) + span, r.Bottom - S(10));
            if (x1 > x0) g.DrawLine(Gdi.GetPen(Accent, lw), x0, r.Bottom - S(10), x1, r.Bottom - S(10));
        }
        else
        {
            g.FillPath(Gdi.GetBrush(_hover == HClean ? AccentHover : Accent), path);
            g.DrawString(Strings.CleanNow, Gdi.GetFont("Segoe UI", 9.5F, FontStyle.Bold), Gdi.GetBrush(TSuiteTokens.ColorAccentTRamCashDarkOnAccent), r, Gdi.Center);
        }
    }

    // Pencere ici sonuc bildirimi: alt orta, 150 ms kayarak belirir, 3 sn sonra kaybolur.
    void DrawToast(Graphics g)
    {
        long dt = _clock.ElapsedMilliseconds - _toastStart;
        float a;
        if (dt < ToastIn) a = dt / (float)ToastIn;
        else if (dt < ToastIn + ToastHold) a = 1f;
        else a = 1f - (dt - ToastIn - ToastHold) / (float)ToastOut;
        a = Math.Clamp(a, 0f, 1f);
        float ease = 1f - (1f - Math.Min(1f, dt / (float)ToastIn)) * (1f - Math.Min(1f, dt / (float)ToastIn));
        int dy = (int)Math.Round(S(8) * (1f - ease));
        int alpha = (int)(255 * a);

        var r = new Rectangle(_toastRect.X, _toastRect.Y + dy, _toastRect.Width, _toastRect.Height);
        if (r.Width <= 0 || r.Height <= 0) return;
        if (alpha >= 255 && dy == 0)
        {
            var path = Gdi.RoundedPath(r, S(8));
            g.FillPath(Gdi.GetBrush(CardBg), path);
            g.DrawPath(Gdi.GetPen(Accent), path);
            g.DrawString(_toastText, Gdi.GetFont("Segoe UI", 9F), Gdi.GetBrush(TextWhite), r, Gdi.Center);
        }
        else
        {
            using var path = CreateRoundedRectanglePath(r, S(8));
            using var bgBrush = new SolidBrush(Color.FromArgb(alpha, CardBg));
            using var borderPen = new Pen(Color.FromArgb(alpha, Accent));
            using var textBrush = new SolidBrush(Color.FromArgb(alpha, TextWhite));
            g.FillPath(bgBrush, path);
            g.DrawPath(borderPen, path);
            g.DrawString(_toastText, Gdi.GetFont("Segoe UI", 9F), textBrush, r, Gdi.Center);
        }
    }

    void FillCard(Graphics g, Rectangle r)
    {
        g.FillRectangle(Gdi.GetBrush(CardBg), r);
        g.DrawRectangle(Gdi.GetPen(CardBorder), r);
    }

    #endregion

    #region Fare

    int HitTest(Point p)
    {
        if (_rcClose.Contains(p)) return HClose;
        if (_rcMin.Contains(p)) return HMin;
        if (_rcSettings.Contains(p)) return HSettings;
        if (_rcAbout.Contains(p)) return HAbout;
        if (_L.ModeHelp.Contains(p)) return HModeHelp;
        if (_rcSmart.Contains(p)) return HSmart;
        if (_rcFull.Contains(p)) return HFull;
        if (_rcCleanBtn.Contains(p)) return HClean;
        if (!MemoryCleaner.IsAdministrator && _rcAdmin.Contains(p)) return HAdmin;
        return HNone;
    }

    Rectangle RectOf(int id) => id switch
    {
        HModeHelp => _L.ModeHelp, HClose => _rcClose, HMin => _rcMin, HSettings => _rcSettings, HAbout => _rcAbout,
        HSmart => _rcSmart, HFull => _rcFull, HClean => _rcCleanBtn, HAdmin => _rcAdmin, _ => Rectangle.Empty
    };

    static string NameOf(int id) => id switch
    {
        HClose => Strings.HideWindow, HMin => Strings.MinimizeTray, HSettings => Strings.Settings, HAbout => Strings.About,
        HModeHelp => Strings.ModeHelpName, HSmart => Strings.SmartMode, HFull => Strings.DeepMode, HClean => Strings.CleanMemory, HAdmin => Strings.RestartAdmin, _ => ""
    };

    void ActivateItem(int id)
    {
        switch (id)
        {
            case HClose: BeginInvoke(new Action(_app.HideMain)); return; // bu formu Dispose eder
            case HMin: BeginInvoke(new Action(_app.HideMain)); return;   // kucultunce tepsiye gizle
            case HSettings: _app.OpenSettings(this); return;
            case HAbout: _app.OpenAbout(this); return;
            case HModeHelp: ShowModeHelp(); return;
            case HSmart: SetMode(CleanMode.Smart); return;
            case HFull: SetMode(CleanMode.Full); return;
            case HClean: _app.RequestClean(_app.ManualMode, false); return;
            case HAdmin: _app.RestartAsAdmin(); return;
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        int id = HitTest(e.Location);
        if (id != HNone) { SetFocusItem(HNone); ActivateItem(id); return; }

        if (e.Y < S(46))
        {
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
        }
    }

    // ---- Klavye ----
    void SetFocusItem(int id)
    {
        if (_focus == id) return;
        var old = RectOf(_focus);
        _focus = id;
        if (!old.IsEmpty) Invalidate(Rectangle.Inflate(old, S(4), S(4)));
        var nr = RectOf(id);
        if (!nr.IsEmpty) { Invalidate(Rectangle.Inflate(nr, S(4), S(4))); ShowTip(id); }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Tab:
            case Keys.Tab | Keys.Shift:
            {
                int n = FocusOrder.Length, i = Array.IndexOf(FocusOrder, _focus);
                i = keyData == Keys.Tab ? (i + 1) % n : (i <= 0 ? n - 1 : i - 1);
                SetFocusItem(FocusOrder[i]);
                return true;
            }
            case Keys.Enter:
            case Keys.Space:
                if (_focus != HNone) { ActivateItem(_focus); return true; }
                break;
            case Keys.Escape:
                BeginInvoke(new Action(_app.HideMain));
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    void DrawFocus(Graphics g)
    {
        if (_focus == HNone) return;
        var r = Rectangle.Inflate(RectOf(_focus), S(2), S(2));
        g.DrawPath(Gdi.GetPen(Accent, 1.5f), Gdi.RoundedPath(r, S(8)));
    }

    void ShowModeHelp()
    {
        _tip?.Hide(this);
        if (_modeTip == null)
        {
            _modeTip = new ToolTip { OwnerDraw = true, InitialDelay = 0, ReshowDelay = 0 };
            _modeTip.Popup += (_, e) =>
            {
                var size = TextRenderer.MeasureText(ModeHelpText, Gdi.GetFont("Segoe UI", 9f),
                    new Size(S(300), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                e.ToolTipSize = new Size(size.Width + S(24), size.Height + S(24));
            };
            _modeTip.Draw += (_, e) =>
            {
                e.Graphics.FillRectangle(Gdi.GetBrush(Surface), e.Bounds);
                e.Graphics.DrawRectangle(Gdi.GetPen(Accent), 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                TextRenderer.DrawText(e.Graphics, ModeHelpText, Gdi.GetFont("Segoe UI", 9f),
                    Rectangle.Inflate(e.Bounds, -S(12), -S(12)), TextWhite,
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            };
        }
        _modeTip.Show(ModeHelpText, this, _L.ModeHelp.Right - S(324), _L.ModeHelp.Bottom + S(4), 15000);
    }

    void ShowTip(int id)
    {
        if (id == HModeHelp) { ShowModeHelp(); return; }
        _modeTip?.Hide(this);
        string t = NameOf(id);
        if (t.Length == 0) return;
        _tip ??= new ToolTip { InitialDelay = 0, ReshowDelay = 0 };
        var r = RectOf(id);
        _tip.Show(t, this, r.X, r.Bottom + S(4), 1500);
    }

    // ---- Erisilebilirlik ----
    protected override AccessibleObject CreateAccessibilityInstance() => new FormAcc(this);

    sealed class FormAcc(MainForm f) : Control.ControlAccessibleObject(f)
    {
        public override int GetChildCount() => FocusOrder.Length;
        public override AccessibleObject GetChild(int index)
            => index >= 0 && index < FocusOrder.Length ? new ItemAcc(f, this, FocusOrder[index]) : null;
    }

    sealed class ItemAcc(MainForm f, AccessibleObject parent, int id) : AccessibleObject
    {
        public override string Name { get => NameOf(id); set { } }
        public override AccessibleRole Role => AccessibleRole.PushButton;
        public override AccessibleObject Parent => parent;
        public override Rectangle Bounds => f.RectangleToScreen(f.RectOf(id));
        public override string DefaultAction => Strings.Activate;
        public override void DoDefaultAction() => f.ActivateItem(id);
    }

    void SetMode(CleanMode m)
    {
        if (_app.ManualMode == m) return;
        _app.ManualMode = m;
        EnsureAnim();
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = HitTest(e.Location);
        if (h != _hover)
        {
            var o = RectOf(_hover);
            _hover = h;
            Cursor = h != HNone ? Cursors.Hand : Cursors.Default;
            if (!o.IsEmpty) Invalidate(Rectangle.Inflate(o, S(4), S(4)));
            var n = RectOf(h);
            if (!n.IsEmpty) { Invalidate(Rectangle.Inflate(n, S(4), S(4))); ShowTip(h); }
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != HNone) { var o = RectOf(_hover); _hover = HNone; Invalidate(Rectangle.Inflate(o, S(4), S(4))); }
    }

    #endregion
}
