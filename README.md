# T-RamCash

Windows bellek temizleyici / memory cleaner (C# WinForms, .NET 10). Surum 1.2.0.

## Derleme / Build

T-Suite SDK'ya (`TSuite.Link`, `TSuite.Update`, `TSuite.Updater`) **ProjectReference** ile bagimlidir. SDK deposu
(`t-suite`) bu klasorun yanindaki dizinde bulunmalidir; varsayilan yol csproj icinde su sekilde cozulur:

```
$(MSBuildProjectDirectory)\..\..\..\..\Projects\t-suite      ->  C:\Users\hp\Projects\t-suite
```

Baska bir konumdaysa: `dotnet build -p:TSuiteRoot=D:\yol\t-suite`.

```
dotnet build -c Release
```

Derleme `TSuite.Updater.*` dosyalarini cikti klasorune (exe'nin yanina) kopyalar.

## Surum yayini
`release.ps1 <surum> "notlar"` yerel build + SHA-256 + ECDSA imza + `latest.json` uretir; `-Publish` verilmedikce yayinlamaz.
Ozel anahtar: `C:\Users\hp\.t-suite\keys\t-ramcash.pem` (repo disinda).

## CI / Yayın
`main` dahil tüm dallara push ve pull request'lerde CI çalışır. Kararlı sürüm yayımlamak için `git tag v1.2.0 && git push --tags` kullanın. Beta sürümleri `v1.2.0-beta.1` gibi etiketleyip aynı şekilde gönderin; beta yayımları ön sürüm olarak işaretlenir. Release işi, imzalama ve yayımdan önce `release` ortamındaki onayı bekler.

## Ozellikler
Akilli / Tam temizlik, haric tutma listesi, otomatik temizlik (daima Akilli), T-Link (`ram.status`, `ram.cleaned`,
`ram.clean`, `app.show`, `app.version`), T-Update. Bellek analizi: `docs/bellek-analizi.md`.
Not: `ram.status.standbyMb` alanı gerçekte `SystemCacheBytes` (sistem önbelleği, MB) değerini taşır; ad sözleşme gereği değiştirilmedi.
# Localization

The UI uses `Strings.resx` (neutral English), `Strings.tr.resx` and `Strings.de.resx`,
accessed through the strongly typed `Strings` class. Keep keys and format placeholders
identical. Run `powershell -NoProfile -File scripts/Check-Resources.ps1`; the same check
runs before Windows builds and in CI. Run `dotnet run --project tests/TRamCash.UiChecks -c Release`
for resource loading, English fallback, regional formatting, label sizing at 96 DPI,
and update dialog closing checks. These checks construct forms without starting
the tray app, cleaning memory, changing user settings, or downloading updates.
Language is saved in `settings.json` as `Language` (`""`, `tr`, `en`, `de`). Restart
T-RamCash after changing it. System language follows Windows; unsupported languages
fall back to English resources while retaining system number formatting.
The About screen always offers phone monitoring. If T-Bridge is missing, its button
opens a contextual invitation; closing it dismisses only that instance. Installation
is rechecked via the T-Suite registry and standard installation paths.
Localized report/update views use SDK reporting and update APIs without modifying the SDK.
About → Legal includes the embedded MIT license, TR/EN/DE privacy policy and
open-source notices. These documents also ship beside the application.

