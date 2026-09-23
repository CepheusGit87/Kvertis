# 04 – Bibliotheken

Jede Bibliothek, jedes NuGet-Paket und jede native Binärdatei steht hier, **bevor** sie gemergt wird. Versionen werden beim Einbau eingetragen und bei Updates nachgezogen. Status: `geplant` (noch nicht im Code), `geprüft` (durch `lizenz-waechter` freigegeben), `gesperrt`.

Einbindung: `NuGet` (verwaltete DLL), `native` (native DLL im Paket), `Prozess` (separates Programm), `System` (Bestandteil von Windows, wird nicht mitgeliefert).

## Produktiv

| Bibliothek | Version | Lizenz | Zweck | Einbindung | Status |
|---|---|---|---|---|---|
| Microsoft.WindowsAppSDK (WinUI 3) | — | Microsoft Software License Terms (Quellcode MIT) | UI-Framework, MSIX | NuGet | geplant |
| Microsoft.Windows.SDK.BuildTools | — | Microsoft Software License Terms | Build der Windows-App | NuGet (nur Build) | geplant |
| CommunityToolkit.Mvvm | — | MIT | MVVM (ObservableObject, RelayCommand) | NuGet | geplant |
| CommunityToolkit.WinUI.Controls.* | — | MIT | SettingsCard u. a. Steuerelemente | NuGet | geplant |
| Microsoft.Extensions.DependencyInjection | — | MIT | Dependency Injection | NuGet | geplant |
| Microsoft.Extensions.Logging | — | MIT | Lokales Logging (opt-in, nie versendet) | NuGet | geplant |
| Magick.NET-Q16-AnyCPU + Magick.NET.Core | — | Apache 2.0 (Wrapper), ImageMagick License (nativ) | Bilder lesen, wandeln, komprimieren, Metadaten entfernen | NuGet + native | geplant, **Prüfung der mitgelieferten Drittbibliotheken in `Magick.Native` offen (Verdacht: HEVC-Komponenten, x265 wäre GPL)** |
| FFMpegCore | — | MIT | Ansteuerung von ffmpeg/ffprobe als Prozess | NuGet | geplant |
| FFmpeg (LGPL-Build) | — | LGPL 2.1 | Audio/Video-Konvertierung | Prozess (`ffmpeg.exe`, `ffprobe.exe`) | geplant, Build-Konfiguration siehe `02-rechtssicherheit.md` §2, Bezugsquelle offener Punkt |
| NAudio | — | MIT | Audio-Analyse (Pegel, Wellenform für Vorschau) | NuGet | geplant |
| PdfPig (UglyToad.PdfPig) | — | Apache 2.0 | PDF lesen: Text, Seitenzahl, Schutz erkennen | NuGet | geplant |
| PDFsharp | — | MIT | PDF erzeugen: Text/Markdown → PDF, Bilder → PDF | NuGet | geplant (ersetzt QuestPDF, siehe unten) |
| DocumentFormat.OpenXml | — | MIT | DOCX/XLSX/PPTX lesen (Text, Tabellen) | NuGet | geplant |
| System.Text.Json | (in .NET 8) | MIT | Einstellungen, Verlauf, Tempo-Profil | Framework | geplant |
| Media Foundation | (Windows) | System | Encoder H.264/HEVC/AAC/MP3 über FFmpeg `*_mf` | System | geplant |
| Windows Imaging Component + HEIF-Bilderweiterung | (Windows) | System | HEIC/HEIF dekodieren | System | geplant |
| Windows.Data.Pdf | (Windows) | System | PDF-Seiten rendern (PDF → Bilder, Vorschau) | System | geplant |
| Windows.Services.Store | (Windows) | System | In-App-Kauf „Kvertis Pro“ | System | geplant |
| Fluent UI System Icons | — | MIT | Icons, die nicht in Segoe Fluent Icons enthalten sind | Assets | geplant |
| Segoe UI Variable, Segoe Fluent Icons | (Windows) | System | Schrift und Standard-Icons | System, nicht mitgeliefert | geplant |

## Nur Test und Build

| Bibliothek | Version | Lizenz | Zweck | Einbindung | Status |
|---|---|---|---|---|---|
| xunit, xunit.runner.visualstudio | — | Apache 2.0 | Testframework | NuGet | geplant |
| NSubstitute | — | BSD-3 | Mocks (`IProcessRunner` u. a.) | NuGet | geplant |
| Shouldly | — | BSD-3 | Assertions | NuGet | geplant |
| coverlet.collector | — | MIT | Testabdeckung | NuGet | geplant |
| Microsoft.NET.Test.Sdk | — | MIT | Test-Host | NuGet | geplant |

## Abgelehnt oder gesperrt

| Bibliothek | Grund | Alternative |
|---|---|---|
| QuestPDF | Community-Lizenz nur bis 1 Mio. USD Jahresumsatz, sonst kostenpflichtig. Ohne schriftliche Freigabe des Projektinhabers nicht erlaubt (`02-rechtssicherheit.md` §1). | PDFsharp (MIT) |
| FluentAssertions ≥ 8 | Kommerzielle Nutzung lizenzpflichtig. | Shouldly (BSD-3) oder xUnit-Asserts |
| libx264, libx265, libfdk-aac, libxvid | GPL bzw. non-free. | Media-Foundation-Encoder, libvpx, libaom/SVT-AV1, libopus |
| FFmpeg.AutoGen (P/Invoke auf FFmpeg-DLLs) | Verlinkung statt Prozessgrenze; LGPL-Auflagen schwerer zu erfüllen. | FFMpegCore + Prozess |
| libde265, libheif mit x265 | HEVC-Patente, x265 ist GPL. | Windows Imaging Component |
| OpenH264 | Patentlizenz gilt nur für den von Cisco verteilten Binär-Download; nicht kontrollierbar im MSIX. | `h264_mf` |

## Regeln

- Neue Zeile hier **und** Eintrag im [CHANGELOG](CHANGELOG.md), sonst kein Merge.
- Lizenztexte liegen unter `third_party/<name>/LICENSE`. Die Third-Party-Licenses-Seite der App wird daraus erzeugt.
- Versionen werden über `Directory.Packages.props` zentral festgelegt.
