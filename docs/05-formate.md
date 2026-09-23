# 05 – Formate

Matrix der Eingabeformate mit möglichen Ausgabeformaten, dem Standardvorschlag für Laien und der zuständigen Engine. Die Erkennung läuft über Magic Bytes, nicht über die Dateiendung. Formate, deren Systemcodec fehlt, werden zur Laufzeit ausgeblendet (`MediaFoundationCapabilities`, WIC-Prüfung).

Legende Engine: `Magick` = Magick.NET, `FFmpeg` = ffmpeg-Prozess, `MF` = Media-Foundation-Encoder über FFmpeg, `WIC` = Windows Imaging Component, `PdfPig`/`PDFsharp`/`OpenXml` = jeweilige Bibliothek, `WinPdf` = Windows.Data.Pdf.

## Bilder

| Eingabe | Ausgaben (Phase 1) | Standardvorschlag | Engine | Anmerkung |
|---|---|---|---|---|
| JPG/JPEG | PNG, WebP, TIFF, BMP, GIF, PDF | PNG | Magick (PDF: PDFsharp) | Komprimieren auf Zielgröße: JPG → JPG mit Qualitätsregler |
| PNG | JPG, WebP, TIFF, BMP, GIF, ICO, PDF | JPG | Magick | Transparenz-Hinweis bei JPG |
| WebP | JPG, PNG, TIFF, GIF | JPG | Magick | Animierte WebP → GIF möglich |
| GIF | PNG, JPG, WebP, MP4 (Phase 2) | PNG | Magick | Animierte GIF: erstes Bild in Phase 1, Hinweis |
| BMP | PNG, JPG, WebP | PNG | Magick | |
| TIFF (auch mehrseitig) | PNG, JPG, WebP, PDF | PNG | Magick | Mehrseitig → nummerierte Dateien oder PDF |
| HEIC/HEIF | JPG, PNG, WebP | JPG | WIC → Magick | Nur mit HEIF-Bilderweiterung; sonst `MissingSystemCodec` |
| AVIF | JPG, PNG, WebP | JPG | Magick (dav1d/aom) | Verfügbarkeit im Magick-Paket prüfen |
| SVG | PNG, JPG, WebP | PNG | Magick | Nur lesen; Größe wählbar unter „Mehr“ |
| ICO | PNG | PNG | Magick | |
| RAW (DNG, CR2, CR3, NEF, ARW, ORF, RAF, RW2) | JPG, PNG, TIFF | JPG | Magick (libraw, LGPL/CDDL) | Alternativ WIC mit Raw-Bilderweiterung; Entscheidung offen |
| PSD | PNG, JPG | PNG | Magick | Nur zusammengeführtes Bild |

Ausgabe-Encoder für Bilder: JPG, PNG, WebP (libwebp, BSD), GIF, TIFF, BMP, ICO. AVIF-Schreiben (libaom) erst, wenn die Paketprüfung abgeschlossen ist.

Zielgröße bei Bildern: Kvertis sucht per Halbierung die höchste Qualität, deren Ergebnis unter der Zielgröße liegt; falls nötig zusätzlich Verkleinerung der Auflösung in 10-%-Schritten. Metadaten werden standardmäßig entfernt (EXIF, GPS, XMP, ICC bleibt erhalten, damit Farben stimmen).

## Audio

| Eingabe | Ausgaben (Phase 1) | Standardvorschlag | Engine | Anmerkung |
|---|---|---|---|---|
| MP3 | WAV, FLAC, OGG (Vorbis), OPUS, M4A (AAC) | WAV | FFmpeg / MF | |
| WAV | MP3, FLAC, OGG, OPUS, M4A | MP3 | FFmpeg / MF | |
| FLAC | MP3, WAV, OGG, OPUS, M4A | MP3 | FFmpeg / MF | |
| OGG (Vorbis) | MP3, WAV, FLAC, OPUS, M4A | MP3 | FFmpeg / MF | |
| OPUS | MP3, WAV, FLAC, OGG, M4A | MP3 | FFmpeg / MF | |
| M4A / AAC | MP3, WAV, FLAC, OGG, OPUS | MP3 | FFmpeg / MF | Dekodierung mit FFmpeg-nativem AAC-Decoder |
| WMA | MP3, WAV, FLAC, OGG, OPUS, M4A | MP3 | FFmpeg | |
| AIFF | MP3, WAV, FLAC, OGG, OPUS, M4A | MP3 | FFmpeg / MF | |
| Video-Datei (Tonspur extrahieren) | MP3, WAV, FLAC, M4A | MP3 | FFmpeg / MF | Erscheint als „Nur Ton“ in der Formatliste |

Encoder: MP3 über `libmp3lame` (LGPL) oder `mp3_mf`; AAC nur über `aac_mf`; Opus über `libopus`; Vorbis über `libvorbis`; FLAC und WAV FFmpeg-nativ.

Zielgröße bei Audio: Bitrate = Zielgröße / Dauer, abgerundet auf gängige Stufen (64, 96, 128, 160, 192, 256, 320 kbit/s), Warnung unter 64 kbit/s. Bei verlustfreien Zielen ist die Zielgröße deaktiviert.

## Video (nur Pro)

