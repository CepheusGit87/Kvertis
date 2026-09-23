# 04 – Bibliotheken

Jede Bibliothek, jedes NuGet-Paket und jede native Binärdatei steht hier, **bevor** sie gemergt wird. Versionen werden beim Einbau eingetragen und bei Updates nachgezogen. Status: `geplant` (noch nicht im Code), `geprüft` (durch `lizenz-waechter` freigegeben), `gesperrt`.

Einbindung: `NuGet` (verwaltete DLL), `native` (native DLL im Paket), `Prozess` (separates Programm), `System` (Bestandteil von Windows, wird nicht mitgeliefert).

## Produktiv

| Bibliothek | Version | Lizenz | Zweck | Einbindung | Status |
|---|---|---|---|---|---|
| Microsoft.WindowsAppSDK (WinUI 3) | 2.5.1 | Microsoft Software License Terms (Quellcode MIT) | UI-Framework, MSIX | NuGet | im Code |
| Microsoft.Windows.SDK.BuildTools | 10.0.28000.2705 | Microsoft Software License Terms | Build der Windows-App | NuGet (nur Build) | im Code |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | MVVM (ObservableObject, RelayCommand) | NuGet | im Code |
| CommunityToolkit.WinUI.Controls.SettingsControls | 8.2.251219 | MIT | SettingsCard, SettingsExpander | NuGet | im Code |
| Microsoft.Extensions.DependencyInjection | 8.0.1 | MIT | Dependency Injection (nur App) | NuGet | im Code |
| Microsoft.Extensions.Logging | 8.0.1 | MIT | Lokales Logging (opt-in, nie versendet) | NuGet | im Code |
| Microsoft.Extensions.Logging.Abstractions | 8.0.3 | MIT | `ILogger` in Engine und Queue | NuGet | im Code |
| Markdig | 1.4.0 | BSD-2 | Markdown → HTML/Text/PDF-Blockmodell | NuGet | im Code |
| Magick.NET-Q16-AnyCPU + Magick.NET.Core | 14.17.1 | Apache 2.0 (Wrapper), ImageMagick License (nativ) | Bilder lesen, wandeln, komprimieren, Metadaten entfernen | NuGet + native (`Magick.Native-Q16-<arch>.dll`) | **entfernt 2026-09-23** (Entscheidung Projektinhaber: null Patentrisiko; nativ enthaltene libde265/openh264, siehe Prüfbericht unten) |
| SkiaSharp | 4.152.1 | MIT | Bilder lesen (JPG, PNG, WebP, GIF, BMP, ICO, DNG), skalieren, JPG/PNG/WebP schreiben, Bild → PDF, PDF-Seite → JPG | NuGet | im Code seit 2026-09-23 (ersetzt Magick.NET), durch lizenz-waechter zu prüfen |
| SkiaSharp.NativeAssets.Win32 | 4.152.1 | MIT (nativ: Skia BSD-3; enthält libjpeg-turbo, libpng, libwebp, zlib, FreeType, HarfBuzz, Expat, ICU, piex, DNG SDK, Wuffs – alle permissiv, keine Video-Codecs) | Native `libSkiaSharp.dll` für Windows | NuGet + native | im Code seit 2026-09-23, Lizenztexte in `third_party/SkiaSharp/` (LICENSE.txt, THIRD-PARTY-NOTICES.txt), durch lizenz-waechter zu prüfen |
| FFMpegCore | 5.5.0 | MIT | Ansteuerung von ffmpeg/ffprobe als Prozess | NuGet | im Code |
| FFmpeg (LGPL-Build) | — | LGPL 2.1 | Audio/Video-Konvertierung | Prozess (`ffmpeg.exe`, `ffprobe.exe`) | geplant, Build-Konfiguration siehe `02-rechtssicherheit.md` §2, Bezugsquelle offener Punkt |
| PdfPig (UglyToad.PdfPig) | 0.1.16 | Apache 2.0 | PDF lesen: Text, Seitenzahl, Schutz erkennen | NuGet | im Code |
| PDFsharp | 6.2.4 | MIT | PDF erzeugen: Text/Markdown → PDF, Bilder → PDF | NuGet | geplant (ersetzt QuestPDF, siehe unten) |
| DocumentFormat.OpenXml | 3.5.1 | MIT | DOCX/XLSX/PPTX lesen (Text, Tabellen) | NuGet | im Code |
| System.Text.Json | (in .NET 8) | MIT | Einstellungen, Verlauf, Tempo-Profil | Framework | im Code |
| Media Foundation | (Windows) | System | Encoder H.264/HEVC/AAC/MP3 über FFmpeg `*_mf` | System | im Code |
| Windows Imaging Component + HEIF-Bilderweiterung | (Windows) | System | HEIC/HEIF dekodieren | System | im Code |
| Windows.Data.Pdf | (Windows) | System | PDF-Seiten rendern (PDF → Bilder, Vorschau) | System | im Code |
| Windows.Services.Store | (Windows) | System | In-App-Kauf „Kvertis Pro“ | System | im Code |
| Fluent UI System Icons | — | MIT | Icons, die nicht in Segoe Fluent Icons enthalten sind | Assets | noch nicht genutzt (nur Segoe Fluent Icons) |
| Segoe UI Variable, Segoe Fluent Icons | (Windows) | System | Schrift und Standard-Icons | System, nicht mitgeliefert | geplant |

