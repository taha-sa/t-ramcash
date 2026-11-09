using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TRamCash;

public class AboutForm : Form
{
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

    const int WM_NCLBUTTONDOWN = 0xA1;
    const int HT_CAPTION = 0x2;

    // Renk Paleti (MainForm ile birebir uyumlu derin mat karbon ve amber altın)
    static readonly Color BgDark = Color.FromArgb(14, 14, 18);
    static readonly Color CardBg = Color.FromArgb(20, 20, 26);
    static readonly Color CardBgHover = Color.FromArgb(28, 28, 36);
    static readonly Color CardBorder = Color.FromArgb(34, 34, 44);
    static readonly Color DarkGold = Color.FromArgb(212, 160, 23);
    static readonly Color DarkGoldHover = Color.FromArgb(228, 176, 36);
    static readonly Color TextWhite = Color.FromArgb(240, 240, 245);
    static readonly Color TextMuted = Color.FromArgb(145, 145, 158);
    static readonly Color TextDim = Color.FromArgb(92, 92, 104);

    readonly Rectangle _rcClose = new Rectangle(306, 12, 24, 22);
    readonly Rectangle _rcInstagram = new Rectangle(20, 138, 300, 58);
    readonly Rectangle _rcBtnClose = new Rectangle(20, 288, 300, 36);

    bool _hoverClose = false;
    bool _hoverInstagram = false;
    bool _hoverBtnClose = false;

    public AboutForm()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        Size = new Size(340, 342);
        BackColor = BgDark;
        KeyPreview = true;

