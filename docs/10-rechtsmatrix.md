# 10 – Rechtsmatrix: Konverter → Bibliothek → Lizenz → Patentlage → Begründung

Die eine Stelle, an der pro Konverter nachvollziehbar ist, womit er arbeitet, unter welcher Lizenz, wie die Patentlage ist, warum das zulässig ist und wo es geprüft wird. Interne Prüfunterlage, **nicht** Teil der App (die App zeigt nur Lizenztexte und das Quellcode-Angebot, siehe unten). Dieses Dokument ersetzt keine Rechtsberatung; es wird kein Anwalt hinzugezogen, offene Punkte stehen in `09-roadmap.md`.

Stand: 2026-09-23. Bei jeder Änderung an einem Konverter oder einer Bibliothek wird diese Tabelle im selben Commit nachgezogen (Compliance-Gate prüft, dass jeder `IConverter` hier steht).

## Grundsätze

1. **Kein mitgelieferter Codec mit Patentpool.** H.264, HEVC, AAC, HEIC, MPEG-4 Part 2, WMV/WMA werden ausschließlich von Windows-Komponenten verarbeitet (Media Foundation, Windows Imaging Component). Die Patentlizenz liegt beim Betriebssystemhersteller bzw. bei der vom Nutzer installierten Store-Erweiterung.
2. **Nur MIT/BSD/Apache/MPL/LGPL-Bibliotheken**, LGPL nur als separater Prozess oder austauschbare DLL, kein GPL.
3. **Native DLLs werden auf eingebettete Drittbibliotheken geprüft** (Notice-Datei plus Zeichenketten in der Binärdatei), nicht nur auf ihre Lizenz. Prüfberichte in `04-bibliotheken.md`.
4. **Kein Netzwerk**: kein Netzwerkcode in Kvertis, FFmpeg ohne Netzwerkprotokolle, Manifest ohne Netzwerkberechtigung.

## Matrix

| Konverter (Code) | Formate | Bibliothek / Systemkomponente | Lizenz | Patentlage | Warum zulässig | Nachweis / Prüfung |
|---|---|---|---|---|---|---|
| `ImageConverter` (Skia-Pfad) | JPG, PNG, WebP, GIF, BMP, ICO, DNG lesen; JPG, PNG, WebP, ICO schreiben | SkiaSharp 4.152.1 (`libSkiaSharp.dll`) | MIT; enthält skia (BSD-3), libjpeg-turbo, libpng, libwebp, zlib, freetype, harfbuzz, expat, ICU, piex, DNG SDK, wuffs; GIF-Decoder MPL 1.1 (Tri-Lizenz, MPL gewählt) | JPEG-Grundpatente abgelaufen; PNG, WebP (Patentgewährung des Herstellers), GIF (LZW abgelaufen 2004), DNG (lizenzfrei) | Keine Video-Codecs in der DLL (Zeichenkettensuche negativ), rein permissive Lizenzen | Prüfbericht SkiaSharp in `04-bibliotheken.md`; `NoImageMagickSourceScanTests`; Lizenztexte `third_party/SkiaSharp/` |
| `ImageConverter` → `ISystemImageCodec` (`WicImageCodec`) | HEIC/HEIF, AVIF, RAW (außer DNG), TIFF lesen; TIFF, BMP, GIF schreiben | Windows Imaging Component + HEIF-, AV1-, Raw-Bilderweiterung | Bestandteil von Windows / Store-Erweiterungen des Nutzers | HEVC (HEIC) und AV1 lizenziert durch Betriebssystemhersteller bzw. Erweiterung | Kvertis liefert keinen Decoder aus; fehlt die Erweiterung, meldet die App `MissingSystemCodec` | ADR-006, ADR-014; `Kvertis.Engine.Windows/Imaging/WicImageCodec.cs` |
| `MediaFoundationTranscoder` | Eingaben mit H.264, HEVC, AAC, MPEG-4 Part 2, WMV, WMA (MP4, MOV, M4A, WMV, AVI, 3GP, MPEG, TS, MKV mit diesen Codecs) → MP4 (H.264/AAC), M4A, MP3, WAV, FLAC | Windows Media Foundation (`MediaTranscoder`) + ggf. HEVC-Videoerweiterung | Bestandteil von Windows | Lizenz beim Betriebssystemhersteller bzw. Erweiterung | Kein H.264/HEVC/AAC-Code im Paket; Routing nach Codec-Namen erzwingt diesen Pfad | ADR-015; `EncumberedCodecs`; Tests: Encumbered-Eingaben erreichen nie ffmpeg |
| `AudioConverter`, `VideoConverter` (ffmpeg-Prozess) | Patentfreie Eingaben: WebM, MKV (VP8/VP9/AV1/Theora), OGG, Opus, FLAC, WAV, MP3, AIFF, MPEG-1/2, AC-3 → WebM, MKV, OGG, Opus, FLAC, WAV, MP3, AIFF, M4A, MP4 | Eigener FFmpeg-Build (Allowlist), separater Prozess | LGPL 2.1 (FFmpeg, libmp3lame); libvpx, libopus, libvorbis, dav1d, SVT-AV1 (BSD) | Decoder nur für patentfreie oder abgelaufene Formate (MP3 2017, MPEG-1/2 2018, AC-3 2017); AV1/VP9/Opus mit Patentgewährung; H.264/AAC-**Kodierung** über `h264_mf`/`aac_mf` (Systemcodecs) | Keine patentbelasteten Decoder oder Encoder im Build, `--disable-network`; Prozessgrenze erfüllt LGPL; Quellcode-Angebot aus dem Build-Workflow | `tools/ffmpeg/configure-allowlist.txt`, `check-build.sh/.ps1`, `FfmpegCompliance` (verweigert fremde Builds), `FfmpegBuildComplianceTests`, `ForbiddenEncoderSourceScanTests`; Lizenztext `third_party/FFmpeg/` |
| `PdfConverter` | PDF → TXT; PDF → PNG/JPG | PdfPig 0.1.16; Rendering über `Windows.Data.Pdf` (`WindowsPdfRasterizer`) | Apache 2.0; System | keine | Reines Dokumentformat; verschlüsselte PDFs werden abgelehnt, nie entsperrt | `DocumentSafetyTests` (keine Passwortversuche); Lizenztext `third_party/PdfPig/` |
| `OfficeConverter` | DOCX/XLSX/PPTX → TXT, Markdown, HTML, CSV | DocumentFormat.OpenXml 3.5.1 | MIT | keine (offenes Format, ISO 29500) | Verschlüsselte Dateien werden bei der Erkennung als `ProtectedFile` abgelehnt | `EncryptedOfficeDetectionTests`; Lizenztext `third_party/DocumentFormat.OpenXml/` |
| `TextConverter` | TXT/Markdown → PDF, HTML; HTML → TXT/Markdown; CSV → TXT | PDFsharp 6.2.4, Markdig 1.4.0, eigener HTML-Parser | MIT, BSD-2 | keine | Kein Netzwerkzugriff (HTML wird nur lokal geparst, keine externen Ressourcen); QuestPDF bewusst nicht verwendet (Umsatzgrenze) | `DocumentSafetyTests` (Quelltext-Scan auf Netzwerkcode); Lizenztexte `third_party/PDFsharp/`, `third_party/Markdig/` |
| `ImageToPdfConverter` | JPG/PNG/TIFF → PDF | PDFsharp 6.2.4, SkiaSharp (Pixel), WIC (TIFF) | MIT | keine | Wie oben; EXIF/GPS werden beim Einbetten entfernt | `ImageToPdfConverterTests` |
| Formaterkennung, Validierung, Queue, Schätzung | alle | eigener Code, .NET 8 | MIT (.NET) | keine | | |
| App (UI, Store-Kauf) | | WinUI 3 / Windows App SDK 2.5.1, CommunityToolkit (MIT), Microsoft.Extensions (MIT), `Windows.Services.Store` | Microsoft Software License Terms (Quellcode MIT), MIT, System | keine | Store-API ist Bestandteil von Windows; kein eigener Netzwerkcode | `tools/compliance/check.sh` (Netzwerkcode, Manifest) |

