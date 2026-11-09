using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TRamCash;

public class MainForm : Form
{
    #region Win32 P/Invoke & Window FX

    [DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
    static extern IntPtr CreateRoundRectRgn(
        int nLeftRect, int nTopRect, int nRightRect, int nBottomRect,
        int nWidthEllipse, int nHeightEllipse);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

    [DllImport("user32.dll")]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern bool DestroyIcon(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, IntPtr lParam);

    const int WM_SETICON = 0x0080;
    const int ICON_SMALL = 0;
    const int ICON_BIG = 1;

    const int WM_NCLBUTTONDOWN = 0xA1;
    const int HT_CAPTION = 0x2;
    const int HOTKEY_ID = 9000;
    const uint MOD_CONTROL = 0x0002;
    const uint VK_F1 = 0x70;
    const int WM_HOTKEY = 0x0312;

    protected override CreateParams CreateParams
    {
        get
        {
            // Görev çubuğunda adın net gözükmesi ve tıklayınca küçülüp büyümesi için WS_MINIMIZEBOX ve WS_SYSMENU
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

    // Renk Paleti: Derin Mat Karbon Siyahı & Kapalı Mat Amber Altın (#D4A017)
    static readonly Color BgDark = Color.FromArgb(14, 14, 18);          // Derin Siyah Gövde
    static readonly Color CardBg = Color.FromArgb(20, 20, 26);          // Ferah Kart Arka Planı
    static readonly Color CardBorder = Color.FromArgb(34, 34, 44);      // İnce Ayrım Çizgisi
    static readonly Color DarkGold = Color.FromArgb(212, 160, 23);      // Kapalı / Mat Amber Sarı (#D4A017)
    static readonly Color DarkGoldHover = Color.FromArgb(228, 176, 36); // Hover Rengi
    static readonly Color TextWhite = Color.FromArgb(240, 240, 245);    // Temiz Beyaz
    static readonly Color TextMuted = Color.FromArgb(145, 145, 158);    // Soluk Gri
    static readonly Color TextDim = Color.FromArgb(92, 92, 104);        // Koyu Gri

    readonly AppSettings _settings;
    NotifyIcon _trayIcon;
    Icon _currentTrayIcon;
    ContextMenuStrip _trayMenu;
    ToolStripMenuItem _menuHeaderItem;
    System.Windows.Forms.Timer _refreshTimer;
    System.Windows.Forms.Timer _autoCleanTimer;
    DateTime _lastCleanTime = DateTime.MinValue;

    MemoryCleaner.MemorySnapshot _currentSnap;
    string _statusText = "Sistem izleniyor • Hazır";
    bool _isCleaning = false;

    // Normal Minimalist Pencere Kontrolleri (Sağ üstte)
    Rectangle _rcClose;
    Rectangle _rcMin;
    Rectangle _rcSettings;
    Rectangle _rcAbout;
    bool _hoverClose, _hoverMin, _hoverSettings, _hoverAbout;

    // Yatay Temizleme Butonu
    Rectangle _rcCleanBtn;
    bool _hoverClean = false;

    public MainForm()
    {
        _settings = AppSettings.Load();

        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;

        // BÜYÜTÜLMÜŞ FERAH YATAY BOYUT: 640 genişlik, 265 yükseklik
        Size = new Size(640, 265);
        BackColor = BgDark;
        TopMost = _settings.AlwaysOnTop;

        // Görev çubuğunda uygulamanın adı ve başlığı açıkça gözüksün
        Text = "T-RamCash";
        ShowInTaskbar = true;

        // Uygulama simgesini (hem görev yöneticisinde hem formda) ayarla
        try
        {
            string icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (System.IO.File.Exists(icoPath))
            {
                Icon = new Icon(icoPath);
            }
            else if (!string.IsNullOrEmpty(Environment.ProcessPath))
            {
                Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath);
            }

            if (Icon != null)
            {
                SendMessage(Handle, WM_SETICON, ICON_SMALL, Icon.Handle);
                SendMessage(Handle, WM_SETICON, ICON_BIG, Icon.Handle);
            }
        }
        catch { }

        // Kontrol kutularının konumlarını dinamik ayarla
        _rcClose = new Rectangle(Width - 36, 12, 24, 22);
        _rcMin = new Rectangle(Width - 66, 12, 24, 22);
        _rcSettings = new Rectangle(Width - 96, 12, 24, 22);
        _rcAbout = new Rectangle(Width - 126, 12, 24, 22);

        // Temizle Butonu (Sağ kolonda geniş ve ferah)
        _rcCleanBtn = new Rectangle(430, 128, 190, 52);

        ApplyRoundedRegion();
        SetupTray();

        // Kısayol (Ctrl + F1)
        RegisterHotKey(this.Handle, HOTKEY_ID, MOD_CONTROL, VK_F1);

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _refreshTimer.Tick += (s, e) => { RefreshData(); Invalidate(); };
        _refreshTimer.Start();

        _autoCleanTimer = new System.Windows.Forms.Timer { Interval = 4000 };
        _autoCleanTimer.Tick += (s, e) => CheckAutoClean();
        _autoCleanTimer.Start();

        RefreshData();
    }

    void ApplyRoundedRegion()
    {
        IntPtr ptr = CreateRoundRectRgn(0, 0, Width, Height, 16, 16);
        Region = Region.FromHrgn(ptr);
        DeleteObject(ptr);
    }

    void RefreshData()
    {
        _currentSnap = MemoryCleaner.GetSnapshot();
        UpdateTrayIcon(_currentSnap.MemoryLoadPercent);

        if (_menuHeaderItem != null)
        {
            double usedGb = _currentSnap.UsedPhysicalBytes / (1024.0 * 1024.0 * 1024.0);
            double totalGb = _currentSnap.TotalPhysicalBytes / (1024.0 * 1024.0 * 1024.0);
            string digits = _currentSnap.MemoryLoadPercent > 9 ? _currentSnap.MemoryLoadPercent.ToString() : $"0{_currentSnap.MemoryLoadPercent}";
            _menuHeaderItem.Text = $"T-RamCash: %{digits} RAM ({usedGb:F1} GB / {totalGb:F1} GB)";
        }
    }

    void SetupTray()
    {
        _trayMenu = new ContextMenuStrip();
        
        // Küçük menünün en başında iki basamaklı RAM göstergesi
        _menuHeaderItem = new ToolStripMenuItem("T-RamCash: %00 RAM")
        {
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.DarkGoldenrod
        };
        _trayMenu.Items.Add(_menuHeaderItem);
        _trayMenu.Items.Add(new ToolStripSeparator());

        _trayMenu.Items.Add("Pencereyi Göster / Gizle", null, (s, e) => ToggleVisibility());
        _trayMenu.Items.Add("Belleği Hızlı Temizle", null, (s, e) => TriggerClean());
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add("Görev Yöneticisi", null, (s, e) => OpenTaskManager());
        _trayMenu.Items.Add("Ayarlar...", null, (s, e) => OpenSettings());
        _trayMenu.Items.Add("Hakkında...", null, (s, e) => OpenAbout());
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add("Çıkış", null, (s, e) => FullExit());

        _trayIcon = new NotifyIcon
        {
            Text = "T-RamCash",
            ContextMenuStrip = _trayMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += (s, e) => ToggleVisibility();

        if (_settings.StartMinimized)
        {
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;
        }
    }

    void UpdateTrayIcon(uint percent)
    {
        try
        {
            // 32x32 yüksek DPI tuval üzerine çizim yapıyoruz (kesilme olmaksızın 2 basamak net sığar)
            using var bmp = new Bitmap(32, 32);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Mat siyah arka plan
            using (var b = new SolidBrush(Color.FromArgb(14, 14, 18)))
                g.FillRectangle(b, 0, 0, 32, 32);

            // İnce altın sarı çerçeve
            using (var p = new Pen(DarkGold, 1.5f))
                g.DrawRectangle(p, 1, 1, 29, 29);

            // İKİ BASAMAKLI SAYI GARANTİSİ (9'dan büyükse tam 2 basamak, örn: 44, 78)
            string text = percent > 9 ? percent.ToString() : percent.ToString();

            // 32x32 piksel içinde iki basamaklı sayı için kusursuz ölçek (15.5f Bold)
            using var f = new Font("Segoe UI", 15.5f, FontStyle.Bold);
            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString(text, f, Brushes.White, new RectangleF(0, 0, 32, 32), sf);

            IntPtr hIcon = bmp.GetHicon();
            using var ico = Icon.FromHandle(hIcon);
            var newIcon = (Icon)ico.Clone();
            DestroyIcon(hIcon);

            var oldIcon = _currentTrayIcon;
            _trayIcon.Icon = newIcon;
            _currentTrayIcon = newIcon;
            oldIcon?.Dispose();

            _trayIcon.Text = $"T-RamCash - %{text} RAM";
        }
        catch { }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // Dış çerçeve çizgisi
        using (var penBorder = new Pen(CardBorder, 1))
            g.DrawRectangle(penBorder, 0, 0, Width - 1, Height - 1);

        // 1. Üst Başlık & Normal Butonlar (Genişletilmiş Başlık Çubuğu)
        DrawHorizontalHeader(g);

        // 2. Kolon 1 (Sol): Fiziksel Bellek (Genişletilmiş ve Ferah)
        DrawColumnPhysical(g, 20, 56, 195, 185);

        // 3. Kolon 2 (Orta): Sanal Bellek & Cache
        DrawColumnVirtual(g, 225, 56, 195, 185);

        // 4. Kolon 3 (Sağ): Aksiyon, Buton & Çekirdek Durumu
        DrawColumnAction(g, 430, 56, 190, 185);
    }

    void DrawHorizontalHeader(Graphics g)
    {
        // Özel Tasarım Logo (Yüksek Çözünürlüklü Altın Çip Monogramı)
        DrawCustomLogo(g, 20, 12, 20);

        // Görev Çubuğunda ve Başlıkta İsim: T-RAMCASH
        using (var fTitle = new Font("Segoe UI", 10.5F, FontStyle.Bold))
        using (var bGold = new SolidBrush(DarkGold))
            g.DrawString("T-RAMCASH", fTitle, bGold, 48, 12);

        // Ortada Durum Rozeti
        using (var fStatus = new Font("Segoe UI", 8.5F))
        using (var bDim = new SolidBrush(TextDim))
        {
            string info = $"● {_statusText}  ·  {_currentSnap.ProcessCount} Aktif Süreç";
            g.DrawString(info, fStatus, bDim, 175, 14);
        }

        // Normal Minimalist Butonlar [ℹ] [⚙] [—] [✕]
        DrawNormalBtn(g, _rcAbout, "ℹ", _hoverAbout);
        DrawNormalBtn(g, _rcSettings, "⚙", _hoverSettings);
        DrawNormalBtn(g, _rcMin, "—", _hoverMin);
        DrawNormalBtn(g, _rcClose, "✕", _hoverClose);

        // İnce alt ayırıcı çizgi
        using var penLine = new Pen(CardBorder, 1);
        g.DrawLine(penLine, 20, 44, Width - 20, 44);
    }

    void DrawCustomLogo(Graphics g, int x, int y, int s)
    {
        var prevSmoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Dış yuvarlatılmış gövde (Squircle)
        var chipRect = new RectangleF(x, y, s, s);
        float radius = s * 0.22f;
        using var path = CreateRoundedRectanglePath(chipRect, radius);

        // Çip gövdesi (Mat Obsidian Titanyum)
        using (var brBody = new LinearGradientBrush(chipRect, Color.FromArgb(28, 28, 36), Color.FromArgb(12, 12, 16), 90f))
            g.FillPath(brBody, path);

        using (var penRim = new Pen(Color.FromArgb(60, 60, 80), 1f))
            g.DrawPath(penRim, path);

        // İç altın hat
        if (s >= 18)
        {
            var inRect = new RectangleF(x + 1.5f, y + 1.5f, s - 3f, s - 3f);
            using var inPath = CreateRoundedRectanglePath(inRect, radius * 0.7f);
            using var penInGold = new Pen(Color.FromArgb(90, 212, 160, 23), 1f);
            g.DrawPath(penInGold, inPath);
        }

        // T Harfi (Geometrik Keskin Altın Monogram)
        float tW = s * 0.60f;
        float tH = s * 0.54f;
        float tX = x + (s - tW) / 2f;
        float tY = y + (s - tH) / 2f;

        float barH = Math.Max(2f, s * 0.16f);
        float stemW = Math.Max(2f, s * 0.18f);
        float chamfer = Math.Max(1f, s * 0.04f);
        float stemLeft = tX + (tW - stemW) / 2f;
        float stemRight = tX + (tW + stemW) / 2f;
        float pointH = Math.Max(1.5f, s * 0.05f);

        var pts = new PointF[]
        {
            new PointF(tX + chamfer, tY),
            new PointF(tX + tW - chamfer, tY),
            new PointF(tX + tW, tY + chamfer),
            new PointF(tX + tW, tY + barH),
            new PointF(stemRight, tY + barH),
            new PointF(stemRight, tY + tH - pointH),
            new PointF(x + (s * 0.5f), tY + tH),
            new PointF(stemLeft, tY + tH - pointH),
            new PointF(stemLeft, tY + barH),
            new PointF(tX, tY + barH),
            new PointF(tX, tY + chamfer)
        };

        using (var tPath = new GraphicsPath())
        {
            tPath.AddPolygon(pts);

            using (var brGold = new LinearGradientBrush(
                new RectangleF(tX, tY, tW, tH),
                Color.FromArgb(245, 196, 52),
                Color.FromArgb(160, 110, 15),
                65f))
            {
                g.FillPath(brGold, tPath);
            }

            if (s >= 18)
            {
                using var penTBorder = new Pen(Color.FromArgb(200, 255, 245, 180), 0.75f);
                g.DrawPath(penTBorder, tPath);
            }
        }

        g.SmoothingMode = prevSmoothing;
    }

    static GraphicsPath CreateRoundedRectanglePath(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float diameter = radius * 2f;
        var arc = new RectangleF(rect.X, rect.Y, diameter, diameter);

        path.AddArc(arc, 180, 90);
        arc.X = rect.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = rect.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = rect.Left;
        path.AddArc(arc, 90, 90);

        path.CloseFigure();
        return path;
    }

    void DrawNormalBtn(Graphics g, Rectangle rc, string text, bool hover)
    {
        if (hover)
        {
            using var bHover = new SolidBrush(Color.FromArgb(28, 28, 36));
            g.FillRectangle(bHover, rc);
        }

        using var font = new Font("Segoe UI", 9F, FontStyle.Regular);
        using var brush = new SolidBrush(hover ? DarkGold : TextMuted);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, brush, rc, sf);
    }

    void DrawColumnPhysical(Graphics g, int x, int y, int w, int h)
    {
        var rc = new Rectangle(x, y, w, h);
        FillCard(g, rc);

        double totalGb = _currentSnap.TotalPhysicalBytes / (1024.0 * 1024.0 * 1024.0);
        double usedGb = _currentSnap.UsedPhysicalBytes / (1024.0 * 1024.0 * 1024.0);
        double freeGb = _currentSnap.AvailablePhysicalBytes / (1024.0 * 1024.0 * 1024.0);

        // Başlık
        using (var fTitle = new Font("Segoe UI", 8F, FontStyle.Bold))
        using (var bMuted = new SolidBrush(TextMuted))
            g.DrawString("FİZİKSEL BELLEK", fTitle, bMuted, x + 16, y + 16);

        // İki Basamaklı Ölçülü Minimal Yüzde
        string pctStr = _currentSnap.MemoryLoadPercent > 9 ? $"%{_currentSnap.MemoryLoadPercent}" : $"%{_currentSnap.MemoryLoadPercent}";
        using (var fPercent = new Font("Segoe UI", 24F, FontStyle.Bold))
        using (var bWhite = new SolidBrush(TextWhite))
            g.DrawString(pctStr, fPercent, bWhite, x + 14, y + 36);

        // Aktif / Toplam
        using (var fSub = new Font("Segoe UI", 8.5F))
        using (var bDim = new SolidBrush(TextMuted))
            g.DrawString($"{usedGb:F1} GB  /  {totalGb:F1} GB", fSub, bDim, x + 16, y + 88);

        // İnce 4px Kapalı Altın Çizgi
        int barW = w - 32;
        int barY = y + 114;
        using (var bTrack = new SolidBrush(Color.FromArgb(30, 30, 38)))
            g.FillRectangle(bTrack, x + 16, barY, barW, 4);

        int fillW = (int)((barW * Math.Min(100, _currentSnap.MemoryLoadPercent)) / 100.0);
        if (fillW > 0)
        {
            using var bFill = new SolidBrush(DarkGold);
            g.FillRectangle(bFill, x + 16, barY, fillW, 4);
        }

        // Boşta Bilgisi
        using (var fFree = new Font("Segoe UI", 8.5F))
        using (var bFree = new SolidBrush(TextDim))
            g.DrawString($"Kullanılabilir: {freeGb:F1} GB", fFree, bFree, x + 16, y + 140);
    }

    void DrawColumnVirtual(Graphics g, int x, int y, int w, int h)
    {
        var rc = new Rectangle(x, y, w, h);
        FillCard(g, rc);

        double pageUsedGb = _currentSnap.UsedPageFileBytes / (1024.0 * 1024.0 * 1024.0);
        double pageTotalGb = _currentSnap.TotalPageFileBytes / (1024.0 * 1024.0 * 1024.0);
        double cacheMb = _currentSnap.SystemCacheBytes / (1024.0 * 1024.0);

        // Başlık
        using (var fTitle = new Font("Segoe UI", 8F, FontStyle.Bold))
        using (var bMuted = new SolidBrush(TextMuted))
            g.DrawString("SANAL & ÖNBELLEK", fTitle, bMuted, x + 16, y + 16);

        // İki Basamaklı Ölçülü Yüzde
        string pctStr = _currentSnap.PageFileLoadPercent > 9 ? $"%{_currentSnap.PageFileLoadPercent}" : $"%{_currentSnap.PageFileLoadPercent}";
        using (var fPercent = new Font("Segoe UI", 24F, FontStyle.Bold))
        using (var bWhite = new SolidBrush(TextWhite))
            g.DrawString(pctStr, fPercent, bWhite, x + 14, y + 36);

        // Sayfa Dosyası
        using (var fSub = new Font("Segoe UI", 8.5F))
        using (var bDim = new SolidBrush(TextMuted))
            g.DrawString($"{pageUsedGb:F1} GB  /  {pageTotalGb:F1} GB", fSub, bDim, x + 16, y + 88);

        // İnce 4px Kapalı Altın Çizgi
        int barW = w - 32;
        int barY = y + 114;
        using (var bTrack = new SolidBrush(Color.FromArgb(30, 30, 38)))
            g.FillRectangle(bTrack, x + 16, barY, barW, 4);

        int fillW = (int)((barW * Math.Min(100, _currentSnap.PageFileLoadPercent)) / 100.0);
        if (fillW > 0)
        {
            using var bFill = new SolidBrush(DarkGold);
            g.FillRectangle(bFill, x + 16, barY, fillW, 4);
        }

        // Cache Bilgisi
        using (var fCache = new Font("Segoe UI", 8.5F))
        using (var bCache = new SolidBrush(TextDim))
            g.DrawString($"Sistem Cache: {cacheMb:F0} MB", fCache, bCache, x + 16, y + 140);
    }

    void DrawColumnAction(Graphics g, int x, int y, int w, int h)
    {
        var rc = new Rectangle(x, y, w, h);
        FillCard(g, rc);

        // Çekirdek Rozeti (Üstte Ferah)
        using (var fBadge = new Font("Segoe UI", 8F, FontStyle.Bold))
        using (var bBadge = new SolidBrush(MemoryCleaner.IsAdministrator ? DarkGold : TextMuted))
        {
            string role = MemoryCleaner.IsAdministrator ? "● YÖNETİCİ MODU" : "○ STANDART MOD";
            g.DrawString(role, fBadge, bBadge, x + 16, y + 18);
        }

        using (var fNote = new Font("Segoe UI", 7.5F))
        using (var bNote = new SolidBrush(TextDim))
        {
            string note = MemoryCleaner.IsAdministrator ? "Tam önbellek yetkili" : "Yönetici ile tam yetki";
            g.DrawString(note, fNote, bNote, x + 16, y + 38);
        }

        // Kapalı Altın Sarı Minimalist Buton
        Color btnBg = _isCleaning ? Color.FromArgb(170, 130, 20)
                    : _hoverClean ? DarkGoldHover
                    : DarkGold;

        using (var bBtn = new SolidBrush(btnBg))
            g.FillRectangle(bBtn, _rcCleanBtn);

        string label = _isCleaning ? "TEMİZLENİYOR..." : "BELLEĞİ TEMİZLE";
        using var fontBtn = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        using var brushBtn = new SolidBrush(Color.Black);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        g.DrawString(label, fontBtn, brushBtn, _rcCleanBtn, sf);

        // Kısayol İpucu
        using var fKey = new Font("Segoe UI", 8F);
        using var bKey = new SolidBrush(TextDim);
        using var sfKey = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString("Kısayol: Ctrl + F1", fKey, bKey, new RectangleF(x, y + 142, w, 18), sfKey);
    }

    void FillCard(Graphics g, Rectangle r)
    {
        using (var b = new SolidBrush(CardBg))
            g.FillRectangle(b, r);

        using (var p = new Pen(CardBorder, 1))
            g.DrawRectangle(p, r);
    }

    #region Mouse Etkileşimleri & Sürükleme

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (_rcClose.Contains(e.Location))
        {
            Hide();
            return;
        }

        if (_rcMin.Contains(e.Location))
        {
            WindowState = FormWindowState.Minimized;
            return;
        }

        if (_rcSettings.Contains(e.Location))
        {
            OpenSettings();
            return;
        }

        if (_rcAbout.Contains(e.Location))
        {
            OpenAbout();
            return;
        }

        if (_rcCleanBtn.Contains(e.Location))
        {
            TriggerClean();
            return;
        }

        // Üst çubuktan pencereyi sürükleme
        if (e.Button == MouseButtons.Left && e.Y < 46)
        {
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        bool newClose = _rcClose.Contains(e.Location);
        bool newMin = _rcMin.Contains(e.Location);
        bool newSettings = _rcSettings.Contains(e.Location);
        bool newAbout = _rcAbout.Contains(e.Location);
        bool newClean = _rcCleanBtn.Contains(e.Location);

        if (newClose != _hoverClose || newMin != _hoverMin || newSettings != _hoverSettings || newAbout != _hoverAbout || newClean != _hoverClean)
        {
            _hoverClose = newClose;
            _hoverMin = newMin;
            _hoverSettings = newSettings;
            _hoverAbout = newAbout;
            _hoverClean = newClean;
            Cursor = (newClose || newMin || newSettings || newAbout || newClean) ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverClose = false;
        _hoverMin = false;
        _hoverSettings = false;
        _hoverAbout = false;
        _hoverClean = false;
        Invalidate();
    }

    #endregion

    void TriggerClean()
    {
        if (_isCleaning) return;
        _isCleaning = true;
        _statusText = "Temizleniyor...";
        Invalidate();

        System.Threading.Tasks.Task.Run(() =>
        {
            double freedMb = MemoryCleaner.Clean(_settings);
            _lastCleanTime = DateTime.Now;

            Invoke(new Action(() =>
            {
                _isCleaning = false;
                _statusText = $"{freedMb:F1} MB serbest";
                RefreshData();
                Invalidate();

                if (_settings.ShowCleanNotification)
                {
                    _trayIcon.ShowBalloonTip(3000, "T-RamCash", $"Bellek temizlendi!\n{freedMb:F1} MB RAM serbest bırakıldı.", ToolTipIcon.Info);
                }
            }));
        });
    }

    void CheckAutoClean()
    {
        if (_settings.AutoCleanByRatio)
        {
            if (_currentSnap.MemoryLoadPercent >= _settings.AutoCleanRatioThreshold)
            {
                if ((DateTime.Now - _lastCleanTime).TotalSeconds > 60)
                {
                    TriggerClean();
                    return;
                }
            }
        }

        if (_settings.AutoCleanByInterval)
        {
            if ((DateTime.Now - _lastCleanTime).TotalMinutes >= _settings.AutoCleanIntervalMinutes)
            {
                TriggerClean();
            }
        }
    }

    void OpenSettings()
    {
        using var frm = new SettingsForm(_settings);
        if (frm.ShowDialog(this) == DialogResult.OK)
        {
            TopMost = _settings.AlwaysOnTop;
            RefreshData();
            Invalidate();
        }
    }

    void OpenAbout()
    {
        using var frm = new AboutForm();
        frm.ShowDialog(this);
    }

    void ToggleVisibility()
    {
        if (Visible && WindowState != FormWindowState.Minimized)
        {
            Hide();
        }
        else
        {
            Show();
            WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();
        }
    }

    void OpenTaskManager()
    {
        try { Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }); } catch { }
    }

    void FullExit()
    {
        UnregisterHotKey(this.Handle, HOTKEY_ID);
        _refreshTimer?.Stop();
        _autoCleanTimer?.Stop();
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _currentTrayIcon?.Dispose();
        Application.Exit();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        UnregisterHotKey(this.Handle, HOTKEY_ID);
        _refreshTimer?.Stop();
        _autoCleanTimer?.Stop();
        _currentTrayIcon?.Dispose();
        base.OnFormClosing(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
        {
            TriggerClean();
            return;
        }
        base.WndProc(ref m);
    }
}