        ApplyRoundedRegion();
    }

    void ApplyRoundedRegion()
    {
        IntPtr ptr = CreateRoundRectRgn(0, 0, Width, Height, 16, 16);
        Region = Region.FromHrgn(ptr);
        DeleteObject(ptr);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
        {
            Close();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // Dış kenar çizgisi
        using (var penBorder = new Pen(CardBorder, 1))
            g.DrawRectangle(penBorder, 0, 0, Width - 1, Height - 1);

        // 1. Üst Başlık Çubuğu
        DrawHeader(g);

        // 2. Uygulama Bilgi Kartı
        DrawAppInfoCard(g, 20, 52, 300, 74);

        // 3. Instagram Kartı / Butonu
        DrawInstagramCard(g, _rcInstagram);

        // 4. İmza Bölümü ("Taha Şaprak " - küçük imza fontuyla)
        DrawSignatureSection(g, 20, 208, 300, 68);

        // 5. Kapat Butonu
        DrawBottomCloseButton(g, _rcBtnClose);
    }

    void DrawHeader(Graphics g)
    {
        // Logo
        DrawCustomLogo(g, 20, 12, 18);

        // Başlık
        using (var fTitle = new Font("Segoe UI", 10F, FontStyle.Bold))
        using (var bGold = new SolidBrush(DarkGold))
            g.DrawString("T-RAMCASH", fTitle, bGold, 46, 13);

        using (var fSub = new Font("Segoe UI", 8.5F))
        using (var bMuted = new SolidBrush(TextDim))
            g.DrawString("•  Hakkında", fSub, bMuted, 134, 14);

        // Kapat Butonu (✕)
        if (_hoverClose)
        {
            using var bHover = new SolidBrush(Color.FromArgb(28, 28, 36));
            g.FillRectangle(bHover, _rcClose);
        }
        using (var font = new Font("Segoe UI", 9F, FontStyle.Regular))
        using (var brush = new SolidBrush(_hoverClose ? DarkGold : TextMuted))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            g.DrawString("✕", font, brush, _rcClose, sf);
        }

        // İnce ayırıcı çizgi
        using var penLine = new Pen(CardBorder, 1);
        g.DrawLine(penLine, 20, 42, Width - 20, 42);
    }

    void DrawCustomLogo(Graphics g, int x, int y, int s)
    {
        var prevSmoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Dış yuvarlatılmış gövde (Squircle)
        var chipRect = new RectangleF(x, y, s, s);
        float radius = s * 0.22f;
        using var path = CreateRoundedRectanglePath(chipRect, radius);

        // Çip gövdesi
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

    void DrawAppInfoCard(Graphics g, int x, int y, int w, int h)
    {
        var rc = new Rectangle(x, y, w, h);
        using (var b = new SolidBrush(CardBg))
            g.FillRectangle(b, rc);
        using (var p = new Pen(CardBorder, 1))
            g.DrawRectangle(p, rc);

        // Büyük Vurgulu Logo Rozeti
        DrawCustomLogo(g, x + 12, y + 13, 48);

        int textX = x + 70;
        using (var fBold = new Font("Segoe UI", 9.5F, FontStyle.Bold))
        using (var bWhite = new SolidBrush(TextWhite))
            g.DrawString("T-RamCash", fBold, bWhite, textX, y + 12);

        using (var fDesc = new Font("Segoe UI", 8F))
        using (var bMuted = new SolidBrush(TextMuted))
            g.DrawString("Akıllı Bellek & Önbellek Yöneticisi", fDesc, bMuted, textX, y + 32);

        using (var fVer = new Font("Segoe UI", 7.5F))
        using (var bDim = new SolidBrush(DarkGold))
            g.DrawString("Sürüm 3.0 • Windows x64", fVer, bDim, textX, y + 50);
    }

    void DrawInstagramCard(Graphics g, Rectangle rc)
    {
        Color bg = _hoverInstagram ? CardBgHover : CardBg;
        Color border = _hoverInstagram ? DarkGold : CardBorder;

        using (var b = new SolidBrush(bg))
            g.FillRectangle(b, rc);
        using (var p = new Pen(border, 1))
            g.DrawRectangle(p, rc);

        // Instagram İkon / Rozet Metni
        using (var fBadge = new Font("Segoe UI", 8F, FontStyle.Bold))
        using (var bGold = new SolidBrush(DarkGold))
            g.DrawString("INSTAGRAM", fBadge, bGold, rc.X + 14, rc.Y + 11);

        // Kullanıcı Adı: tdesk.studio
        using (var fInsta = new Font("Segoe UI", 11F, FontStyle.Bold))
        using (var bWhite = new SolidBrush(_hoverInstagram ? DarkGoldHover : TextWhite))
            g.DrawString("@tdesk.studio", fInsta, bWhite, rc.X + 13, rc.Y + 28);

        // Sağdaki yönlendirme oku (➔)
        using (var fArrow = new Font("Segoe UI", 11F))
        using (var bArrow = new SolidBrush(_hoverInstagram ? DarkGold : TextDim))
        using (var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
        {
            var arrowRc = new Rectangle(rc.Right - 36, rc.Y, 26, rc.Height);
            g.DrawString("↗", fArrow, bArrow, arrowRc, sf);
        }
    }

    void DrawSignatureSection(Graphics g, int x, int y, int w, int h)
    {
        var rc = new Rectangle(x, y, w, h);
        using (var b = new SolidBrush(CardBg))
            g.FillRectangle(b, rc);
        using (var p = new Pen(CardBorder, 1))
            g.DrawRectangle(p, rc);

        // Üst Başlık (Küçük & Muted)
        using (var fSub = new Font("Segoe UI", 7.5F))
        using (var bDim = new SolidBrush(TextDim))
            g.DrawString("TASARIM & GELİŞTİRME", fSub, bDim, x + 14, y + 10);

        // İmza Fontu: "Taha Şaprak " (Zarif, küçük imza)
        using var fontSig = GetSignatureFont(10.5f);
        using var brushSig = new SolidBrush(Color.FromArgb(220, 175, 45)); // Zarif altın tonu
        g.DrawString("Taha Şaprak ", fontSig, brushSig, x + 14, y + 28);
    }

    void DrawBottomCloseButton(Graphics g, Rectangle rc)
    {
        Color bg = _hoverBtnClose ? Color.FromArgb(32, 32, 42) : Color.FromArgb(24, 24, 32);
        Color border = _hoverBtnClose ? DarkGold : CardBorder;

        using (var b = new SolidBrush(bg))
            g.FillRectangle(b, rc);
        using (var p = new Pen(border, 1))
            g.DrawRectangle(p, rc);

        using var font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        using var brush = new SolidBrush(_hoverBtnClose ? DarkGold : TextMuted);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("KAPAT", font, brush, rc, sf);
    }

    Font GetSignatureFont(float size)
    {
        string[] candidates = { "Segoe Script", "Brush Script MT", "Pristina", "Edwardian Script ITC", "Lucida Handwriting" };
        foreach (var name in candidates)
        {
            try
            {
                var f = new Font(name, size, FontStyle.Italic);
                if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
                    return f;
                f.Dispose();
            }
            catch { }
        }
        return new Font("Segoe UI", size, FontStyle.Italic);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (_rcClose.Contains(e.Location) || _rcBtnClose.Contains(e.Location))
        {
            Close();
            return;
        }

        if (_rcInstagram.Contains(e.Location))
        {
            OpenInstagram();
            return;
        }

        // Pencereyi sürükleme
        if (e.Button == MouseButtons.Left && e.Y < 42)
        {
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        bool newClose = _rcClose.Contains(e.Location);
        bool newInsta = _rcInstagram.Contains(e.Location);
        bool newBtnClose = _rcBtnClose.Contains(e.Location);

        if (newClose != _hoverClose || newInsta != _hoverInstagram || newBtnClose != _hoverBtnClose)
        {
            _hoverClose = newClose;
            _hoverInstagram = newInsta;
            _hoverBtnClose = newBtnClose;
            Cursor = (newClose || newInsta || newBtnClose) ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverClose = false;
        _hoverInstagram = false;
        _hoverBtnClose = false;
        Invalidate();
    }

    void OpenInstagram()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://www.instagram.com/tdesk.studio")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Tarayıcı açılamadı: " + ex.Message, "T-RamCash", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
