using System.Reflection;
using TSuite.Design;

namespace TRamCash;

internal sealed class LegalDialog : Form
{
    readonly Font _textFont;

    internal LegalDialog()
    {
        Text = AccessibleName = Strings.Legal;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(760, 560);
        MinimumSize = new Size(480, 360);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = TrayApp.Theme.Bg; ForeColor = TrayApp.Theme.Text;
        ShowInTaskbar = false;
        _textFont = new Font("Segoe UI", 10);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        foreach (var (title, file) in new[] { (Strings.Privacy, "PRIVACY.md"), (Strings.License, "LICENSE"), (Strings.Notices, "THIRD-PARTY-NOTICES.md") })
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Legal." + file)
                ?? throw new InvalidOperationException("Missing embedded legal document: " + file);
            using var reader = new StreamReader(stream);
            var page = new TabPage(title) { BackColor = TrayApp.Theme.Bg, ForeColor = TrayApp.Theme.Text };
            page.Controls.Add(new TextBox { Text = reader.ReadToEnd(), ReadOnly = true, Multiline = true,
                ScrollBars = ScrollBars.Vertical, WordWrap = true, Dock = DockStyle.Fill,
                BackColor = TrayApp.Theme.Surface, ForeColor = TrayApp.Theme.Text, Font = _textFont,
                AccessibleName = title, SelectionStart = 0 });
            tabs.TabPages.Add(page);
        }
        var close = new Button { Text = Strings.Close, AccessibleName = Strings.Close,
            Dock = DockStyle.Bottom, Height = 40, DialogResult = DialogResult.Cancel };
        Controls.Add(tabs); Controls.Add(close); CancelButton = close;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _textFont?.Dispose();
    }
}
