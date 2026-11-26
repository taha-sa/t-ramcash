using TSuite.Link;

namespace TRamCash;

internal static class BridgeInvitation
{
    const string Download = "https://github.com/taha-sa/t-bridge-releases/releases/latest";

    static bool IsValidExePath(string path)
        => !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path) &&
           path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path);

    static string InstalledPath()
    {
        var manifest = new AppRegistry().Read("t-bridge");
        if (manifest != null && IsValidExePath(manifest.ExePath)) return manifest.ExePath;
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
            foreach (var relative in new[] { @"Programs\T-Bridge\T-Bridge.exe", @"T-Bridge\T-Bridge.exe", @"T-Suite\T-Bridge\T-Bridge.exe" })
            {
                var path = Path.Combine(root, relative);
                if (File.Exists(path)) return path;
            }
        return null;
    }

    internal static Control CreateRow(Form owner)
    {
        var row = new FlowLayoutPanel { Width = 360, Height = 112, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 8, 0, 8) };
        row.Controls.Add(new Label { Text = Strings.BridgeLine, ForeColor = TrayApp.Theme.TextDim, Width = 350, Height = 52 });
        var button = new Button { AutoSize = true, Height = 36, FlatStyle = FlatStyle.Flat, ForeColor = TrayApp.Theme.Accent };
        void Refresh() { button.Text = button.AccessibleName = InstalledPath() == null ? Strings.GetBridge : Strings.OpenBridge; }
        Refresh();
        button.Click += (_, _) => {
            var path = InstalledPath();
            if (path != null) LocalDialog.OpenExecutable(owner, path);
            else { using var dialog = new InvitationDialog(); dialog.ShowDialog(owner); }
            Refresh();
        };
        // Recheck while open so installing Bridge removes the invitation automatically.
        var timer = new System.Windows.Forms.Timer { Interval = 2000 };
        timer.Tick += (_, _) => Refresh();
        timer.Start();
        row.Disposed += (_, _) => timer.Dispose();
        row.Controls.Add(button);
        return row;
    }

    sealed class InvitationDialog : LocalDialog
    {
        readonly Image icon;
        internal InvitationDialog() : base(Strings.BridgeTitle)
        {
            ClientSize = new Size(580, 280);
            icon = BrandAssets.LoadLogo("t-bridge");
            Body.Controls.Add(new PictureBox { Image = icon, Width = 64, Height = 64, SizeMode = PictureBoxSizeMode.Zoom, AccessibleName = "T-Bridge" });
            AddText(Strings.BridgeLine);
            var close = AddButton("×", () => Close());
            close.AccessibleName = Strings.Close;
            close.DialogResult = DialogResult.Cancel;
            CancelButton = close;
            AddButton(Strings.GetBridge, () => Open(this, Download));
        }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) icon.Dispose(); }
    }
}
