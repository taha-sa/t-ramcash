using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace TRamCash;

public class AppSettings
{
    public string Language { get; set; } = "";
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
    public bool StartWithWindows { get; set; } = true;
    public bool ShowCleanNotification { get; set; } = true;
    /// <summary>Otomatik temizlikte balon (varsayilan sessiz); elle temizlikte ShowCleanNotification gecerli.</summary>
    public bool AutoCleanNotification { get; set; } = false;

    /// <summary>Temizlikten haric tutulan surec adlari (.exe'siz).</summary>
    public List<string> ExcludedProcesses { get; set; } = new();

    /// <summary>Elle temizlik modu: "Smart" | "Full". Otomatik temizlik her zaman Akilli.</summary>
    public string ManualMode { get; set; } = "Smart";


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
                return Normalize(JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings) ?? new AppSettings());
            }

            // 2. Yeni dosya yoksa, eski yoldan ayarları otomatik taşı (Migration)
            if (File.Exists(LegacyConfigPath))
            {
                string json = File.ReadAllText(LegacyConfigPath);
                var legacySettings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings);
                if (legacySettings != null)
                {
                    Normalize(legacySettings);
                    legacySettings.Save(); // Yeni yola kaydet ve Registry'i güncelle
                    return legacySettings;
                }
            }
        }
        catch
        {
            try { File.Copy(ConfigPath, ConfigPath + ".bad", true); } catch { }
        }
        return new AppSettings();
    }

    static readonly object _saveLock = new();

    static AppSettings Normalize(AppSettings s)
    {
        s.Language ??= "";
        s.ManualMode = s.ManualMode == "Full" ? "Full" : "Smart";
        s.ExcludedProcesses ??= new();
        s.AutoCleanRatioThreshold = Math.Clamp(s.AutoCleanRatioThreshold, 10, 99);
        s.AutoCleanIntervalMinutes = Math.Clamp(s.AutoCleanIntervalMinutes, 1, 360);
        return s;
    }

    public void Save()
    {
        lock (_saveLock)
        {
            string tmp = null;
            try
            {
                if (!Directory.Exists(ConfigDir))
                {
                    Directory.CreateDirectory(ConfigDir);
                }
                string json = JsonSerializer.Serialize(this, SettingsJsonContext.Default.AppSettings);
                tmp = Path.Combine(ConfigDir, $"settings.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
                File.WriteAllText(tmp, json);
                File.Move(tmp, ConfigPath, true);
                tmp = null;
            }
            catch
            {
                if (tmp != null) { try { File.Delete(tmp); } catch { } }
            }
        }
        ApplyStartupRegistration();
    }

    public void ApplyStartupRegistration()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (key != null)
            {
                string exePath = Environment.ProcessPath ?? "";
                if (StartWithWindows && !string.IsNullOrEmpty(exePath))
                {
                    key.SetValue("T-RamCash", $"\"{exePath}\" --tray", RegistryValueKind.String);
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

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext { }
