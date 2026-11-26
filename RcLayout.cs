using System;
using System.Drawing;
using System.Windows.Forms;

namespace TRamCash;

/// <summary>
/// Ana pencere yerlesimi: tasarim birimleri (96 DPI) -> aygit pikseli. Tek kaynak; DeviceDpi degisince yeniden hesaplanir.
/// Form boyutu, kartlar ve dugmeler buradan gelir; yerlesim kodunda sabit piksel kalmaz.
/// </summary>
sealed class RcLayout
{
    public int Dpi;
    public float Scale;
    public int W, H;
    public Rectangle Close, Min, Settings, About;
    public Rectangle Card1, Card2, Card3;
    public Rectangle CleanBtn, Admin;
    public Rectangle InfoInv, CardsInv, InfoText;

    public int S(int v) => (int)Math.Round(v * Scale);

    public static RcLayout Compute(int dpi)
    {
        var l = new RcLayout { Dpi = Math.Max(96, dpi) };
        l.Scale = l.Dpi / 96f;
        int S(int v) => l.S(v);
        l.W = S(640); l.H = S(265);
        l.Close = new Rectangle(l.W - S(36), S(12), S(24), S(22));
        l.Min = new Rectangle(l.W - S(66), S(12), S(24), S(22));
        l.Settings = new Rectangle(l.W - S(96), S(12), S(24), S(22));
        l.About = new Rectangle(l.W - S(126), S(12), S(24), S(22));
        l.Card1 = new Rectangle(S(20), S(56), S(195), S(185));
        l.Card2 = new Rectangle(S(225), S(56), S(195), S(185));
        l.Card3 = new Rectangle(S(430), S(56), S(190), S(185));
        l.Admin = new Rectangle(S(446), S(104), S(158), S(22));
        l.CleanBtn = new Rectangle(S(446), S(136), S(158), S(48));
        l.InfoText = new Rectangle(S(175), S(10), l.About.X - S(8) - S(175), S(24));
        l.InfoInv = new Rectangle(S(170), S(8), l.About.X - S(174), S(30));
        l.CardsInv = new Rectangle(S(20), S(56), S(400), S(185));
        return l;
    }

    /// <summary>--dpi N (veya tum olcekler) icin yerlesim dogrulamasi; sonuc metni. Arayuz acmaz.</summary>
    public static string SelfTest(int[] dpis)
    {
        var sb = new System.Text.StringBuilder();
        int bad = 0;
        foreach (int dpi in dpis)
        {
            var L = Compute(dpi);
            var form = new Rectangle(0, 0, L.W, L.H);
            void Chk(string name, Rectangle r, Rectangle outer)
            {
                if (!outer.Contains(r)) { bad++; sb.AppendLine($"[{dpi}] HATA {name} {r} sinir disi {outer}"); }
            }
            foreach (var (n, r) in new[] { ("Close", L.Close), ("Min", L.Min), ("Settings", L.Settings), ("About", L.About),
                ("Card1", L.Card1), ("Card2", L.Card2), ("Card3", L.Card3), ("InfoInv", L.InfoInv), ("CardsInv", L.CardsInv) })
                Chk(n, r, form);
            foreach (var (n, r) in new[] { ("Clean", L.CleanBtn), ("Admin", L.Admin) }) Chk(n, r, L.Card3);
            if (L.Admin.IntersectsWith(L.CleanBtn)) { bad++; sb.AppendLine($"[{dpi}] HATA admin/temizle cakisiyor"); }
            if (L.Card1.Right > L.Card2.X || L.Card2.Right > L.Card3.X) { bad++; sb.AppendLine($"[{dpi}] HATA kartlar cakisiyor"); }

            // Metin: font = pt * dpi / 72 piksel. Kart ici (16 birim kenar boslugu) genislige ve dikey akisa sigmali.
            Font F(float pt, FontStyle st = FontStyle.Regular) => new("Segoe UI", pt * dpi / 72f, st, GraphicsUnit.Pixel);
            Size M(string t, Font f) => TextRenderer.MeasureText(t, f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
            int inner = L.S(195) - L.S(32);
            using (var fp = F(24f, FontStyle.Bold)) using (var fs = F(8.5f)) using (var ft = F(8f, FontStyle.Bold))
            {
                var pct = M("%100", fp);
                if (pct.Width > inner) { bad++; sb.AppendLine($"[{dpi}] HATA yuzde metni {pct.Width}>{inner}"); }
                if (L.S(16) + M("X", ft).Height > L.S(36)) { bad++; sb.AppendLine($"[{dpi}] HATA baslik/yuzde dikey cakisma"); }
                if (L.S(36) + pct.Height > L.S(88)) { bad++; sb.AppendLine($"[{dpi}] HATA yuzde/alt satir dikey cakisma"); }
                if (L.S(88) + M("X", fs).Height > L.S(114)) { bad++; sb.AppendLine($"[{dpi}] HATA alt satir/cubuk cakisma"); }
                if (L.S(140) + M("X", fs).Height > L.S(185)) { bad++; sb.AppendLine($"[{dpi}] HATA alt bilgi kart disi"); }
            }
            sb.AppendLine($"[{dpi}] form {L.W}x{L.H} kontrol tamam");
        }
        sb.AppendLine(bad == 0 ? "SONUC: GECTI" : $"SONUC: BASARISIZ ({bad})");
        return sb.ToString();
    }
}
