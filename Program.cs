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
        // Komut satırından sessiz temizleme (--clean veya -c): Tam mod
        if (args.Length > 0 && (args[0] == "--clean" || args[0] == "-c"))
        {
            MemoryCleaner.Clean(AppSettings.Load(), CleanMode.Full);
            return;
        }

        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!createdNew)
        {
            // Çalışan kopyaya "pencereyi göster" sinyali gönder (pencere gizliyken de çalışır).
            try { showEvent.Set(); } catch { }
            return;
        }

        try { SetCurrentProcessExplicitAppUserModelID("TRamCash.App.V3"); } catch { }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp(showEvent));
    }
}