## Nur Test und Build

| Bibliothek | Version | Lizenz | Zweck | Einbindung | Status |
|---|---|---|---|---|---|
| xunit 2.9.3, xunit.runner.visualstudio 3.1.5 | s. links | Apache 2.0 | Testframework | NuGet | im Code |
| NSubstitute | 5.3.0 | BSD-3 | Mocks (`IProcessRunner` u. a.) | NuGet | im Code |
| Shouldly | 4.3.0 | BSD-3 | Assertions | NuGet | im Code |
| coverlet.collector | 6.0.4 | MIT | Testabdeckung | NuGet | im Code |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | Test-Host | NuGet | im Code |
| SkiaSharp.NativeAssets.Linux.NoDependencies | 4.152.1 | MIT (nativ wie oben) | Native Skia-Bibliothek, damit die Engine-Tests unter Linux laufen; wird nie mit der App ausgeliefert | NuGet (nur Testprojekt) | im Code seit 2026-09-23, durch lizenz-waechter zu prüfen |

## Prüfbericht Magick.Native (2026-09-23, lizenz-waechter)

Untersucht: `Magick.NET-Q16-AnyCPU 14.17.1`, Datei `runtimes/win-x64/native/Magick.Native-Q16-x64.dll` (24 MB) und die mitgelieferte `Notice.txt`. Die Notice listet alle statisch eingebundenen Bibliotheken:

| Komponente | Lizenz | Bewertung |
|---|---|---|
| ImageMagick, libjpeg-turbo, libpng, libwebp, libtiff, zlib, lcms, openjpeg, openjph, libjxl, brotli, libhwy, aom, freetype, harfbuzz, pixman, libxml2, libzip, liblzma, bzip2, openexr, imath, libffi, fribidi, libraqm | permissiv (Apache/BSD/MIT/zlib/ImageMagick) | OK |
| glib, pango, gdk-pixbuf, cairo, librsvg, libcroco, liblqr, libraw, fontconfig | LGPL (bzw. MPL/CDDL-Dual) | OK unter der Bedingung, dass `Magick.Native-*.dll` als eigenständige, austauschbare DLL ausgeliefert wird und Magick.NET die Build-Skripte veröffentlicht (Relink-Möglichkeit). Lizenztexte aus `Notice.txt` in die App übernehmen. |
| **libde265 1.1.1** | LGPL 3 | **HEVC-Software-Decoder wird mitgeliefert**, auch wenn Kvertis ihn nie aufruft. Widerspricht ADR-003/ADR-006 (keine mitgelieferten HEVC-Decoder, Patentlage). |
| **libheif 1.23.2** | LGPL 3 | HEIF-Container, ohne Decoder unkritisch, hängt aber an libde265. |
| **openh264 2.6.0** | BSD-2 | H.264-Codec; die Patentlizenz gilt nur für die von Cisco verteilten Binärdateien, nicht für diese Einbettung. |
| x265 | GPL | **nicht enthalten** (Suche nach `x265` in der DLL ohne Treffer). |

