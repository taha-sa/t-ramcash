using System.Globalization;

namespace TRamCash;

internal static class Localization
{
    static readonly CultureInfo SystemUI = CultureInfo.CurrentUICulture;
    static readonly CultureInfo SystemFormatting = CultureInfo.CurrentCulture;
    internal static readonly string[] Languages = { "", "tr", "en", "de" };

    internal static void Apply(string language)
    {
        var culture = language switch
        {
            "tr" => CultureInfo.GetCultureInfo("tr-TR"),
            "de" => CultureInfo.GetCultureInfo("de-DE"),
            "en" => CultureInfo.GetCultureInfo("en-US"),
            _ => SystemUI
        };
        var formatting = Languages.Skip(1).Contains(language) ? culture : SystemFormatting;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = formatting;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture = culture;
    }

    internal static string Percent(uint value) => (value / 100.0).ToString("P0");
}
