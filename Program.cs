using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace TRamCash;

static class Program
{
    [DllImport("shell32.dll", SetLastError = true)]
    static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    const string MutexName = @"Global\TRamCash_SingleInstance_Mutex_998877";
    const string ShowEventName = @"Local\TRamCash_ShowWindow_998877";

    [STAThread]
    static void Main(string[] args)
    {
        // Test kancasi: `--dpi N` yerlesim dogrulamasi (arayuz yok); sonuc TEMP ramcash-dpi-test.txt. N yoksa 96/120/144/168.
        if (args.Length > 0 && args[0] == "--dpi")
        {
            int[] dpis = args.Length > 1 && int.TryParse(args[1], out int d) ? new[] { d } : new[] { 96, 120, 144, 168 };
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ramcash-dpi-test.txt"), RcLayout.SelfTest(dpis));
            return;
        }

        if (args.Length > 0 && args[0] == "--tray-test")
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ramcash-tray-test.txt"), TrayIconRender.SelfTest());
            return;
        }

        // Komut satırından sessiz temizleme (--clean veya -c): Tam mod
        if (args.Length > 0 && (args[0] == "--clean" || args[0] == "-c"))
        {
            try
            {
                var r = MemoryCleaner.Clean(AppSettings.Load(), CleanMode.Full);
                Console.WriteLine("freed=" + r.FreedMb.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                Environment.ExitCode = 0;
            }
            catch
            {
                Environment.ExitCode = 1;
            }
            return;
        }

        // T-Bridge "Hepsini güncelle": kapalıyken `--update` ile başlatılır -> kontrol, indirme, yardımcıyı başlat, çık (arayüz yok).
        if (TSuite.Update.UpdateCli.IsRequested(args))
        {
            Environment.ExitCode = TSuite.Update.UpdateCli.RunAsync(TrayApp.MakeUpdateOptions()).GetAwaiter().GetResult();
            return;
        }

        bool startInTray = Array.Exists(args, a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));
        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!createdNew)
        {
            // Çalışan kopyaya "pencereyi göster" sinyali gönder (pencere gizliyken de çalışır).
            try { if (!startInTray) showEvent.Set(); } catch { }
            return;
        }

        try { SetCurrentProcessExplicitAppUserModelID("TRamCash.App.V3"); } catch { }

        ApplicationConfiguration.Initialize();
        TSuite.Diagnostics.CrashReporter.Install(TrayApp.AppId, "T-RamCash", TrayApp.Version, "taha-sa/t-ramcash-releases", TrayApp.Theme);
        TSuite.Diagnostics.CrashReporter.InstallWinForms();
        TSuite.Diagnostics.Log.Info("Application starting");
        Application.Run(new TrayApp(showEvent, startInTray));
    }
}
