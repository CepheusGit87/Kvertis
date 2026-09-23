# Changelog

Format: Datum, Änderung, neue oder entfernte Bibliotheken mit Lizenz. Neueste Einträge oben.

## 2026-09-23 – Ideenliste, Verkäuferpflichten, verworfene Funktionen

**Geändert (nur Doku)**

- `09-roadmap.md`: neue Liste „Ideen (geprüft, rechtlich unbedenklich)“ mit Prioritäten (u. a. PDFs zusammenfügen/aufteilen, Bilder → GIF, GIF → Einzelbilder, vCard/iCalendar ↔ CSV/XLSX, Untertitel, OpenDocument/EPUB → Text, 3D-Modelle in offenen Formaten); neue offene Punkte O-17 (Pflichten als Verkäufer), O-18 (E-Rechnung zurückgestellt), O-19 (ADR Sammel-Jobs), O-20 (FFmpeg-Filter für GIF); verworfen: Transkription, RAR/CBR, MOBI/AZW, PDF/A, FBX/USDZ, MIDI → Audio.
- `10-rechtsmatrix.md`: „Bewusst nicht enthalten“ und offene Punkte ergänzt.
- `02-rechtssicherheit.md`: neuer Abschnitt 10 „Pflichten als Verkäufer“.

**Bibliotheken**

- Keine Änderung.

## 2026-09-23 – Rechtsrahmen ohne Patentrisiko

**Geändert**

- Bilder: Magick.NET entfernt; SkiaSharp (MIT, DLL geprüft) plus Windows Imaging Component (`ISystemImageCodec`, `WicImageCodec`) für HEIC, AVIF, RAW, TIFF und die TIFF/BMP/GIF-Encoder; eigener ICO-Writer; EXIF-Übernahme nur JPEG → JPG/WebP; PSD und SVG entfallen (ADR-014).
- Audio/Video: eigener FFmpeg-Allowlist-Build ohne patentbelastete Decoder/Encoder und ohne Netzwerkprotokolle (`tools/ffmpeg/configure-allowlist.txt`, Build-Workflow, erweiterte Prüfskripte); `FfmpegCompliance` verweigert fremde Builds (Decoder, Protokolle); Routing nach Stream-Codec (`EncumberedCodecs`): H.264/HEVC/AAC/MPEG-4/WMV/WMA-Eingaben laufen nur über `MediaFoundationTranscoder` (Windows), nie über ffmpeg; `HevcFallbackGuard` entfernt (ADR-015).
- Formatmatrix: patentbelastete Familien → MP4/M4A/MP3/WAV/FLAC; MKV je nach Codec (`IConverterResolver.CanConvert`); FLV entfällt; MP4 → MP4 ist Standardvorschlag bei Video.
- Neu: `docs/10-rechtsmatrix.md` (Konverter → Bibliothek → Lizenz → Patentlage → Begründung → Nachweis); Compliance-Gate prüft, dass jeder `IConverter` dort steht.
- App: Ausgabeliste über `CanConvert` gefiltert; Analyzer-Hinweise CA1822/CA1859/CA1873 als Vorschläge (Windows-Build war daran gescheitert).
- Vorschau der Oberfläche als HTML-Attrappe (nicht im Repository).

**Bibliotheken**

- Neu: SkiaSharp 4.152.1 (MIT), SkiaSharp.NativeAssets.Win32 4.152.1 (MIT; enthält skia, libjpeg-turbo, libpng, libwebp, zlib, freetype, harfbuzz, expat, ICU, piex, DNG SDK, wuffs; GIF-Decoder unter MPL 1.1), SkiaSharp.NativeAssets.Linux.NoDependencies 4.152.1 (MIT, nur Tests).
- Entfernt: Magick.NET-Q16-AnyCPU, Magick.NET.Core (Magick.Native enthält libde265 und openh264).

## 2026-09-23 – Grundgerüst und Phase-1-Funktionen

**Geändert**