## Bewusst nicht enthalten (und warum)

| Feature | Grund | Alternative / Status |
|---|---|---|
| Magick.NET (ImageMagick) | `Magick.Native` enthält libde265 (HEVC) und openh264 statisch | entfernt 2026-09-23, ersetzt durch SkiaSharp + WIC (ADR-014) |
| H.264/HEVC/AAC-Decoder in FFmpeg | Patentpools (Via LA, Access Advance, Velos u. a.); Verbreitung genügt | Media Foundation (ADR-015) |
| libx264, libx265, libfdk-aac, libxvid | GPL bzw. non-free | `h264_mf`, `hevc_mf`, `aac_mf` |
| HEVC-Software-Fallback | Patentlage | im Build nicht vorhanden |
| PSD, SVG lesen | keine risikofreie Bibliothek (SVG-Pakete ziehen LGPL-Abhängigkeiten nach) | Phase 2 prüfen |
| H.264/HEVC-Eingabe → WebM/MKV | bräuchte Media-Foundation-Source-Reader mit Pipe zu FFmpeg | Phase 2 |
| QuestPDF | Community-Lizenz mit Umsatzgrenze | PDFsharp (MIT) |
| FluentAssertions ≥ 8 | kostenpflichtig für kommerzielle Nutzung | Shouldly (BSD-3) |
| NAudio | nicht nötig | FFmpeg |
| DRM-Umgehung, Passwörter entfernen, URL-Downloads | Rechtslage, Briefing | nie |
| Transkription mit Sprachmodell | Modelllizenzen teils nicht kommerziell, KI-Kennzeichnung, Paketgröße | verworfen 2026-09-23 |
| RAR/CBR, MOBI/AZW, FBX, USDZ | proprietär, restriktive Lizenzen, Markenbezug, DRM | nie |
| PDF/A erzeugen, MIDI → Audio | Haftung ohne Prüfer bzw. unklare Klangbibliothek-Lizenzen | nicht geplant |
| E-Rechnung (XML) → PDF | Haftung bei falscher Darstellung, Markenfrage | verworfen 2026-09-23 |

## Was die App zeigt (Pflichtteil)

Seite „Third-Party Licenses“: jede Bibliothek mit Version, Lizenz und vollständigem Lizenztext (aus `third_party/`), FFmpeg-Hinweis mit Link, LGPL-Text und Quellcode-Angebot, plus der Satz „Video- und HEIC-Codecs stellt Windows bereit. Kvertis enthält keine eigenen Codecs.“ Die Spalten „Patentlage“, „Warum zulässig“ und „Nachweis“ bleiben in diesem Dokument.

## Offene Punkte mit Rechtsbezug

Siehe `09-roadmap.md`: O-02 (FFmpeg-Build erzeugen, Quellcode-Angebot ablegen), O-11 (`NtSuspendProcess` und Store-Zertifizierung), O-13 (Mono-Schrift mit OFL-Lizenz), O-17 (Pflichten als Verkäufer, Checkliste ohne Anwalt), O-20 (FFmpeg-Filter für GIF).
