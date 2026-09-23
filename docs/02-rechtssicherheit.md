# 02 – Rechtssicherheit

**Oberste Regel: Rechtssicherheit vor Features. Im Zweifel wird ein Feature weggelassen, statt ein Risiko einzugehen.**

Dieses Dokument ist die verbindliche Prüfliste für `lizenz-waechter` und `reviewer`. Es ersetzt keine Rechtsberatung. Es wird kein Anwalt hinzugezogen (Entscheidung Projektinhaber, 2026-09-23); was ohne juristische Prüfung nicht sicher einschätzbar ist, wird weggelassen. Offene Punkte stehen in [09-roadmap.md](09-roadmap.md).

## 1. Lizenzen von Bibliotheken

Kvertis ist Closed Source und wird verkauft. Daraus folgt:

| Lizenz | Erlaubt? | Bedingung |
|---|---|---|
| MIT, BSD-2/3, ISC, zlib | ja | Lizenztext und Copyright in der App (Third-Party-Licenses-Seite). |
| Apache 2.0 | ja | Lizenztext, `NOTICE`-Datei übernehmen, falls vorhanden. |
| ImageMagick License | ja | Apache-ähnlich, OSI-anerkannt. Lizenztext übernehmen. |
| LGPL 2.1 / 3.0 | ja, mit Auflagen | Nur als **separate DLL oder eigener Prozess**, nie statisch gelinkt. Lizenztext, Hinweis auf die Bibliothek, Quellcode-Angebot für die LGPL-Komponente inklusive Build-Konfiguration. Nutzer muss die Komponente austauschen können (bei separatem Prozess erfüllt). |
| MPL 2.0 | ja | Dateiweise Copyleft. Keine Änderung an MPL-Dateien; Lizenztext übernehmen. |
| GPL 2 / 3, AGPL, SSPL | **nein** | Kein GPL-Code im Produkt, auch nicht als DLL, auch nicht als „nur dynamisch gelinkt“. |
| Lizenzen mit Umsatzgrenze oder Gebühren (z. B. QuestPDF Community, FluentAssertions ab v8, Syncfusion Community) | **nein**, außer ausdrücklich freigegeben | Nur nach schriftlicher Freigabe des Projektinhabers mit Eintrag in `04-bibliotheken.md`. Standard: Alternative wählen (PDFsharp MIT, Shouldly BSD). |
| „Frei für nicht-kommerzielle Nutzung“ | **nein** | Kvertis ist kommerziell. |
| Unklare oder fehlende Lizenz | **nein** | Wird wie GPL behandelt. |

Regeln:

- Transitive Abhängigkeiten zählen mit. NuGet-Pakete werden inklusive ihrer Abhängigkeiten geprüft.
- Native Binärdateien in NuGet-Paketen (z. B. `Magick.Native`, FFmpeg-Builds) werden auf **mitgelieferte Drittbibliotheken** geprüft. Ein MIT-Paket kann eine GPL-Bibliothek einbetten.
- Jede Bibliothek steht in [04-bibliotheken.md](04-bibliotheken.md) und im [CHANGELOG](CHANGELOG.md) mit Version, Lizenz, Zweck und Einbindungsart. **Ohne Eintrag kein Merge.**
- Die App hat eine Seite „Third-Party Licenses“, die alle Bibliotheken mit vollständigem Lizenztext auflistet. Sie wird aus `04-bibliotheken.md` und den Lizenzdateien unter `third_party/` erzeugt.

## 2. FFmpeg

FFmpeg wird ausschließlich als **eigener LGPL-Build** und als **separater Prozess** (`ffmpeg.exe`, `ffprobe.exe`) genutzt. Nie als DLL laden, nie linken.

Der Build enthält **nur patentfreie Komponenten** (Allowlist in `tools/ffmpeg/configure-allowlist.txt`, Begründung in ADR-015):

