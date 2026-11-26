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
            new ExceptionDetails("Manual report", Strings.ManualReport, null, Array.Empty<ExceptionDetails>()), lines.ToArray());
        using var dialog = new LocalReportDialog(report, manual: true);
        dialog.ShowDialog(owner);
    }
}