| Eingabe | Ausgaben (Phase 1) | Standardvorschlag | Engine | Anmerkung |
|---|---|---|---|---|
| MP4, MOV, M4V | MP4 (H.264/AAC), MKV, WebM (VP9/Opus), Nur Ton | MP4 | FFmpeg + MF | |
| MKV | MP4, WebM, Nur Ton | MP4 | FFmpeg + MF | Mehrere Tonspuren: erste wird genommen, Auswahl Phase 2 |
| AVI, WMV, FLV, 3GP, MPG/MPEG, TS/MTS/M2TS | MP4, MKV, WebM, Nur Ton | MP4 | FFmpeg + MF | Interlaced-Quellen: Hinweis, Deinterlace unter „Mehr“ |
| WebM | MP4, MKV, Nur Ton | MP4 | FFmpeg + MF | |
| HEVC-Quellen (in jedem Container) | wie oben | MP4 | D3D11VA → FFmpeg + MF | Ohne HEVC-Videoerweiterung: `MissingSystemCodec` |
| VFR-Quellen (Bildschirmaufnahmen, Handy) | wie oben | MP4 | FFmpeg | Warnung `VariableFrameRate`; Kvertis setzt `-vsync cfr` automatisch |

Encoder: H.264 nur `h264_mf`, HEVC nur `hevc_mf`, AAC nur `aac_mf`, VP9 `libvpx-vp9`, AV1 `libsvtav1` (Phase 2, wenn Tempo akzeptabel), Opus `libopus`. Video → GIF und Bildsequenz: Phase 2. Zielgröße bei Video: Phase 2 (Two-Pass).

## Dokumente

| Eingabe | Ausgaben (Phase 1) | Standardvorschlag | Engine | Anmerkung |
|---|---|---|---|---|
| PDF | TXT, PNG/JPG je Seite (Phase 2: laut Briefing „Zerlegen“; technisch über `WinPdf` früh möglich) | TXT | PdfPig, WinPdf | Passwortgeschützt → `ProtectedFile`, keine Umgehung |
| DOCX | TXT, Markdown, HTML | TXT | OpenXml | Kein layouttreues PDF (ADR-010) |
| XLSX | CSV (je Blatt), TXT | CSV | OpenXml | Formeln als Werte |
| PPTX | TXT (Folientexte), Markdown | TXT | OpenXml | |
| TXT, Markdown | PDF, HTML, TXT (Zeichensatz-Umwandlung) | PDF | PDFsharp | Markdown-Rendering: einfache Teilmenge |
| HTML | TXT, Markdown | TXT | eigener Parser (klein) | Nur lokale Dateien, keine externen Ressourcen laden |
| Bilder (mehrere) | PDF | PDF | PDFsharp | „Sammeln“ laut Briefing Phase 2; als Ausgabeformat einzelner Bilder schon Phase 1 |
| DOC, XLS, PPT (alte Binärformate) | — | — | — | Nicht unterstützt, klare Meldung |
| Verschlüsselte Office-Dateien | — | — | — | `ProtectedFile` |

## Presets

| Preset | Bilder | Audio | Video | Metadaten |
|---|---|---|---|---|
| Für Messenger | JPG, max. 1600 px lange Kante, Ziel ≤ 1 MB | M4A 96 kbit/s | MP4 H.264 720p, Ziel ≤ 16 MB (Phase 2 exakt) | entfernen |
| Für E-Mail | JPG, max. 2048 px, Ziel ≤ 2 MB pro Bild | MP3 128 kbit/s | MP4 H.264 720p, Ziel ≤ 20 MB (Phase 2) | entfernen |
| Für Social Media | JPG, 2048 px, Qualität 85 | MP3 192 kbit/s | MP4 H.264 1080p | entfernen |
| Für Website | WebP, 1920 px, Qualität 80 | OPUS 96 kbit/s | WebM VP9 1080p | entfernen |
| Archiv (verlustfrei) | PNG oder TIFF, Originalgröße | FLAC | MKV mit Kopie der Spuren (`-c copy`, wenn möglich) | behalten |

## Grenzwerte für die Eingabeprüfung (Standard, änderbar unter „Mehr“)

| Kategorie | Maximale Dateigröße | Analyse-Timeout | Konvertierungs-Timeout |
|---|---|---|---|
| Bilder | 500 MB | 10 s | 5 min |
| Audio | 2 GB | 15 s | 30 min |
| Video | 20 GB | 30 s | 6 h |
| Dokumente | 500 MB | 15 s | 10 min |

Dateien darüber werden nicht stillschweigend abgelehnt; die App fragt nach.

## Magic Bytes (Auszug, vollständige Tabelle im Code `Formats/MagicBytes.cs`)

| Format | Signatur |
|---|---|
| JPG | `FF D8 FF` |
| PNG | `89 50 4E 47 0D 0A 1A 0A` |
| GIF | `47 49 46 38` |
| WebP | `52 49 46 46 .. .. .. .. 57 45 42 50` |
| HEIC/AVIF | `.. .. .. .. 66 74 79 70` + Brand (`heic`, `heix`, `mif1`, `avif`) |
| MP4/MOV | `.. .. .. .. 66 74 79 70` + Brand (`isom`, `mp42`, `qt  `) |
| MKV/WebM | `1A 45 DF A3` |
| MP3 | `49 44 33` (ID3) oder Frame-Sync `FF Fx` |
| FLAC | `66 4C 61 43` |
| OGG | `4F 67 67 53` |
| WAV | `52 49 46 46 .. .. .. .. 57 41 56 45` |
| PDF | `25 50 44 46` |
| DOCX/XLSX/PPTX | `50 4B 03 04` + `[Content_Types].xml` im Archiv |