- `--disable-gpl --disable-nonfree --disable-version3` (LGPL 2.1), `--disable-everything`, danach nur die gelisteten Bausteine.
- `--disable-network`, Protokolle nur `file` und `pipe`. Der Build kann technisch nichts aus dem Netz laden.
- `--enable-mediafoundation`: Encoder `h264_mf`, `hevc_mf`, `aac_mf`, `mp3_mf` nutzen die Windows-Systemcodecs.
- Decoder nur für patentfreie oder patentfrei gewordene Formate: VP8, VP9, AV1, Theora, MPEG-1/2 (abgelaufen 2018), MJPEG, PNG, GIF, Opus, Vorbis, FLAC, MP3 (abgelaufen 2017), MP2, AC-3 (abgelaufen 2017), ALAC, WavPack, PCM.
- **Nicht enthalten, weder als Encoder noch als Decoder:** H.264, HEVC, AAC, MPEG-4 Part 2, WMV/WMA/VC-1, H.263, ProRes, DNxHD, DTS, E-AC-3, AMR. Diese Formate werden ausschließlich von Windows Media Foundation verarbeitet.
- Externe Bibliotheken: `libvpx`, `libopus`, `libvorbis` (BSD), `libmp3lame` (LGPL), `dav1d` (BSD-2), `SVT-AV1` (BSD-2 mit Patentklausel).
- **Verboten:** `libx264`, `libx265`, `libfdk-aac`, `libxvid`, alles, was `--enable-gpl` oder `--enable-nonfree` verlangt.
- Der Build-Nachweis läuft dreifach: `tools/ffmpeg/check-build.sh|.ps1` (nach dem Bauen), `FfmpegCompliance` in der App (beim Start, verweigert fremde Builds) und der Integrationstest `FfmpegBuildComplianceTests`.

LGPL-Pflichten, die Kvertis erfüllt:

- Lizenztext LGPL 2.1 in der App.
- Hinweis, dass Kvertis FFmpeg verwendet, mit Link zu ffmpeg.org.
- Quellcode-Angebot: Der Build-Workflow (`.github/workflows/ffmpeg-build.yml`) archiviert FFmpeg-Quellen, Commit, Konfiguration und die Paketliste der Bibliotheken als Artefakt. Dieses Artefakt wird mit jeder Veröffentlichung abgelegt (Ort: offener Punkt O-02).
- Der Nutzer kann `ffmpeg.exe` austauschen: Kvertis unterstützt einen benutzerdefinierten FFmpeg-Pfad in den Einstellungen (ohne Download-Funktion). Ein fremder Build wird nur akzeptiert, wenn er die Compliance-Prüfung besteht.

## 3. Codecs und Patente

Grundsatz: **Kvertis liefert keinen einzigen patentbelasteten Codec aus.** Kodierung und Dekodierung von H.264, HEVC, AAC, HEIC, MPEG-4 Part 2 und Windows-Media-Formaten laufen über **Systemcodecs von Windows** (Media Foundation, Windows Imaging Component). Die Patentlizenz liegt dann beim Betriebssystem bzw. der vom Nutzer installierten Erweiterung, nicht bei Kvertis.

| Format | Kodieren | Dekodieren | Anmerkung |
|---|---|---|---|
| H.264 / AVC | nur `h264_mf` (Media Foundation) | nur Media Foundation (`MediaTranscoder`) | kein H.264-Decoder im FFmpeg-Build |
| HEVC / H.265 | nur `hevc_mf` | nur Media Foundation (HEVC-Videoerweiterung) | fehlt die Erweiterung: klare Meldung mit Hinweis auf den Store |
| AAC | nur `aac_mf` | nur Media Foundation | kein AAC-Decoder im FFmpeg-Build |
| MPEG-4 Part 2, WMV, WMA, VC-1 | nicht | nur Media Foundation | |
| HEIC / HEIF, AVIF, RAW (außer DNG) | nicht | nur Windows Imaging Component (HEIF-, AV1-, Raw-Bilderweiterung des Systems) | keine mitgelieferten Decoder; DNG liest SkiaSharp (Adobe DNG SDK, lizenzfrei) |
| MP3 | `libmp3lame` (LGPL) oder `mp3_mf` | FFmpeg-nativ | Patente abgelaufen 2017 |
| MPEG-1/2 Video, AC-3 | nicht | FFmpeg-nativ | Patente abgelaufen (2018 bzw. 2017) |
| AV1, VP8, VP9, Opus, Vorbis, FLAC, Theora, WebP, PNG, JPG, GIF, TIFF, BMP, WAV | frei | frei | Bevorzugte Standardformate |

