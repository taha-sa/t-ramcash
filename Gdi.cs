using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace TRamCash;

/// <summary>
/// Paylasimli GDI+ nesne onbellegi: font / firca / kalem / yuvarlak dikdortgen yolu / StringFormat.
/// Boyama sirasinda yeni nesne uretilmez. Acik form sayisi sifira dusunce tum nesneler serbest birakilir
/// (tepsi modunda GDI+ belleği tutulmaz). Yalnizca UI thread'inden kullanilir.
/// </summary>
static class Gdi
{
    static int _refs;
    static readonly Dictionary<(string, float, FontStyle), Font> Fonts = new();
    static readonly Dictionary<int, SolidBrush> Brushes = new();
    static readonly Dictionary<(int, float), Pen> Pens = new();
    static readonly Dictionary<(int, int, int, int, int), GraphicsPath> Paths = new();
    static StringFormat _center, _hCenter, _vCenterLeft;

    public static void Acquire() => _refs++;

    public static void Release()
    {
        if (--_refs > 0) return;
        _refs = 0;
        foreach (var f in Fonts.Values) f.Dispose();
        foreach (var b in Brushes.Values) b.Dispose();
        foreach (var p in Pens.Values) p.Dispose();
        foreach (var g in Paths.Values) g.Dispose();
        Fonts.Clear(); Brushes.Clear(); Pens.Clear(); Paths.Clear();
        _center?.Dispose(); _hCenter?.Dispose(); _vCenterLeft?.Dispose();
        _center = _hCenter = _vCenterLeft = null;
    }

    public static Font GetFont(string name, float size, FontStyle style = FontStyle.Regular)
    {
        var key = (name, size, style);
        if (!Fonts.TryGetValue(key, out var f)) Fonts[key] = f = new Font(name, size, style);
        return f;
    }

    public static SolidBrush GetBrush(Color c)
    {
        int k = c.ToArgb();
        if (!Brushes.TryGetValue(k, out var b)) Brushes[k] = b = new SolidBrush(c);
        return b;
    }

    public static Pen GetPen(Color c, float width = 1f)
    {
        var key = (c.ToArgb(), width);
        if (!Pens.TryGetValue(key, out var p)) Pens[key] = p = new Pen(c, width);
        return p;
    }

    /// <summary>Yuvarlak dikdortgen yolu (onbellekli).</summary>
    public static GraphicsPath RoundedPath(Rectangle r, int radius)
    {
        var key = (r.X, r.Y, r.Width, r.Height, radius);
        if (!Paths.TryGetValue(key, out var path))
        {
            path = new GraphicsPath();
            int d = Math.Max(1, radius * 2);
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            if (Paths.Count > 64) { foreach (var g in Paths.Values) g.Dispose(); Paths.Clear(); }
            Paths[key] = path;
        }
        return path;
    }

    public static StringFormat Center => _center ??= new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
    public static StringFormat HCenter => _hCenter ??= new StringFormat { Alignment = StringAlignment.Center };
    public static StringFormat VCenterLeft => _vCenterLeft ??= new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };

    public static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }
}
