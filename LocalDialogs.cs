using System.Diagnostics;
using TSuite.Diagnostics;
using TSuite.Update;

namespace TRamCash;

// Local views keep SDK state, signature verification and report scrubbing intact.
internal class LocalDialog : Form
{
    readonly Font _baseFont;
    protected readonly FlowLayoutPanel Body;
    protected readonly FlowLayoutPanel Actions;
    internal LocalDialog(string title)
    {
        Text = AccessibleName = title;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(580, 420);
        MinimumSize = new Size(480, 320);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = TrayApp.Theme.Bg;
        ForeColor = TrayApp.Theme.Text;
        _baseFont = new Font("Segoe UI", 10);
        Font = _baseFont;
        Actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(12), FlowDirection = FlowDirection.RightToLeft, WrapContents = true };
        Body = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(16), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        Controls.Add(Body);
        Controls.Add(Actions);
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _baseFont?.Dispose();
    }
    protected Label AddText(string text, int height = 72)
    {
        var label = new Label { Text = text, AccessibleName = text, Width = 520, Height = height, Margin = new Padding(0, 0, 0, 12) };
        Body.Controls.Add(label);
        return label;
    }
    protected Button AddButton(string text, Action action)
    {
        var button = new Button { Text = text, AccessibleName = text, AutoSize = true, MinimumSize = new Size(90, 36), FlatStyle = FlatStyle.Flat, BackColor = TrayApp.Theme.Surface, ForeColor = ForeColor };
        button.Click += (_, _) => action();
        Actions.Controls.Add(button);
        return button;
    }
    internal static bool Open(IWin32Window owner, string target)
    {
        try
        {
            if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                MessageBox.Show(owner, Strings.LaunchFailed, "T-RamCash", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch { MessageBox.Show(owner, Strings.LaunchFailed, "T-RamCash", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
    }
    internal static bool OpenExecutable(IWin32Window owner, string exePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(exePath) || !Path.IsPathRooted(exePath) ||
                !exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(exePath))
            {
                MessageBox.Show(owner, Strings.LaunchFailed, "T-RamCash", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exePath) ?? "" })?.Dispose();
            return true;
        }
        catch { MessageBox.Show(owner, Strings.LaunchFailed, "T-RamCash", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
    }
}

internal sealed class LocalReportDialog : LocalDialog
{
    internal LocalReportDialog(CrashReport report, bool manual = false) : base(manual ? Strings.ReportTitle : Strings.CrashTitle)
    {
        ClientSize = new Size(620, 560);
        AddText(Strings.ReportHelp);
        var preview = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Width = 560, Height = 290, Text = CrashReporter.GetReportText(report), AccessibleName = Strings.ReportPreview, BackColor = TrayApp.Theme.Surface, ForeColor = ForeColor };
        Body.Controls.Add(preview);
        if (!manual)
        {
            var prepare = new CheckBox { Text = Strings.PrepareReports, AccessibleName = Strings.PrepareReports, Checked = CrashReporter.AutomaticallyPrepareReports, AutoSize = true };
            prepare.CheckedChanged += (_, _) => CrashReporter.AutomaticallyPrepareReports = prepare.Checked;
            Body.Controls.Add(prepare);
        }
        var status = AddText("", 40);
        var close = AddButton(Strings.Close, () => Close());
        close.DialogResult = DialogResult.Cancel;
        CancelButton = close;
        AddButton(Strings.Copy, () => {
            try { Clipboard.SetText(CrashReporter.GetReportText(report)); status.Text = status.AccessibleName = Strings.ReportCopied; }
            catch { status.Text = status.AccessibleName = Strings.ActionFailed; }
        });
        AddButton(Strings.ReportTitle, () => {
            if (Open(this, CrashReporter.BuildIssueUrl(report))) { if (!manual) CrashReporter.MarkReported(report); Close(); }
        });
        FormClosed += (_, _) => { if (!manual) CrashReporter.MarkDismissed(report); };
    }
}

internal sealed class LocalUpdateDialog : LocalDialog
{
    readonly UpdateController controller;
    readonly Label status;
    readonly Button primary, later, skip;
    readonly System.Windows.Forms.Timer refresh = new() { Interval = 250 };

    internal LocalUpdateDialog(UpdateController controller) : base(Strings.UpdateTitle)
    {
        this.controller = controller;
        var info = controller.Info;
        AddText($"T-RamCash {info?.CurrentVersion} → {info?.NewVersion}", 32);
        if (info?.IsRequired == true) AddText(Strings.RequiredUpdate);
        Body.Controls.Add(new TextBox { Text = info?.Notes ?? "", AccessibleName = Strings.UpdateTitle, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Width = 520, Height = 160, BackColor = TrayApp.Theme.Surface, ForeColor = ForeColor });
        status = AddText("", 55);
        later = AddButton(Strings.Later, () => { controller.Later(); Close(); });
        skip = AddButton(Strings.SkipVersion, () => { controller.Skip(); Close(); });
        primary = AddButton(Strings.UpdateNow, () => _ = controller.StartUpdateAsync());
        refresh.Tick += (_, _) => RefreshState();
        refresh.Start();
        RefreshState();
        FormClosing += (_, e) => {
            // Keep user dismissals blocked, but let the updater exit the app after
            // launching its helper (IsBusy remains true in the Launching state).
            if (e.CloseReason == CloseReason.UserClosing && (controller.IsBusy || controller.Info?.IsRequired == true))
                e.Cancel = true;
        };
    }
    void RefreshState()
    {
        status.Text = status.AccessibleName = controller.State switch {
            UpdateState.Failed => Strings.UpdateFailed,
            UpdateState.Downloading => Strings.Format(Strings.Downloading, controller.Progress),
            UpdateState.Launching => Strings.Installing,
            _ => ""
        };
        primary.Text = primary.AccessibleName = controller.State == UpdateState.Failed ? Strings.Retry : Strings.UpdateNow;
        primary.Enabled = !controller.IsBusy;
        later.Enabled = !controller.IsBusy && controller.Info?.IsRequired != true;
        skip.Enabled = !controller.IsBusy && controller.Info?.IsRequired != true;
    }
    protected override void Dispose(bool disposing) { if (disposing) refresh.Dispose(); base.Dispose(disposing); }
}
