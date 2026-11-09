using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace TRamCash;

static class Program
{
    [DllImport("shell32.dll", SetLastError = true)]
    static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    const int SW_RESTORE = 9;
    const string MutexName = "Global\\TRamCash_SingleInstance_Mutex_998877";

    [STAThread]
    static void Main(string[] args)
    {
        // Komut satırından sessiz temizleme desteği (--clean veya -c)
        if (args.Length > 0 && (args[0] == "--clean" || args[0] == "-c"))
        {
            var settings = AppSettings.Load();
            MemoryCleaner.Clean(settings);
            return;
        }

        // 1'den fazla örneğin açılmasını engelleyen ve uyarı veren Mutex kontrolü
        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Zaten açık olan pencereyi öne getir
            try
            {
                IntPtr hWnd = FindWindow(null, "T-RamCash");
                if (hWnd != IntPtr.Zero)
                {
                    ShowWindow(hWnd, SW_RESTORE);
                    SetForegroundWindow(hWnd);
                }
            }
            catch { }

            // Kullanıcıya uyarı mesaj kutusunu göster
            MessageBox.Show(
                "T-RamCash zaten çalışıyor!\n\nUygulama arka planda ve sağ alttaki sistem tepsisinde (saatin yanında) etkindir.",
                "T-RamCash - Uyarı",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return; // İkinci örneği kapat
        }

        // Windows Görev Çubuğu kimliğini ata
        try
        {
            SetCurrentProcessExplicitAppUserModelID("TRamCash.App.V3");
        }
        catch { }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }    
}
