using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace TRamCash;

public class AppSettings
{
    public bool CleanProcessWorkingSets { get; set; } = true;
    public bool CleanSystemWorkingSet { get; set; } = true;
    public bool CleanModifiedPageList { get; set; } = true;
    public bool CleanStandbyList { get; set; } = true;
    public bool CleanStandbyLowPriority { get; set; } = true;

    public bool AutoCleanByRatio { get; set; } = false;
    public int AutoCleanRatioThreshold { get; set; } = 85;

    public bool AutoCleanByInterval { get; set; } = false;
    public int AutoCleanIntervalMinutes { get; set; } = 15;

    public bool AlwaysOnTop { get; set; } = false;
    public bool StartMinimized { get; set; } = false;
    public bool StartWithWindows { get; set; } = false;
    public bool ShowCleanNotification { get; set; } = true;
    public bool IsDarkThemeEnabled { get; set; } = true;

    public int WarningLevel { get; set; } = 60;
    public int DangerLevel { get; set; } = 80;

    static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "T-Desk", "T-RamCash");
    static readonly string ConfigPath = Path.Combine(ConfigDir, "settings.json");

    // Eski kalıntı yapılandırma (Geriye dönük uyumluluk ve otomatik taşıma)
    static readonly string LegacyConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Henry++", "Mem Reduct");
    static readonly string LegacyConfigPath = Path.Combine(LegacyConfigDir, "memreduct_clone.json");

    public static AppSettings Load()
    {
        try
        {
            // 1. Yeni güncel yapılandırma dosyasını kontrol et
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }

            // 2. Yeni dosya yoksa, eski yoldan ayarları otomatik taşı (Migration)
            if (File.Exists(LegacyConfigPath))
            {
                string json = File.ReadAllText(LegacyConfigPath);
                var legacySettings = JsonSerializer.Deserialize<AppSettings>(json);
                if (legacySettings != null)
                {
                    legacySettings.Save(); // Yeni yola kaydet ve Registry'i güncelle
                    return legacySettings;
                }
            }
        }
        catch
        {
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            if (!Directory.Exists(ConfigDir))
            {
                Directory.CreateDirectory(ConfigDir);
            }
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);

            // Startup registry check
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                string exePath = Environment.ProcessPath ?? "";
                if (StartWithWindows && !string.IsNullOrEmpty(exePath))
                {
                    key.SetValue("T-RamCash", $"\"{exePath}\"");
                    key.DeleteValue("MemReductClone", false); // Eski registry kaydını temizle
                }
                else
                {
                    key.DeleteValue("T-RamCash", false);
                    key.DeleteValue("MemReductClone", false);
                }
            }
        }
        catch
        {
        }
    }
}
