# T-RamCash

Windows bellek temizleyici / memory cleaner (C# WinForms, .NET 10). Surum 1.1.0.

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

## Ozellikler
Akilli / Tam temizlik, haric tutma listesi, otomatik temizlik (daima Akilli), T-Link (`ram.status`, `ram.cleaned`,
`ram.clean`, `app.show`, `app.version`), T-Update. Bellek analizi: `docs/bellek-analizi.md`.
