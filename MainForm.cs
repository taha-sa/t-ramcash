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

    // Palet: Derin karbon siyahi & mat amber altin (#D4A017)
    static readonly Color BgDark = Color.FromArgb(14, 14, 18);
    static readonly Color CardBg = Color.FromArgb(20, 20, 26);
    static readonly Color CardBorder = Color.FromArgb(34, 34, 44);
    static readonly Color DarkGold = Color.FromArgb(212, 160, 23);
    static readonly Color DarkGoldHover = Color.FromArgb(228, 176, 36);
    static readonly Color TextWhite = Color.FromArgb(240, 240, 245);
    static readonly Color TextMuted = Color.FromArgb(145, 145, 158);
    static readonly Color TextDim = Color.FromArgb(92, 92, 104);
    static readonly Color Surface = Color.FromArgb(28, 28, 36);

    const int HNone = 0, HClose = 1, HMin = 2, HSettings = 3, HAbout = 4, HClean = 5, HSmart = 6, HFull = 7, HAdmin = 8;
    // Tab sirasi
    static readonly int[] FocusOrder = { HAbout, HSettings, HMin, HClose, HSmart, HFull, HClean };
    int _focus = HNone;
    ToolTip _tip;

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
        using var ptr = new RegionHandle(CreateRoundRectRgn(0, 0, Width + 1, Height + 1, rr, rr));
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _app.CleaningChanged -= OnCleaningChanged;
            _app.CleanCompleted -= OnCleanCompleted;
            _refresh.Dispose();
            _anim.Dispose();
            _logo.Dispose();
            _tip?.Dispose();
        }
        base.Dispose(disposing);
        if (disposing)
        {
            _icon?.Dispose();
            Gdi.Release();
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
        _sPct = "%" + s.MemoryLoadPercent;
        _sUsed = $"{s.UsedPhysicalBytes / GB:F1} GB  /  {s.TotalPhysicalBytes / GB:F1} GB";
        _sFree = $"Kullanılabilir: {s.AvailablePhysicalBytes / GB:F1} GB";
        _sPagePct = "%" + s.PageFileLoadPercent;
        _sPage = $"{s.UsedPageFileBytes / GB:F1} GB  /  {s.TotalPageFileBytes / GB:F1} GB";
        _sCache = $"Sistem Cache: {s.SystemCacheBytes / (1024.0 * 1024.0):F0} MB";
        _sInfo = $"● {status}  ·  {s.ProcessCount} Aktif Süreç";
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
        string freed = mb < 1 ? "Boşaltılacak bellek bulunamadı"
            : mb >= 1024 ? $"+{mb / 1024.0:F1} GB boşaltıldı" : $"+{mb:F0} MB boşaltıldı";
        _toastText = r.Note != null
            ? $"{freed} · {r.Note}"
            : $"{freed} · {(r.Mode == CleanMode.Full ? "Tam" : "Akıllı")} · {r.DurationMs} ms";
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
        DrawColumn(g, _L.Card1, "FİZİKSEL BELLEK", _sPct, _sUsed, _app.Snap.MemoryLoadPercent, _sFree);
        DrawColumn(g, _L.Card2, "SANAL & ÖNBELLEK", _sPagePct, _sPage, _app.Snap.PageFileLoadPercent, _sCache);
        DrawColumnAction(g, _L.Card3);
        if (_toastStart >= 0) DrawToast(g);
        DrawFocus(g);
    }

    void DrawHorizontalHeader(Graphics g)
    {
        g.DrawImageUnscaled(_logo, S(20), S(12));
        g.DrawString("T-RAMCASH", Gdi.GetFont("Segoe UI", 10.5F, FontStyle.Bold), Gdi.GetBrush(DarkGold), S(48), S(12));
        DrawFit(g, _sInfo, 8.5F, FontStyle.Regular, TextDim, _L.InfoText, Gdi.VCenterLeft);

        DrawNormalBtn(g, _rcAbout, HAbout, _hover == HAbout);
        DrawNormalBtn(g, _rcSettings, HSettings, _hover == HSettings);
        DrawNormalBtn(g, _rcMin, HMin, _hover == HMin);
        DrawNormalBtn(g, _rcClose, HClose, _hover == HClose);

        g.DrawLine(Gdi.GetPen(CardBorder), S(20), S(44), Width - S(20), S(44));
    }

    void DrawCustomLogo(Graphics g, int x, int y, int s)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var chipRect = new RectangleF(x, y, s, s);
        float radius = s * 0.22f;
        using var path = CreateRoundedRectanglePath(chipRect, radius);

        using (var brBody = new LinearGradientBrush(chipRect, Color.FromArgb(28, 28, 36), Color.FromArgb(12, 12, 16), 90f))
            g.FillPath(brBody, path);
        using (var penRim = new Pen(Color.FromArgb(60, 60, 80), 1f))
            g.DrawPath(penRim, path);

        if (s >= 18)
        {
            var inRect = new RectangleF(x + 1.5f, y + 1.5f, s - 3f, s - 3f);
            using var inPath = CreateRoundedRectanglePath(inRect, radius * 0.7f);
            using var penInGold = new Pen(Color.FromArgb(90, 212, 160, 23), 1f);
            g.DrawPath(penInGold, inPath);
        }

        float tW = s * 0.60f, tH = s * 0.54f;
        float tX = x + (s - tW) / 2f, tY = y + (s - tH) / 2f;
        float barH = Math.Max(2f, s * 0.16f), stemW = Math.Max(2f, s * 0.18f);
        float chamfer = Math.Max(1f, s * 0.04f);
        float stemLeft = tX + (tW - stemW) / 2f, stemRight = tX + (tW + stemW) / 2f;
        float pointH = Math.Max(1.5f, s * 0.05f);

        var pts = new PointF[]
        {
            new(tX + chamfer, tY), new(tX + tW - chamfer, tY), new(tX + tW, tY + chamfer), new(tX + tW, tY + barH),
            new(stemRight, tY + barH), new(stemRight, tY + tH - pointH), new(x + (s * 0.5f), tY + tH),
            new(stemLeft, tY + tH - pointH), new(stemLeft, tY + barH), new(tX, tY + barH), new(tX, tY + chamfer)
        };

        using var tPath = new GraphicsPath();
        tPath.AddPolygon(pts);
        using (var brGold = new LinearGradientBrush(new RectangleF(tX, tY, tW, tH),
                   Color.FromArgb(245, 196, 52), Color.FromArgb(160, 110, 15), 65f))
            g.FillPath(brGold, tPath);
        if (s >= 18)
        {
            using var penTBorder = new Pen(Color.FromArgb(200, 255, 245, 180), 0.75f);
            g.DrawPath(penTBorder, tPath);
        }
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
        var col = hover ? DarkGold : TextMuted;
        float lw = Math.Max(1.25f, S(2) * 0.7f);
        var pen = Gdi.GetPen(col, lw);
        pen.StartCap = pen.EndCap = LineCap.Round;
        pen.LineJoin = LineJoin.Round;
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
        g.FillRectangle(Gdi.GetBrush(Color.FromArgb(30, 30, 38)), x + pad, barY, barW, barH);
        int fillW = (int)((barW * Math.Min(100u, load)) / 100.0);
        if (fillW > 0) g.FillRectangle(Gdi.GetBrush(DarkGold), x + pad, barY, fillW, barH);

        DrawFit(g, foot, 8.5F, FontStyle.Regular, TextDim, new RectangleF(x + pad, y + S(140), inner, S(22)), Gdi.TopLeft);
    }

    void DrawColumnAction(Graphics g, Rectangle card)
    {
        int x = card.X, y = card.Y, w = card.Width;
        int pad = S(16);
        float inner = w - 2 * pad;
        FillCard(g, card);
        bool admin = MemoryCleaner.IsAdministrator;

        DrawFit(g, admin ? "● YÖNETİCİ MODU" : "○ STANDART MOD", 8F, FontStyle.Bold, admin ? DarkGold : TextMuted,
            new RectangleF(x + pad, y + S(16), inner, S(18)), Gdi.TopLeft);
        DrawFit(g, admin ? "Tam önbellek yetkili" : "Yönetici ile tam yetki", 7.5F, FontStyle.Regular, TextDim,
            new RectangleF(x + pad, y + S(34), inner, S(14)), Gdi.TopLeft);

        if (!admin)
            DrawFit(g, "▸ Yönetici olarak yeniden başlat", 7F, FontStyle.Underline, _hover == HAdmin ? TextWhite : DarkGold,
                new RectangleF(x + pad, y + S(48), inner, S(16)), Gdi.TopLeft);
        DrawToggle(g);
        DrawCleanButton(g);

        DrawFit(g, _app.HotkeyOk ? "Kısayol: Ctrl + F1" : "Kısayol kullanılamıyor (başka uygulama kullanıyor)", 8F, FontStyle.Regular, TextDim,
            new RectangleF(x, y + S(148), w, S(32)), Gdi.HCenter);
    }

    // Segmentli anahtar: [ Akıllı | Tam ]
    void DrawToggle(Graphics g)
    {
        g.FillPath(Gdi.GetBrush(BgDark), Gdi.RoundedPath(_rcToggle, S(8)));
        g.DrawPath(Gdi.GetPen(CardBorder), Gdi.RoundedPath(_rcToggle, S(8)));
        DrawSegment(g, _rcSmart, "Akıllı", _seg[0], _hover == HSmart);
        DrawSegment(g, _rcFull, "Tam", _seg[1], _hover == HFull);
    }

    void DrawSegment(Graphics g, Rectangle r, string text, float t, bool hover)
    {
        var inner = Rectangle.Inflate(r, -S(3), -S(3));
        if (t > 0.01f)
            g.FillPath(Gdi.GetBrush(Color.FromArgb((int)(255 * t), DarkGold)), Gdi.RoundedPath(inner, S(6)));
        Color unselected = hover ? TextWhite : TextMuted;
        g.DrawString(text, Gdi.GetFont("Segoe UI", 8.5F, FontStyle.Bold), Gdi.GetBrush(Gdi.Lerp(unselected, BgDark, t)), r, Gdi.Center);
    }

    void DrawCleanButton(Graphics g)
    {
        var r = _rcCleanBtn;
        var path = Gdi.RoundedPath(r, S(8));
        if (_app.IsCleaning)
        {
            g.FillPath(Gdi.GetBrush(Surface), path);
            g.DrawPath(Gdi.GetPen(CardBorder), path);
            g.DrawString("Temizleniyor…", Gdi.GetFont("Segoe UI", 9.5F, FontStyle.Bold), Gdi.GetBrush(DarkGold), r, Gdi.Center);

            // Animasyonlu altin alt cizgi: etiketin altinda soldan saga kayan segment
            float phase = (_clock.ElapsedMilliseconds % 1100) / 1100f;
            int span = r.Width - S(24), segW = span / 3;
            int sx = r.X + S(12) + (int)((span + segW) * phase) - segW;
            int x0 = Math.Max(sx, r.X + S(12)), x1 = Math.Min(sx + segW, r.X + S(12) + span);
            float lw = Math.Max(2f, S(2));
            g.DrawLine(Gdi.GetPen(CardBorder, lw), r.X + S(12), r.Bottom - S(10), r.X + S(12) + span, r.Bottom - S(10));
            if (x1 > x0) g.DrawLine(Gdi.GetPen(DarkGold, lw), x0, r.Bottom - S(10), x1, r.Bottom - S(10));
        }
        else
        {
            g.FillPath(Gdi.GetBrush(_hover == HClean ? DarkGoldHover : DarkGold), path);
            g.DrawString("BELLEĞİ TEMİZLE", Gdi.GetFont("Segoe UI", 9.5F, FontStyle.Bold), Gdi.GetBrush(Color.Black), r, Gdi.Center);
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
        var path = Gdi.RoundedPath(r, S(8));
        g.FillPath(Gdi.GetBrush(Color.FromArgb(alpha, CardBg)), path);
        g.DrawPath(Gdi.GetPen(Color.FromArgb(alpha, DarkGold)), path);
        g.DrawString(_toastText, Gdi.GetFont("Segoe UI", 9F), Gdi.GetBrush(Color.FromArgb(alpha, TextWhite)), r, Gdi.Center);
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
        if (_rcSmart.Contains(p)) return HSmart;
        if (_rcFull.Contains(p)) return HFull;
        if (_rcCleanBtn.Contains(p)) return HClean;
        if (!MemoryCleaner.IsAdministrator && _rcAdmin.Contains(p)) return HAdmin;
        return HNone;
    }

    Rectangle RectOf(int id) => id switch
    {
        HClose => _rcClose, HMin => _rcMin, HSettings => _rcSettings, HAbout => _rcAbout,
        HSmart => _rcSmart, HFull => _rcFull, HClean => _rcCleanBtn, HAdmin => _rcAdmin, _ => Rectangle.Empty
    };

    static string NameOf(int id) => id switch
    {
        HClose => "Pencereyi gizle", HMin => "Tepsiye küçült", HSettings => "Ayarlar", HAbout => "Hakkında",
        HSmart => "Akıllı mod", HFull => "Tam mod", HClean => "Belleği temizle", HAdmin => "Yönetici olarak yeniden başlat", _ => ""
    };

    void ActivateItem(int id)
    {
        switch (id)
        {
            case HClose: BeginInvoke(new Action(_app.HideMain)); return; // bu formu Dispose eder
            case HMin: BeginInvoke(new Action(_app.HideMain)); return;   // kucultunce tepsiye gizle
            case HSettings: _app.OpenSettings(this); return;
            case HAbout: _app.OpenAbout(this); return;
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
        g.DrawPath(Gdi.GetPen(DarkGold, 1.5f), Gdi.RoundedPath(r, S(8)));
    }

    void ShowTip(int id)
    {
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
        public override string DefaultAction => "Etkinleştir";
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