Folgerung: Kein GPL-Verstoß. Das Patentrisiko liegt in den mitgelieferten Codecs libde265 und openh264. Entscheidung O-01 (Projektinhaber): (a) Risiko akzeptieren und dokumentieren, oder (b) Bildpfad auf eine Bibliothek ohne Video-Codecs umstellen (Kandidat: SkiaSharp, MIT, plus Windows Imaging Component für HEIC/RAW/TIFF-Mehrseitig). Bis zur Entscheidung bleibt Magick.NET im Code, HEIC läuft ausschließlich über WIC.

## Prüfbericht SkiaSharp (2026-09-23, lizenz-waechter)

Untersucht: `SkiaSharp.NativeAssets.Win32 4.152.1`, `runtimes/win-x64/native/libSkiaSharp.dll` (13 MB), `LICENSE.txt` (MIT) und `THIRD-PARTY-NOTICES.txt`. Enthalten: skia (BSD-3), libjpeg-turbo (IJG/BSD), libpng (zlib/libpng), libwebp (BSD), zlib, freetype (FTL), harfbuzz (MIT), expat (MIT), ICU (Unicode), piex (Apache 2.0), DNG SDK (Adobe, lizenzfrei), wuffs (Apache 2.0), sfntly, ANGLE, SPIR-V, etc1, jsoncpp, imgui, sdl (alle permissiv). Der GIF-Decoder stammt aus mozilla.org-Code unter **Tri-Lizenz MPL 1.1 / GPL 2.0 / LGPL 2.1**; Kvertis nutzt ihn unter der **MPL 1.1** (dateiweise Copyleft, keine Änderung, Lizenztext liegt bei). Zeichenkettensuche in der DLL: keine Spur von libde265, x265, openh264, libheif, dav1d, aom oder anderen Video-Codecs. **Urteil: OK.**

## Abgelehnt oder gesperrt

| Bibliothek | Grund | Alternative |
|---|---|---|
| QuestPDF | Community-Lizenz nur bis 1 Mio. USD Jahresumsatz, sonst kostenpflichtig. Ohne schriftliche Freigabe des Projektinhabers nicht erlaubt (`02-rechtssicherheit.md` §1). | PDFsharp (MIT) |
| FluentAssertions ≥ 8 | Kommerzielle Nutzung lizenzpflichtig. | Shouldly (BSD-3) oder xUnit-Asserts |
| libx264, libx265, libfdk-aac, libxvid | GPL bzw. non-free. | Media-Foundation-Encoder, libvpx, libaom/SVT-AV1, libopus |
| FFmpeg.AutoGen (P/Invoke auf FFmpeg-DLLs) | Verlinkung statt Prozessgrenze; LGPL-Auflagen schwerer zu erfüllen. | FFMpegCore + Prozess |
| NAudio | Nicht nötig: Wellenform und Pegel liefert ffmpeg (`-af astats`, PCM-Export). Eine Abhängigkeit weniger. | ffmpeg |
| libde265, libheif (als eigene Abhängigkeit) | HEVC-Patente. Hinweis: in Magick.Native dennoch enthalten, siehe Prüfbericht oben. | Windows Imaging Component |
| OpenH264 | Patentlizenz gilt nur für den von Cisco verteilten Binär-Download; nicht kontrollierbar im MSIX. | `h264_mf` |

## Regeln

- Neue Zeile hier **und** Eintrag im [CHANGELOG](CHANGELOG.md), sonst kein Merge.
- Lizenztexte liegen unter `third_party/<name>/LICENSE`. Die Third-Party-Licenses-Seite der App wird daraus erzeugt.
- Versionen werden über `Directory.Packages.props` zentral festgelegt.