Regeln:

- Standardvorschläge der App bevorzugen patentfreie Formate, wo das für Laien sinnvoll ist. Bei Video bleibt MP4/H.264 der Standardvorschlag, weil das für die Zielgruppe die höchste Kompatibilität hat; Kodierung und Dekodierung laufen über Media Foundation.
- Kein Codec wird „vorsichtshalber“ mitgeliefert. Was das System nicht kann, kann Kvertis nicht, und die App sagt das klar.
- Bildbibliothek ist SkiaSharp (MIT, geprüft: keine Video-Codecs). Magick.NET wurde entfernt, weil `Magick.Native` libde265 und openh264 statisch enthält (Prüfbericht in `04-bibliotheken.md`).
- Jede neue native Bibliothek wird vor dem Einbau auf mitgelieferte Codecs geprüft (Notice-Datei und Zeichenketten in der DLL), nicht nur auf ihre Lizenz.

## 4. Verbotene Funktionen

- Kein Umgehen von DRM oder Kopierschutz, in keiner Form.
- Keine Stream-, URL- oder Plattform-Downloads. Kvertis hat keinen Netzwerkcode.
- Kein Entfernen von Passwörtern oder Berechtigungen aus fremden PDFs. Verschlüsselte oder geschützte Dateien (PDF mit Passwort, DRM-geschützte Medien, verschlüsselte Office-Dateien) werden **abgelehnt** mit `ConversionErrorCode.ProtectedFile` und einer klaren Meldung.
- Kein Schreiben in Systemordner, keine Änderung an Originaldateien ohne ausdrückliche Wahl des Nutzers.

## 5. Markennamen

- Keine fremden Markennamen in App-Name, Presets, UI, Store-Texten, Doku, Code-Kommentaren oder Ressourcen-Schlüsseln.
- Presets beschreiben den Zweck, nicht das Produkt: „Für Messenger“, „Für E-Mail“, „Für Social Media“, „Für Website“, „Archiv (verlustfrei)“.
- Formatnamen (MP4, H.264, HEIC, PDF, WebP) sind technische Bezeichnungen und erlaubt. Firmennamen dahinter werden nicht genannt.
- `lizenz-waechter` sucht vor jedem Merge per Grep nach bekannten Marken.

## 6. Icons, Schriften, Bilder

- Schrift: Segoe UI Variable und Segoe Fluent Icons sind Systemschriften von Windows 11 und werden **nicht** mitgeliefert, nur referenziert. Fallback für Windows 10: Segoe UI und Segoe MDL2 Assets (ebenfalls System).
- Icons: Fluent UI System Icons (MIT) dürfen mitgeliefert werden. Lizenz in `third_party/`.
- Zusätzliche Schriften nur aus Quellen mit klarer Lizenz (OFL, Apache 2.0), Lizenzdatei mitliefern.
- App-Logo: eigenständig, keine Ähnlichkeit mit bestehenden Marken. Platzhalter bis zur finalen Gestaltung.
- Keine Stock-Bilder unklarer Herkunft, auch nicht für Screenshots im Store.

## 7. Store-Vorgaben

- Manifest: nur Berechtigungen mit begründetem Bedarf (siehe [07-store.md](07-store.md)). **Keine** Netzwerkberechtigungen (`internetClient`, `internetClientServer`, `privateNetworkClientServer`), kein `broadFileSystemAccess`. Dateizugriff nur über Picker, Drag-and-drop und Zwischenablage.
- Kryptografie-Deklaration: Phase 1 enthält keine Verschlüsselung → „nein“. Wird PDF-Passwortschutz geplant, wird die Deklaration vor der Umsetzung neu bewertet (Exportkontrolle).
- Datenschutzerklärung ist Pflicht, auch wenn keine Daten erhoben werden. Sie sagt genau das.
- Die Third-Party-Licenses-Seite und das LGPL-Quellcode-Angebot müssen vor der Einreichung vollständig sein.
- Altersfreigabe und Kategorie nach Store-Fragebogen; Kvertis verarbeitet keine nutzergenerierten Inhalte online.

