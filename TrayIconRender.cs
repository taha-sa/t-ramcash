using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace TRamCash;

/// <summary>
/// Tepsi ikonu: SystemInformation.SmallIconSize (DPI'ya gore 16/20/24/32) boyutunda dogrudan cizilir (kucultme yok),
/// piksel hizali kalin yazi (SingleBitPerPixelGridFit). 100 degeri ucuncu hane yerine "99+" olarak yazilir;
/// yazi hicbir boyutta ikon sinirini asmaz (gerekirse yazi tipi piksel adimlariyla kuculur).
/// </summary>
static class TrayIconRender
{
    static readonly Color Bg = Color.FromArgb(14, 14, 18);
    static readonly Color Gold = Color.FromArgb(212, 160, 23);

    public static int IconSize()
    {
        int s = SystemInformation.SmallIconSize.Width;
        return Math.Clamp(s, 16, 48);
    }

    public static string TextFor(uint pct) => pct >= 100 ? "99+" : pct.ToString();

    /// <summary>size x size ikon bitmap'i cizer; textSize = yazinin olculen boyutu (test icin).</summary>
    public static Bitmap Render(int size, uint pct, out Size textSize)
    {
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Bg);
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        int bw = Math.Max(1, size / 16);
        using (var pen = new Pen(Gold, bw) { Alignment = PenAlignment.Inset })
            g.DrawRectangle(pen, 0, 0, size - 1, size - 1);

        string text = TextFor(pct);
        int avail = size - 2 * bw;                // kenarligin ici
        int maxW = avail, maxH = avail;
        // Baslangic: yuksekligin ~%68'i; sigmazsa 1 piksel adimlarla kucult.
        float px = Math.Max(6f, size * 0.68f);
        Font font = null;
        Size ts = Size.Empty;
        for (; px >= 6f; px -= 1f)
        {
            font?.Dispose();
            font = new Font("Segoe UI", px, FontStyle.Bold, GraphicsUnit.Pixel);
            ts = TextRenderer.MeasureText(g, text, font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            if (ts.Width <= maxW && ts.Height <= maxH) break;
        }
        textSize = ts;
        using (font)
        {
            var r = new Rectangle(bw, bw, avail, avail);
            TextRenderer.DrawText(g, text, font, r, Color.White,
                TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoClipping);
        }
        return bmp;
    }

    /// <summary>--tray-test: 16/20/24/32 x 0..100 sinir kontrolu + PNG dokumu (TEMP\ramcash-tray-test.png / .txt).</summary>
    public static string SelfTest()
    {
        int[] sizes = { 16, 20, 24, 32 };
        var uniq = new uint[] { 0, 7, 50, 88, 99, 100 };
        var sb = new System.Text.StringBuilder();
        int bad = 0;
        foreach (int sz in sizes)
            for (uint p = 0; p <= 100; p++)
            {
                using var b = Render(sz, p, out var ts);
                int bw = Math.Max(1, sz / 16);
                if (ts.Width > sz - 2 * bw || ts.Height > sz - 2 * bw) { bad++; sb.AppendLine($"HATA {sz}px %{p} metin {ts} sinir disi"); }
                // Piksel denetimi: kenarlik halkasinin disinda/uzerinde beyaz piksel olmamali
                for (int i = 0; i < sz; i++)
                    for (int k = 0; k < bw; k++)
                    {
                        if (IsWhite(b.GetPixel(i, k)) || IsWhite(b.GetPixel(i, sz - 1 - k)) || IsWhite(b.GetPixel(k, i)) || IsWhite(b.GetPixel(sz - 1 - k, i)))
                        { bad++; sb.AppendLine($"HATA {sz}px %{p} yazi kenarliga tasti"); i = sz; break; }
                    }
            }
        // Gorsel dokum: her satir bir boyut, sutunlar secili degerler (4x buyutulmus)
        const int Z = 4, gap = 4;
        using var sheet = new Bitmap(uniq.Length * (32 * Z + gap) + gap, sizes.Length * (32 * Z + gap) + gap);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(60, 60, 70));
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            for (int r = 0; r < sizes.Length; r++)
                for (int c = 0; c < uniq.Length; c++)
                {
                    using var b = Render(sizes[r], uniq[c], out _);
                    g.DrawImage(b, gap + c * (32 * Z + gap), gap + r * (32 * Z + gap), sizes[r] * Z, sizes[r] * Z);
                }
        }
        string dir = System.IO.Path.GetTempPath();
        sheet.Save(System.IO.Path.Combine(dir, "ramcash-tray-test.png"), System.Drawing.Imaging.ImageFormat.Png);
        sb.AppendLine(bad == 0 ? "SONUC: GECTI (4 boyut x 0-100)" : $"SONUC: BASARISIZ ({bad})");
        return sb.ToString();
    }

    static bool IsWhite(Color c) => c.R > 200 && c.G > 200 && c.B > 200;
}
