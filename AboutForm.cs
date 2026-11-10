using System.Diagnostics;
using System.Runtime.InteropServices;
using TSuite.Design;
using TSuite.Link;

namespace TRamCash;

public class AboutForm : Form
{
    [DllImport("gdi32.dll")]
    static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr handle);
    [DllImport("user32.dll")]
    static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    readonly List<Image> _images = new();
    static readonly Color Bg = TSuiteTokens.ColorNeutralDarkBg;
    static readonly Color TextColor = TSuiteTokens.ColorNeutralDarkText;
    static readonly Color Muted = TSuiteTokens.ColorNeutralDarkTextMuted;
    static readonly Color Accent = TSuiteTokens.ColorAccentTRamCashDarkBase;

    public AboutForm()
    {
        Text = "T-RamCash • Hakkında";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(440, 520);
        MinimumSize = new Size(400, 380);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Bg;
        ForeColor = TextColor;
        Font = new Font("Segoe UI", 9);
        var header = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = TSuiteTokens.ColorNeutralDarkSurface1 };
        var title = MakeLabel("T-RamCash • Hakkında", 320, 36, 10);
        title.Padding = new Padding(16, 0, 0, 0);
        title.MouseDown += (_, e) => {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
        };
        header.Controls.Add(title);
        var dismiss = MakeButton("×", () => Close());
        dismiss.AutoSize = false;
        dismiss.Width = 36;
        dismiss.Dock = DockStyle.Right;
        dismiss.AccessibleName = "Kapat";
        header.Controls.Add(dismiss);
        var body = new FlowLayoutPanel {
            Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(16), BackColor = Bg
        };
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52,
            FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var close = MakeButton("Kapat", () => Close(), true);
        footer.Controls.Add(close);
        footer.Controls.Add(MakeButton("Sorun bildir", () => Open("https://github.com/taha-sa/t-ramcash-releases/issues")));
        footer.Controls.Add(MakeButton("Tanıyı kopyala", () => Clipboard.SetText(
            $"T-RamCash {TrayApp.Version}\n{RuntimeInformation.OSDescription}\nOS: {RuntimeInformation.OSArchitecture}; Process: {RuntimeInformation.ProcessArchitecture}")));
        Controls.Add(body);
        Controls.Add(footer);
        Controls.Add(header);
        AcceptButton = close;
        CancelButton = close;
        body.Controls.Add(Logo("t-ramcash", 64, 360));
        body.Controls.Add(MakeLabel("T-RamCash", 360, 40, 20, ContentAlignment.MiddleCenter));
        body.Controls.Add(MakeLabel($"Sürüm {TrayApp.Version} • {RuntimeInformation.ProcessArchitecture}", 360, 24, 9, ContentAlignment.MiddleCenter, Muted));
        body.Controls.Add(MakeLabel("● Çalışıyor", 360, 24, 9, ContentAlignment.MiddleCenter, TSuiteTokens.ColorStateDarkSuccessBase));
        body.Controls.Add(MakeLabel("T-Suite ailesinin parçası", 360, 32, 12));
        var registry = new AppRegistry();
        foreach (var (id, name) in new[] {
            ("t-bridge", "T-Bridge"), ("t-downloader", "T-Downloader"),
            ("t-ramcash", "T-RamCash"), ("t-desktree", "T-DeskTree"),
            ("t-desk-viewer", "T-Desk Viewer")
        })
        {
            var manifest = registry.Read(id);
            bool current = id == TrayApp.AppId;
            bool installed = current || (manifest != null && File.Exists(manifest.ExePath));
            string version = current ? TrayApp.Version : installed ? manifest.Version : "";
            var row = new Panel { Width = 360, Height = 48, BackColor = TSuiteTokens.ColorNeutralDarkSurface1, Margin = new Padding(0, 0, 0, 4) };
            var icon = Logo(id, 32, 32);
            icon.Location = new Point(8, 8);
            row.Controls.Add(icon);
            var appTitle = MakeLabel(name, 170, 22, 10);
            appTitle.Location = new Point(48, 2);
            row.Controls.Add(appTitle);
            var status = MakeLabel(current ? $"Bu uygulama • {version}" : installed ? $"Yüklü • {version}" : "Yüklü değil", 190, 20, 8, ContentAlignment.MiddleLeft, Muted);
            status.Location = new Point(48, 24);
            row.Controls.Add(status);
            if (!installed)
            {
                var link = MakeLink("… edin", $"https://github.com/taha-sa/{id}-releases/releases/latest");
                link.SetBounds(270, 12, 80, 26);
                link.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                link.AccessibleName = name + " edin";
                row.Controls.Add(link);
            }
            body.Controls.Add(row);
        }
        // No local license/privacy document: do not invent legal claims.
        body.Controls.Add(MakeLabel("Tasarım & geliştirme: Taha Şaprak", 360, 28, 9, ContentAlignment.MiddleLeft, Muted));
        var links = new FlowLayoutPanel { Width = 360, Height = 34 };
        links.Controls.Add(MakeLink("Kaynak / lisans", "https://github.com/taha-sa/t-ramcash-releases"));
        links.Controls.Add(MakeLink("@tdesk.studio", "https://www.instagram.com/tdesk.studio/"));
        body.Controls.Add(links);
        body.Layout += (_, _) => {
            int width = Math.Max(300, body.ClientSize.Width - body.Padding.Horizontal - 4);
            foreach (Control child in body.Controls)
                if (child.Width != width) child.Width = width;
        };
        Shown += (_, _) => { close.Focus(); body.AutoScrollPosition = Point.Empty; };
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Width <= 0 || Height <= 0) return;
        int radius = (int)Math.Round(16 * DeviceDpi / 96f);
        var handle = CreateRoundRectRgn(0, 0, Width + 1, Height + 1, radius, radius);
        var old = Region;
        Region = Region.FromHrgn(handle);
        DeleteObject(handle);
        old?.Dispose();
    }

    Control Logo(string id, int size, int width)
    {
        var image = BrandAssets.LoadLogo(id);
        _images.Add(image);
        return new PictureBox { Image = image, Width = width, Height = size, SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 0, 4) };
    }

    static Label MakeLabel(string text, int width, int height, float size,
        ContentAlignment alignment = ContentAlignment.MiddleLeft, Color? color = null)
        => new() { Text = text, Width = width, Height = height, ForeColor = color ?? TextColor,
            TextAlign = alignment, Font = new Font("Segoe UI", size), Margin = Padding.Empty };

    static LinkLabel MakeLink(string text, string url)
    {
        var link = new LinkLabel { Text = text, AutoSize = true, Padding = new Padding(4),
            LinkColor = Accent, ActiveLinkColor = TSuiteTokens.ColorAccentTRamCashDarkHover,
            VisitedLinkColor = Accent, AccessibleName = text };
        link.LinkClicked += (_, _) => Open(url);
        return link;
    }

    static Button MakeButton(string text, Action action, bool primary = false)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 36, FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : TSuiteTokens.ColorNeutralDarkSurface2,
            ForeColor = primary ? TSuiteTokens.ColorAccentTRamCashDarkOnAccent : TextColor };
        button.FlatAppearance.BorderColor = TSuiteTokens.ColorNeutralDarkBorder;
        button.FlatAppearance.MouseOverBackColor = primary ? TSuiteTokens.ColorAccentTRamCashDarkHover : TSuiteTokens.ColorNeutralDarkSurface3;
        button.FlatAppearance.MouseDownBackColor = primary ? TSuiteTokens.ColorAccentTRamCashDarkPressed : TSuiteTokens.ColorNeutralDarkSurface3;
        button.Click += (_, _) => action();
        return button;
    }

    static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) foreach (var image in _images) image.Dispose();
    }
}
