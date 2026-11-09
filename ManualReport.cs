using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TSuite.Diagnostics;

namespace TRamCash;

static class ManualReport
{
    public static void Show(IWin32Window owner)
    {
        var lines = new Queue<string>();
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "T-Suite", "logs", TrayApp.AppId, "app.log");
        foreach (string file in new[] { path + ".2", path + ".1", path })
        {
            try
            {
                if (!File.Exists(file)) continue;
                foreach (string line in File.ReadLines(file))
                {
                    lines.Enqueue(line);
                    if (lines.Count > 200) lines.Dequeue();
                }
            }
            catch (Exception ex) { Log.Warn("Could not read recent log: " + ex.Message); }
        }
        using var process = Process.GetCurrentProcess();
        var report = new CrashReport(Guid.NewGuid().ToString("N"), DateTime.UtcNow, TrayApp.AppId, "T-RamCash", TrayApp.Version,
            "taha-sa/t-ramcash-releases", RuntimeInformation.OSDescription, RuntimeInformation.FrameworkDescription,
            CultureInfo.CurrentCulture.Name, DateTime.UtcNow - process.StartTime.ToUniversalTime(),
            new ExceptionDetails("Manual report", "Kullanıcı tarafından hazırlanan hata raporu", null, Array.Empty<ExceptionDetails>()), lines.ToArray());
        using var dialog = new CrashReportDialog(report, TrayApp.Theme);
        dialog.Text = dialog.AccessibleName = "Hata raporu gönder";
        foreach (Control control in dialog.Controls)
        {
            if (control is Label label && label.Text == "Uygulama beklenmedik şekilde kapandı") label.Text = label.AccessibleName = "Hata raporu gönder";
            else if (control is Label explanation && explanation.Text.StartsWith("İsterseniz"))
                explanation.Text = "Raporu inceleyin. Raporu gönder düğmesi GitHub'da bir sorun taslağı açar.";
            else if (control is CheckBox checkbox) checkbox.Visible = false;
        }
        dialog.ShowDialog(owner);
    }
}
