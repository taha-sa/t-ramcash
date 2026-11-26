using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;
using TRamCash;
using TSuite.Update;

static class Checks
{
    const BindingFlags Internal = BindingFlags.Static | BindingFlags.NonPublic;
    static readonly Assembly App = typeof(AboutForm).Assembly;
    static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    [STAThread]
    static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-GB");
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
        var apply = App.GetType("TRamCash.Localization").GetMethod("Apply", Internal);
        apply.Invoke(null, new object[] { "" });
        var diagnostics = typeof(TSuite.Diagnostics.CrashReporter).Assembly;
        var session = Activator.CreateInstance(diagnostics.GetType("TSuite.Diagnostics.ReporterSession"), BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { "ramcash-checks", "Checks", "1.0.0", "taha-sa/t-ramcash-releases", Path.Combine(AppContext.BaseDirectory, "test-data") }, null);
        typeof(TSuite.Diagnostics.CrashReporter).GetProperty("Current", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, session);
        var strings = new ResourceManager("TRamCash.Strings", App);
        foreach (var language in new[] { "tr", "en", "de", "fr" })
        {
            apply.Invoke(null, new object[] { language });
            var expected = language == "fr" ? "Settings" : language == "tr" ? "Ayarlar" : language == "de" ? "Einstellungen" : "Settings";
            // Unsupported languages are tested through ResourceManager directly; system mode restores startup culture.
            if (strings.GetString("Settings", CultureInfo.GetCultureInfo(language)) != expected) throw new Exception("Resource fallback: " + language);
            if (language == "fr")
            {
                if (CultureInfo.CurrentUICulture.Name != "en-GB" || CultureInfo.CurrentCulture.Name != "tr-TR")
                    throw new Exception("System mode must preserve the original UI language and regional formatting.");
                continue;
            }
            if (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != language)
                throw new Exception("Selected language not applied: " + language);
            if (1.5.ToString("F1") != (language == "en" ? "1.5" : "1,5"))
                throw new Exception("Selected formatting not applied: " + language);
            foreach (var property in App.GetType("TRamCash.Strings").GetProperties(Internal))
                if (property.PropertyType == typeof(string) && string.IsNullOrWhiteSpace((string)property.GetValue(null)))
                    throw new Exception("Empty resource: " + property.Name);
            foreach (Form form in new Form[] { new SettingsForm(new AppSettings()), new AboutForm(), new ProcessPickerForm(new()) })
            {
                using (form)
                {
                    form.CreateControl();
                    form.PerformLayout();
                    foreach (var item in Descendants(form).Where(c => c is Label && !c.AutoSize && c.Text.Length > 0))
                    {
                        var size = TextRenderer.MeasureText(item.Text, item.Font, new Size(item.Width, int.MaxValue), TextFormatFlags.WordBreak);
                        if (size.Height > item.Height) throw new Exception($"Clipped label {language} {form.GetType().Name}: {item.Text} ({size.Height}>{item.Height})");
                    }
                }
            }
            Console.WriteLine("PASS language " + language);
        }
        foreach (bool required in new[] { false, true })
        foreach (bool busy in new[] { false, true })
        foreach (var reason in new[] { CloseReason.UserClosing, CloseReason.ApplicationExitCall, CloseReason.WindowsShutDown })
        {
            var controller = new UpdateController(new UpdateChecker(new UpdateOptions { AppId = "ramcash-test", CurrentVersion = "1.0.0" }));
            typeof(UpdateController).GetProperty("Info").SetValue(controller,
                new UpdateInfo("1.0.0", "2.0.0", "", null, new LatestManifest { Signature = "test", MinVersion = required ? "2.0.0" : null }));
            typeof(UpdateController).GetField("_busy", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, busy ? 1 : 0);
            typeof(UpdateController).GetProperty("State").SetValue(controller, busy ? UpdateState.Launching : UpdateState.Available);
            using var form = (Form)Activator.CreateInstance(App.GetType("TRamCash.LocalUpdateDialog"), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { controller }, null);
            var e = new FormClosingEventArgs(reason, false);
            typeof(Form).GetMethod("OnFormClosing", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { e });
            bool expected = reason == CloseReason.UserClosing && (required || busy);
            if (e.Cancel != expected) throw new Exception($"Closing required={required} busy={busy} reason={reason}: cancelled={e.Cancel}, expected={expected}");
        }
        Console.WriteLine("PASS update closing matrix");
        using var legal = (Form)Activator.CreateInstance(App.GetType("TRamCash.LegalDialog"), true);
        if (Descendants(legal).OfType<TabPage>().Count() != 3 || Descendants(legal).OfType<TextBox>().Any(t => t.Text.Length < 100))
            throw new Exception("Legal documents are missing or empty.");
        Console.WriteLine("PASS embedded legal documents");
    }
}
