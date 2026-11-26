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
    readonly List<Font> _fonts = new();
    static readonly Color Bg = TSuiteTokens.ColorNeutralDarkBg;
    static readonly Color TextColor = TSuiteTokens.ColorNeutralDarkText;
    static readonly Color Muted = TSuiteTokens.ColorNeutralDarkTextMuted;
    static readonly Color Accent = TSuiteTokens.ColorAccentTRamCashDarkBase;

    public AboutForm()
    {
        Text = Strings.AboutTitle;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 560);
        MinimumSize = new Size(400, 380);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Bg;
        ForeColor = TextColor;
        Font = TrackFont(9);
        var header = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = TSuiteTokens.ColorNeutralDarkSurface1 };
        var title = MakeLabel(Strings.AboutTitle, 320, 36, 10);
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
        dismiss.AccessibleName = Strings.Close;
        header.Controls.Add(dismiss);
        var body = new FlowLayoutPanel {
            Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(16), BackColor = Bg
        };
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 82,
            FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var close = MakeButton(Strings.Close, () => Close(), true);
        footer.Controls.Add(close);
        footer.Controls.Add(MakeButton(Strings.ReportIssue, () => Open("https://github.com/taha-sa/t-ramcash-releases/issues")));
        footer.Controls.Add(MakeButton(Strings.CopyDiagnostics, () => {
            try { Clipboard.SetText($"T-RamCash {TrayApp.Version}\n{RuntimeInformation.OSDescription}\n{RuntimeInformation.OSArchitecture} / {RuntimeInformation.ProcessArchitecture}"); }
            catch { MessageBox.Show(this, Strings.ActionFailed, Strings.CopyDiagnostics); }
        }));
        Controls.Add(body);
        Controls.Add(footer);
        Controls.Add(header);
        AcceptButton = close;
        CancelButton = close;
        body.Controls.Add(Logo("t-ramcash", 64, 360));
        body.Controls.Add(MakeLabel("T-RamCash", 360, 40, 20, ContentAlignment.MiddleCenter));
        body.Controls.Add(MakeLabel(Strings.Format(Strings.VersionInfo, TrayApp.Version, RuntimeInformation.ProcessArchitecture), 360, 24, 9, ContentAlignment.MiddleCenter, Muted));
        body.Controls.Add(MakeLabel(Strings.Running, 360, 24, 9, ContentAlignment.MiddleCenter, TSuiteTokens.ColorStateDarkSuccessBase));
        body.Controls.Add(MakeLabel(Strings.SuiteFamily, 360, 32, 12));
        body.Controls.Add(BridgeInvitation.CreateRow(this));
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
            var status = MakeLabel(current ? Strings.Format(Strings.CurrentApp, version) : installed ? Strings.Format(Strings.Installed, version) : Strings.NotInstalled, 190, 20, 8, ContentAlignment.MiddleLeft, Muted);
            status.Location = new Point(48, 24);
            row.Controls.Add(status);
            if (!installed)
            {
                var link = MakeLink(Strings.Format(Strings.GetNamedApp, name), $"https://github.com/taha-sa/{(id == "t-desk-viewer" ? "tdesk-viewer" : id)}-releases/releases/latest");
                link.AutoSize = false;
                link.SetBounds(218, 8, 140, 36);
                link.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                link.AccessibleName = Strings.Format(Strings.GetNamedApp, name);
                row.Controls.Add(link);
            }
            body.Controls.Add(row);
        }
        body.Controls.Add(MakeButton(Strings.Legal, () => { using var legal = new LegalDialog(); legal.ShowDialog(this); }));
        body.Controls.Add(MakeLabel(Strings.Credits, 360, 28, 9, ContentAlignment.MiddleLeft, Muted));
        var links = new FlowLayoutPanel { Width = 360, Height = 34 };
        links.Controls.Add(MakeLink(Strings.SourceLicense, "https://github.com/taha-sa/t-ramcash-releases"));
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
        if (handle == IntPtr.Zero) return;
        var old = Region;
        Region = Region.FromHrgn(handle);
        DeleteObject(handle);
        old?.Dispose();
    }

    Font TrackFont(float size)
    {
        var font = new Font("Segoe UI", size);
        _fonts.Add(font);
        return font;
    }

    Control Logo(string id, int size, int width)
    {
        var image = BrandAssets.LoadLogo(id);
        _images.Add(image);
        return new PictureBox { Image = image, Width = width, Height = size, SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 0, 4) };
    }

    Label MakeLabel(string text, int width, int height, float size,
        ContentAlignment alignment = ContentAlignment.MiddleLeft, Color? color = null)
        => new() { Text = text, Width = width, Height = height, ForeColor = color ?? TextColor,
            TextAlign = alignment, Font = TrackFont(size), Margin = Padding.Empty };

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
        LocalDialog.Open(null, url);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            foreach (var image in _images) image.Dispose();
            _images.Clear();
            foreach (var font in _fonts) font.Dispose();
            _fonts.Clear();
        }
    }
}