## 8. Eingabedateien

Jede Datei wird vor der Übergabe an FFmpeg, Magick.NET oder eine Dokument-Bibliothek geprüft:

- Typ anhand der ersten Bytes (Magic Bytes), nicht anhand der Endung. Stimmen beide nicht überein, wird der Nutzer informiert.
- Größe gegen ein Limit je Kategorie (Standardwerte in `05-formate.md`), Restspeicher im Zielordner gegen die Größenschätzung.
- Analyse (`ffprobe`, Magick-Header) mit Timeout. Hängt die Analyse, gilt die Datei als beschädigt.
- Externe Prozesse laufen mit Timeout, werden bei Abbruch beendet, und ihre Ausgabe wird vollständig gelesen.
- Pfade werden gegen Traversal geprüft; Ausgabepfade liegen immer im gewählten Zielordner.

## 9. Was bei einem Verstoß passiert

Ein Verstoß gegen dieses Dokument blockiert den Merge. `lizenz-waechter` benennt Komponente, Verstoß und mindestens eine erlaubte Alternative. Gibt es keine Alternative, wird das Feature gestrichen und in `09-roadmap.md` als „verworfen wegen Rechtsrisiko“ dokumentiert.

## 10. Pflichten als Verkäufer

Kvertis wird verkauft. Neben Lizenzen und Patenten gelten deshalb Pflichten, die nicht an einzelnen Bibliotheken hängen. Sie werden ohne Anwalt abgearbeitet (O-17 in `09-roadmap.md`). Fristen und Einordnungen vor dem Verkaufsstart anhand der amtlichen Quellen nachprüfen.

Checkliste vor dem Verkaufsstart:

- [ ] **Rechtstexte:** Impressum, AGB, Widerrufsbelehrung, Datenschutzerklärung über ein Rechtstexte-Abo eines spezialisierten Anbieters (mit Aktualisierung und, falls angeboten, Haftung für die Texte). Impressum auch im Store-Eintrag.
- [ ] **Update-Zusage** (§ 327f BGB): Zeitraum für Sicherheits- und Funktions-Updates festlegen und veröffentlichen (Vorschlag: mindestens 2 Jahre ab Kauf); Sicherheitslücken in mitgelieferten Bibliotheken (SkiaSharp, FFmpeg, PdfPig, PDFsharp, OpenXml, Markdig) werden nachgeliefert.
- [ ] **Cyber Resilience Act:** SBOM (Stückliste der Softwarebestandteile) automatisch im Build erzeugen, abgeglichen mit `04-bibliotheken.md`; öffentlicher Sicherheitskontakt; kurzer dokumentierter Ablauf für Schwachstellen (annehmen, beheben, ausliefern, melden); Produktkategorie prüfen (erwartet: Standardkategorie mit Selbstbewertung) und CE-Konformität nach den offiziellen EU-Leitfäden.
- [x] **Produkthaftung technisch absichern:** Originaldateien werden nie überschrieben; Ausgaben entstehen atomar über eine temporäre Datei, ein bestehendes Ziel wird nur mit ausdrücklicher Zustimmung ersetzt (`ConversionOutput`, Tests in `tests/Kvertis.Engine.Tests/IO/ConversionOutputTests.cs`).
- [ ] **Datenschutz:** Kein Netzwerkcode bleibt Voraussetzung dafür, dass keine Verarbeitung personenbezogener Daten durch den Anbieter stattfindet.

Grundsatz: Funktionen mit Haftungsnähe, die ohne juristische Prüfung nicht sicher einschätzbar sind (z. B. Anzeige von Rechnungsdaten, PDF/A), werden nicht umgesetzt.
