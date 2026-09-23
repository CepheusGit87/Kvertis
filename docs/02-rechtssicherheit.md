# 02 – Rechtssicherheit

**Oberste Regel: Rechtssicherheit vor Features. Im Zweifel wird ein Feature weggelassen, statt ein Risiko einzugehen.**

Dieses Dokument ist die verbindliche Prüfliste für `lizenz-waechter` und `reviewer`. Es ersetzt keine Rechtsberatung. Punkte, die eine juristische Prüfung brauchen, sind in [09-roadmap.md](09-roadmap.md) unter „Offene Punkte“ markiert.

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

FFmpeg wird ausschließlich als **LGPL-Build** und als **separater Prozess** (`ffmpeg.exe`, `ffprobe.exe`) genutzt. Nie als DLL laden, nie linken.

Verbindliche Build-Konfiguration (wird unter `tools/ffmpeg/` dokumentiert und geprüft):

- `--disable-gpl --disable-nonfree --disable-version3` (bleibt bei LGPL 2.1).
- `--enable-mediafoundation` (Encoder `h264_mf`, `hevc_mf`, `aac_mf`, `mp3_mf` nutzen die Windows-Systemcodecs).
- Erlaubte externe Bibliotheken: `libvpx` (BSD), `libopus` (BSD), `libvorbis` (BSD), `libmp3lame` (LGPL), `libaom` (BSD-2 mit Patentklausel), `libsvtav1` (BSD-2 mit Patentklausel), `libdav1d` (BSD-2), `libwebp` (BSD).
- **Verboten:** `libx264`, `libx265`, `libfdk-aac`, `libxvid`, alles, was `--enable-gpl` oder `--enable-nonfree` verlangt.
- Der Build-Nachweis (`ffmpeg -version` zeigt `--disable-gpl` und keine `libx26x`-Einträge; `ffmpeg -encoders` enthält kein `libx264`/`libx265`) wird im Test `FfmpegBuildComplianceTests` (Kategorie Integration) geprüft.

LGPL-Pflichten, die Kvertis erfüllt:

- Lizenztext LGPL 2.1 in der App.
- Hinweis, dass Kvertis FFmpeg verwendet, mit Link zu ffmpeg.org.
- Quellcode-Angebot: exakte Version, Build-Konfiguration und Quellen des verwendeten Builds werden veröffentlicht (Ort: offener Punkt in `09-roadmap.md`). Werden Patches angewendet, werden sie mitveröffentlicht.
- Der Nutzer kann `ffmpeg.exe` im Installationsordner austauschen. Bei MSIX ist der Ordner schreibgeschützt; deshalb unterstützt Kvertis optional einen benutzerdefinierten FFmpeg-Pfad in den Einstellungen (nur für diesen Zweck, ohne Download-Funktion).

## 3. Codecs und Patente

Grundsatz: Kodierung und Dekodierung patentbelasteter Formate laufen über **Systemcodecs von Windows** (Media Foundation, Windows Imaging Component). Die Patentlizenz liegt dann beim Betriebssystem bzw. der vom Nutzer installierten Erweiterung, nicht bei Kvertis.

| Format | Kodieren | Dekodieren | Anmerkung |
|---|---|---|---|
| H.264 / AVC | nur `h264_mf` (Media Foundation) | bevorzugt Hardware/System (D3D11VA), Fallback FFmpeg-Software-Decoder | Software-Dekodierung ist LGPL, aber patentbelastet. Juristische Prüfung: offener Punkt. |
| HEVC / H.265 | nur `hevc_mf` | nur über System (D3D11VA / HEVC-Videoerweiterung). **Kein** Software-Fallback in Phase 1. | Fehlt die Erweiterung, klare Meldung mit Hinweis auf den Store. |
| AAC | nur `aac_mf` | FFmpeg-nativer Decoder (LGPL) | AAC-Dekodierung gilt als geringes Risiko, Prüfung dennoch offener Punkt. |
| HEIC / HEIF | nicht in Phase 1 | nur über Windows Imaging Component (HEIF-Bilderweiterung + HEVC-Videoerweiterung des Systems) | Keine mitgelieferten HEVC-Decoder (`libde265`, `x265`). Siehe ADR-006 in `03-architektur.md`. |
| MP3 | `libmp3lame` (LGPL) oder `mp3_mf` | FFmpeg-nativ | Patente abgelaufen. |
| AV1, VP9, VP8, Opus, Vorbis, FLAC, WebP, PNG, JPG, GIF, TIFF, BMP, WAV | frei | frei | Bevorzugte Standardformate. |

Regeln:

- Standardvorschläge der App bevorzugen patentfreie Formate, wo das für Laien sinnvoll ist. Bei Video bleibt MP4/H.264 der Standardvorschlag, weil das für die Zielgruppe die höchste Kompatibilität hat; die Kodierung läuft über Media Foundation.
- Kein Codec wird „vorsichtshalber“ mitgeliefert. Was das System nicht kann, kann Kvertis nicht, und die App sagt das klar.

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
