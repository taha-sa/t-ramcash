# T-RamCash 1.1.0 - Bellek Analizi (Faz 2)

> Bu belge **kuramsal** bir analizdir. GUI bu fazda calistirilmadi; rakamlar .NET/WinForms/GDI+ davranisindan
> turetilmis **tahminlerdir** ve Gorev Yoneticisi / VMMap / `GC.GetTotalMemory` ile dogrulanmalidir.
> "WS" = working set (fiziksel), "Private" = ozel commit.

## 1. Degisiklikler ve beklenen kazanc

| # | Degisiklik | Gerekce | Beklenen kazanc |
|---|---|---|---|
| 1 | **Gizlenince `MainForm` Dispose** (kalici cekirdek `TrayApp`'te) | Form + 640x265 cift tampon bitmap'i (~0,7 MB) + Region + pencere DC'si + denetim agaci serbest kalir; Settings/About zaten `using` ile kapaniyordu | ~1-3 MB yonetilen + ~10-20 GDI/USER tutamaci (tepside) |
| 2 | **UI 1 sn zamanlayicisi gizliyken tamamen durur** (form kapaninca zaten yok; kucultulunce de durur) | Onceden gizli form bile 1 Hz `RefreshData + Invalidate` yapiyordu: 86.400 uyanma/gun | Uyanma -%80 (5 sn arka plan kontrolu: 17.280/gun), gizliyken boyama = 0 |
| 3 | **Tepsi simgesi yalniz yuzde degisince yeniden cizilir**; Bitmap/Graphics/Font/StringFormat tek sefer olusur | Eskiden her saniye `Bitmap(32x32)` (4 KB) + `Graphics` + `Font` + `StringFormat` + `Icon.Clone` + string: tick basina ~10 KB, ~0,85 GB/gun cop | Gen0 GC sikligi dusuk; tepsi modunda tick basina ~0 allocation (`ReadSnapshot(ref)`, `Unsafe.SizeOf` ile `Marshal.SizeOf` yok) |
| 4 | **`Gdi` onbellegi**: Font/Brush/Pen/StringFormat/`GraphicsPath` (yuvarlak dikdortgen) form omru boyunca paylasilir, son form kapaninca topluca serbest | Eski `OnPaint` her karede ~14 `Font` + ~20 `SolidBrush/Pen` + `StringFormat`'lar uretiyordu (~35 nesne/saniye = ~3 M nesne/gun; her `Font` icin GDI+ FontFamily aramasi) | Boyama basina allocation ~0; CPU ve gen0 baskisi belirgin duser; tepsi modunda GDI+ nesnesi kalmaz |
| 5 | **Metin onbellegi** (`_sPct`, `_sUsed`...) yalniz veri degisince uretilir; veri ayniysa `Invalidate` yok | Her karede 6-7 `string.Format` ve gereksiz tam boyama | -6 string/kare; bosta tekrar boyama yok |
| 6 | **Logo bir kez bitmap'e cizilir** (20x20) | Eskiden her karede 4 `LinearGradientBrush` + 3 `GraphicsPath` | Kare basina ~7 agir GDI+ nesnesi daha az |
| 7 | **`Process.GetProcesses()` yerine Toolhelp32** + tum tutamaclar `try/finally` | ~300 `Process` nesnesi (+`ProcessName`, `ProcessInfo`) ~150 KB/temizlik; Toolhelp tek struct'i yeniden kullanir (~12 KB). Not: eski kodda kesin bir tutamac sizintisi tespit edilmedi; `OpenProcess` -> `CloseHandle` istisna guvenli hale getirildi, aksi halde her sizan tutamac ~ birkac yuz bayt kernel nesnesidir | ~140 KB/temizlik gecici cop; tutamac garantisi. `PROCESS_QUERY_LIMITED_INFORMATION` ile daha az ayricalik |
| 8 | **Kaynak uretimli (source-gen) System.Text.Json** (`SettingsJsonContext`) | Yansima tabanli serilestirici ilk cagrida tip meta verisi + IL uretimi icin ~1,5-3 MB + JIT maliyeti getirir | ~1-2 MB private, acilis suresi kisalir |
| 9 | **GC ayari**: `ServerGC=false`, `ConcurrentGC=false`, `GCConserveMemory=5`, `RetainVM=false`, `TieredPGO=true` | Es zamanli (background) GC ayri thread + ek veri yapilari acar (~0,3-1 MB); ConserveMemory=5 gen2 sikistirmayi yeglerek parcalanmayi azaltir; RetainVM=false bos segmentleri OS'ye iade eder | ~0,5-1,5 MB; yonetilen yigin ~%5-10 daha siki |
| 10 | **`TrimSelf()`**: form kapaninca (400 ms sonra), tepsi acilisinda ve tepside temizlik sonrasi: `GC.Collect(2, Forced, blocking, compacting)` + `LOH CompactOnce` + ikinci gecis + `SetProcessWorkingSetSizeEx(-1,-1)` | Yonetilen yigin kuculur, WS'deki ulasilabilir sayfalar standby'a gider | Tepside WS ~35-45 MB -> ~10-14 MB (asil kazanc) |
| 11 | `InvariantGlobalization` **kapali birakildi** | Arayuz Turkce kulturde ondalik bicimlendirir (`1,2 GB`); Invariant `1.2` yazdirir ve buyuk/kucuk harf (i/I) kurallarini bozar. Kazanc ~ICU 1-2 MB, ama dogruluk onceliklidir | 0 |
| 12 | `UseSystemResourceKeys` **kullanilmadi** | Yalniz trimmed/NativeAOT'ta anlamli; istisna iletilerini anahtara cevirir, kazanc < 100 KB | 0 |

Ek olarak: T-Link sunucusu (bir `Task` + bir named pipe, ~0,3-0,5 MB) ve 30 sn sonra tek seferlik guncelleme kontrolu (gecici `HttpClient`, ~2-3 MB, sonraki `TrimSelf` ile geri doner) eklendi; bunlar yukaridaki kazanctan dusulmustur.

## 2. Tahmini onceki / sonraki degerler

| Durum | Metrik | Onceki (1.0) | Sonraki (1.1.0) | Not |
|---|---|---|---|---|
| **UI acik** | WS | ~42-48 MB | ~38-43 MB | runtime + WinForms ortak sayfalari baskin; kazanc Gdi/JSON/GC'den |
| | Private | ~26-30 MB | ~22-26 MB | |
| | GDI+USER tutamaci | ~70-90 | ~55-70 | boyama gecici nesneleri yok |
| | Allocation | ~35 GDI nesnesi + ~10 string / sn | ~0 (veri degismezse boyama yok) | |
| **Tepsi** (pencere gizli) | WS | ~38-45 MB (form + zamanlayici yasiyor) | **~10-14 MB** (TrimSelf sonrasi; tick ile ~15-18 MB'a kadar sicrayip yine kuculur) | en buyuk kazanc |
| | Private | ~24-28 MB | ~7-10 MB | |
| | Tutamac | ~70-90 | ~25-35 | |
| | Uyanma/gun | 86.400 (1 Hz) + 21.600 (4 sn) | 17.280 (5 sn) | |
| | Tick basina allocation | ~10-12 KB | ~0 B (yuzde ayniysa) | |

## 3. Temizleyici (MemoryCleaner) yan etkileri
* Adim adim `GlobalMemoryStatusEx` olcumu (tek cagri, ~1 us) -> `CleanResult.PerStep`.
* Haric tutma: on plan sureci, kendi surec, T-Suite manifestindeki exe adlari/PID'ler, kullanici listesi, PID <= 4.
* Korumali surecler `OpenProcess` basarisizliginda sessizce atlanir.

## 4. Dogrulama onerisi (sonraki faz)
1. `Get-Process T-RamCash | select WS, PM, HandleCount` -> UI acik / gizli (60 sn sonra).
2. `dotnet-counters` ile `gc-heap-size`, `gen-0-gc-count`: tepside 10 dk'da 0-1 gen0 GC beklenir.
3. VMMap: Managed Heap + Shareable WS karsilastirmasi.