- Solution mit `Kvertis.Engine`, `Kvertis.Engine.Windows`, `Kvertis.Queue`, `Kvertis.App` und zwei Testprojekten; zentrale Paketversionen; Analyzer mit Warnungen als Fehler.
- Engine: Formaterkennung per Magic Bytes, Eingabeprüfung, Prozess-Runner (Timeout, Abbruch, Suspend-bewusst), Namensmuster, atomare Ausgabe, Schätzung mit Tempo-Profil; Konverter für Bilder (Magick.NET), Audio/Video (ffmpeg-Prozess, MF-Encoder, HEVC-Fallback-Wächter), Dokumente (PDF → Text/Bilder, Office → Text/CSV/Markdown/HTML, Text/Markdown → PDF/HTML, Bilder → PDF); ImageMagick-Sicherheitsrichtlinie; AVIF gesperrt (ADR-013).
- Engine.Windows: Media-Foundation-Codec-Prüfung, HEIC über WIC, Prozess-Pause, PDF-Rasterung über Windows.Data.Pdf.
- Queue: Warteschlange mit Parallelitätsgrenzen, Pause/Fortsetzen/Abbruch, Gesamtfortschritt, Verlauf (JSON, 200 Einträge), Tempo-Profil-Speicherung, Zulassungsregel (Freemium-Haken).
- App (WinUI 3, noch nicht unter Windows gebaut): Hauptansicht mit Ablagefläche, Job-Karten, „Mehr“-Panel, Vorschau, Verlauf, Einstellungen, Pro-Seite, Lizenzseite; 302 Ressourcen-Schlüssel je Sprache (DE/EN); Manifest nur mit `runFullTrust`.
- CI (Linux: Engine/Queue/Tests; Windows: App), Compliance-Gate (`tools/compliance/check.sh`, `check-resw.py`), FFmpeg-Prüfskripte, Lizenztexte unter `third_party/`.
- Code-Review durch `reviewer` (Fable): 21 Befunde, alle behoben (u. a. HEVC-Software-Fallback, AVIF-Umgehung, PID-Wiederverwendung beim Pausieren, Timeout während Suspend, MP3/TS-Fehlerkennungen, Windows-Gerätenamen, Überschreiben beim Commit).

**Bibliotheken (jetzt im Code, alle bereits in `04-bibliotheken.md`)**

- Magick.NET-Q16-AnyCPU 14.17.1 (Apache 2.0 / ImageMagick License; Prüfbericht zu `Magick.Native` in `04-bibliotheken.md`, O-01 offen)
- FFMpegCore 5.5.0 (MIT), PdfPig 0.1.16 (Apache 2.0), PDFsharp 6.2.4 (MIT), DocumentFormat.OpenXml 3.5.1 (MIT), Markdig 1.4.0 (BSD-2), Microsoft.Extensions.Logging.Abstractions 8.0.3 (MIT)
- App: Microsoft.WindowsAppSDK 2.5.1, Microsoft.Windows.SDK.BuildTools 10.0.28000.2705, CommunityToolkit.Mvvm 8.4.2 (MIT), CommunityToolkit.WinUI.Controls.SettingsControls 8.2.251219 (MIT), Microsoft.Extensions.DependencyInjection 8.0.1 (MIT), Microsoft.Extensions.Logging 8.0.1 (MIT)
- Tests: xunit 2.9.3, xunit.runner.visualstudio 3.1.5 (Apache 2.0), NSubstitute 5.3.0 (BSD-3), Shouldly 4.3.0 (BSD-3), coverlet.collector 6.0.4 (MIT), Microsoft.NET.Test.Sdk 17.14.1 (MIT)
- Gestrichen: NAudio (ffmpeg deckt Analyse und Vorschau ab).

## 2026-09-23 – Projektstart

**Geändert**

- Dokumentationsstruktur unter `docs/` angelegt und mit dem Briefing gefüllt (Anforderungen, Rechtssicherheit, Architektur, Bibliotheken, Formate, Design, Store, Testing, Roadmap).
- `CLAUDE.md` mit oberster Regel, Stack, Build-Befehlen, Konventionen und Doku-Index.
- Agenten unter `.claude/agents/`: `architekt`, `lizenz-waechter`, `reviewer` (Fable), `engine-entwickler`, `ui-entwickler`, `tester`, `store-release` (Opus), `doku-pfleger` (Sonnet).
- Architekturentscheidungen ADR-001 bis ADR-010 in `03-architektur.md`.

**Bibliotheken**

- Noch keine im Code. Geplante Bibliotheken mit Lizenz stehen in `04-bibliotheken.md` (Status `geplant`).
- Abgelehnt: QuestPDF (Umsatzgrenze in der Community-Lizenz) zugunsten von PDFsharp (MIT); FluentAssertions ≥ 8 zugunsten von Shouldly (BSD-3).
