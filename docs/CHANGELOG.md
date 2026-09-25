# Changelog

Format: Datum, Änderung, neue oder entfernte Bibliotheken mit Lizenz. Neueste Einträge oben.

## 2026-09-25 – Fensterposition merken

**Geändert**

- Die App merkt sich Position, Größe und Maximiert-Zustand des Hauptfensters (`WindowPlacement` in den Einstellungen, `Helpers/WindowPlacementHelper.cs`) und öffnet beim nächsten Start wieder dort, sofern der Bildschirm noch vorhanden ist und mindestens 200×200 Pixel im Arbeitsbereich liegen; sonst Standardlage.

**Bibliotheken**

- Keine Änderung.

## 2026-09-25 – Schritt 3 „Umwandeln“: Umwandeln-Seite, Koordinator, alter Start-Weg entfernt (ADR-021)

**Geändert**

- Neue Umwandeln-Seite (`Views/ConvertPage`): Eingangsstapel, Platzhalter-Fläche `SwirlHost` für den späteren Pixelwirbel mit laufenden Jobs, „fertig n von N“, Speicherort mit „Speicherort ändern“ (Neben dem Original, Unterordner „Kvertis“, eigener Ordner, Ordner aus dem Explorer ziehen) und eigenem Ziel je Datei, Liste Format → Format mit Zielpfad, „Ändern“, „Öffnen“, „Im Ordner zeigen“, Fehler mit Lösungsvorschlag; untere Leiste mit Gesamtfortschritt, Restzeit, Pause/Weiter, Abbrechen mit Rückfrage, Bericht und „Neue Runde“. Fortschritt als Text für den Erzähler.
- `TargetPathPlanner` (Zielpfad-Vorschau mit Nummerierung, nie überschreiben), `ConversionCoordinator` (Jobs aus Plan und Speicherort, `EnqueueRange`, Filter auf die eigene Runde, Speicherort-Wechsel nach Start nur für wartende Jobs, `RoundReport`), `ShellLauncher` (nur lokale Pfade). `IWorkflowSession` mit `OwnLocations`.
- Alter Start-Weg entfernt: Start-Knopf, Speicherort, Gesamtfortschritt, Pause/Abbruch und Queue-Anbindung aus Schritt 1; `MorePanel` und `FormatPickerFlyout` gelöscht; `JobCardControl` zeigt nur noch Erkennung, Bereit und Abgelehnt. Schrittleiste sperrt Schritt 1 und 2 während des Laufs.
- Echter Durchlauf unter Windows: zwei Testbilder → JPG im Unterordner „Kvertis“, Nummerierung bei Namenskollision geprüft.
- Tests: 22 neu (App 65), gesamt 831. Ressourcen: 78 neue `Convert_*`-Schlüssel, `More_*`, `FormatPicker_*` und die alten `Main_*`/`Card_*`-Schlüssel entfernt.

**Bibliotheken**

- Keine Änderung.

## 2026-09-25 – Schritt 2 „Ziel“, Teile B–D: Ziel-Seite in der App

**Geändert**

- Neue Ziel-Seite (`Views/TargetPage`): links Dateiarten und Dateikarten mit „Alle gleich / Jede einzeln“, Mitte die Wege Format → Ziel mit Größenschätzung, rechts Zielformat, Note 0–100 im Ring (als Slider, Tastatur ±5), Größenleiste mit Farbsegmenten und Preset-Marken, Zonen je Merkmal (Schärfe, Details, Bewegung, Klang), „Zielgröße genau“, „Weiteres“, „Was sich ändert“, „damals“-Karte aus dem Verlauf; untere Leiste mit Summe und „Weiter: Umwandeln“. Ohne Zeichenschicht (kommt mit Win2D).
- `IWorkflowSession` (ADR-020) hält Dateien, Plan und Speicherort; `TargetPlanner` als WinUI-freie Logik (Format-Schnittmenge, logarithmische Größenskala, Plan mit Freemium-Grenzen); `Debouncer` (50 ms, TimeProvider). `FreemiumPolicy` mit `IsKindLocked` und `BatchLimit`.
- Schritt 1 legt Dateien in die Sitzung und bietet „Weiter: Ziel“; Verlauf „Nochmal“ setzt die alten Werte als „damals“ und springt nach Schritt 2; Schritt 3 zeigt vorerst Anzahl und Summe des Plans. Der alte Start-Weg bleibt bis Schritt 3 erhalten.
- Absturzfix in `CardAnimations` (Translation-Animation vor Aktivierung gestoppt).
- Debug-Hilfe: Umgebungsvariable `KVERTIS_STAGE_FILES` (nur Debug-Build) legt Dateien direkt in die Sitzung.
- Neues Testprojekt `tests/Kvertis.App.Tests` (26 Tests, WinUI-freie Logik als Quelldateien eingebunden). Gesamt 792 Tests.
- 105 neue Ressourcen-Schlüssel je Sprache; `check-resw.py` kennt die neuen Familien.

**Bibliotheken**

- Keine Änderung.

## 2026-09-25 – Schritt 2 „Ziel“, Teil A: Note und Größenmodell in der Engine

**Geändert**

- Schnittstellen-Skizze für Schritt 2 in `03-architektur.md` mit ADR-019 (Note 0–100 als reine Rechenfunktion in `Kvertis.Engine.Tuning`, kein neues Feld in den Einstellungen) und ADR-020 (Ablaufzustand `IWorkflowSession` in der App, Queue unverändert). Arbeitsblatt `docs/entwuerfe/schritt-2-ziel.md`.
- Neu in der Engine: `Tuning/QualityGrade`, `GradeMapper` (Note ↔ Einstellungen, Merkmale Schärfe/Details/Bewegung/Klang), `EffectAnalyzer` (Codes statt Texte für „Was sich ändert“), `GradeSizeTable` (21 Stützstellen je Datei für Note ↔ Größe), `SizeMarks` (Marken wie „E-Mail“ aus den Presets), `Estimation/SizeModel`. `Estimator` berücksichtigt Qualität, Auflösung und Bitrate; Tempo-Profil bleibt kompatibel.
- Engine-Tests 596 → 717.

**Bibliotheken**

- Keine Änderung.

## 2026-09-25 – Neue Oberfläche, Etappe 0: Farbtokens, Schrittleiste, ruhige Ansicht

**Geändert**

- Farbtokens aus dem Mischentwurf als Theme-Wörterbuch `src/Kvertis.App/Themes/KvertisColors.xaml` (Dunkel, Hell, Hoher Kontrast); Mint ersetzt den Systemakzent bei Standard-Bedienelementen (ADR-017). Im Hohen Kontrast gelten die Systemfarben.
- Schrittleiste „Hineinwerfen → Ziel → Umwandeln“ (`Views/StepHeader`) unter der Titelleiste, `StepNavigationService` mit `WorkflowStep`. Die bisherige Hauptseite bleibt Schritt 1; `TargetPage` und `ConvertPage` sind navigierbare Platzhalter.
- `IMotionSettings` (`Services/MotionSettings.cs`) liest „Animationen reduzieren“ und Hohen Kontrast zentral; `CardAnimations` nutzt ihn. Grundlage für die statische Ansicht ohne Zeichenschicht (ADR-018).
- ADR-017 (eigene Akzentfarbe), ADR-018 (Win2D als Zeichenschicht) und Nachtrag zu ADR-001 (.NET 10) in `03-architektur.md`; Token-Tabelle in `06-design.md`.
- 13 neue Ressourcen-Schlüssel (`Steps_*`, `Target_*`, `Convert_*`) in DE und EN; `check-resw.py` kennt die neuen Präfixe.

**Bibliotheken**

- Microsoft.Graphics.Win2D 1.4.0 durch den Lizenz-Wächter geprüft und freigegeben (Quellcode MIT, Paket Microsoft Software License Terms wie Windows App SDK; native `Microsoft.Graphics.Canvas.dll` nur mit Windows-Systemimporten, keine Codecs, kein Netzwerk). Status „geprüft, geplant“ in `04-bibliotheken.md`, Lizenztexte in `third_party/_geplant/Win2D/`. Paketauflösung neben Windows App SDK 2.5.1 getestet, ohne Konflikt. Noch nicht im Code referenziert.

## 2026-09-25 – Umstieg auf .NET 10

**Geändert**

- Alle Projekte von `net8.0` auf `net10.0` (App und Engine.Windows: `net10.0-windows10.0.19041.0`). Grund: Support-Ende von .NET 8 im November 2026, .NET 10 ist Langzeitversion bis November 2028. Windows App SDK 2.5.1 baut damit unverändert; App startet, Engine- und Queue-Tests 645 grün.
- CI (`ci.yml`) auf .NET SDK 10. Doku nachgezogen: `CLAUDE.md`, `03-architektur.md`, `04-bibliotheken.md`, App-README.
- Beobachtung auf dem Entwicklungsrechner: Smart App Control hat eine frisch gebaute Test-DLL einmalig blockiert (Ereignis 3077, Anwendungssteuerungsrichtlinie); ein Neubau mit anderem Hash lief durch. Kein Befund im Code.

**Bibliotheken**

- Aktualisiert: Microsoft.Extensions.DependencyInjection 8.0.1 → 10.0.12 (MIT), Microsoft.Extensions.Logging 8.0.1 → 10.0.12 (MIT), Microsoft.Extensions.Logging.Abstractions 8.0.3 → 10.0.12 (MIT).

## 2026-09-25 – Erster Windows-Build und Start der App

**Geändert**

- Die WinUI-App baut erstmals unter Windows (`dotnet build src/Kvertis.App/Kvertis.App.csproj -p:Platform=x64`, .NET SDK 10, Windows App SDK 2.5.1), ohne Fehler und ohne XAML-Korrekturen. Engine- und Queue-Tests: 645 grün. Offener Punkt O-14 erledigt.
- Unverpackter Entwicklerstart ohne Entwicklermodus: `PackageInfo` (`src/Kvertis.App/Services/`) erkennt fehlende Paket-Identität; `AppPaths` weicht dann auf `%LOCALAPPDATA%Kvertis` aus, die Versionsanzeige liest die Assembly-Version. Der Store-Build (MSIX) verhält sich unverändert. Anleitung in `src/Kvertis.App/README.md`.
- `tools/design-server.js` und `.claude/launch.json`: kleiner lokaler Server ohne Abhängigkeiten für die Entwurfsseiten unter `design/`, damit die Vorschau Hell und Dunkel umschalten kann.
- Bekannt: Compliance-Schritt 6 (`check-resw.py`) braucht Python 3, das auf dem Entwicklungsrechner fehlt; die übrigen sechs Schritte laufen durch.

**Bibliotheken**

- Keine Änderung.

## 2026-09-25 – Oberflächenentwurf Schritt 3: weißes Loch und Abschluss

**Geändert (nur Entwurf und Doku)**

- Mischentwurf (`design/oberflaeche-mischentwurf.html`), Schritt 3: Die Pixel tragen die Farbe ihrer Dateiart. Statt des Zielordners rechts ein weißes Loch mit gebündelten Bahnen, jede fertige Datei wird ein Planet; Speicherort und „Speicherort ändern“ darunter, eigene Ziele im kleinen Knopf daneben.
- Neuer Abschluss statt „Häkchen im Loch“: Planeten drehen hoch, Sog, beide Löcher kreisen um die Mitte und verschmelzen, Supernova mit Beben von Fenster, Kopfleiste und Liste, danach Ring aus Planeten mit Häkchen und Bericht in der Mitte.
- Entwurfsseiten: `zielwelten-konzepte.html`, `weisses-loch-varianten.html`, `weisses-loch-mengen.html`, `weisses-loch-abschluss*.html`, `weisses-loch-tanz*.html`, `weisses-loch-finale.html`, `weisses-loch-anlauf.html`, `weisses-loch-einfach.html` (gewählt). Entscheidung in `design/ENTSCHEIDUNGEN.md`, Kurzfassung in `06-design.md`.

**Bibliotheken**

- Keine Änderung.

## 2026-09-24 – Oberflächenentwurf Übergänge und Abschluss

**Geändert (nur Entwurf und Doku)**

- Mischentwurf (`design/oberflaeche-mischentwurf.html`): Übergänge zwischen den Schritten als Überlagerung, die Seiten bleiben unverändert. Festgehalten: „Wurmloch“ (das Loch saugt die Fächer ein und gibt sie links als Artwahl frei, dann die Dateien als Blätter an den Eingangsstapel) und als Abschluss „Häkchen im Loch“ mit Ergebnis und Fehlern. Zum Zeigen lässt sich eine fehlerhafte Datei einschalten; sie landet wieder im Eingang und die Zeile zeigt den Fehler.
- Skizzen der fünf Konzepte in `design/uebergaenge-konzepte.html`. Entscheidung in `design/ENTSCHEIDUNGEN.md`, Kurzfassung in `06-design.md`.

**Bibliotheken**

- Keine Änderung.

## 2026-09-24 – Oberflächenentwurf Schritt 2

**Geändert (nur Entwurf und Doku)**

- Schritt 2 „Ziel“ im Mischentwurf (`design/oberflaeche-mischentwurf.html`) neu: Artwahl links als kleine Universen (flache Symbole mit Ring), Zoom-Wege in der Mitte, rechts Zielformate, Qualitätsnote, Größenleiste mit Zielgröße und aufklappbare Zonen mit Balken.
- Entscheidungen in `design/ENTSCHEIDUNGEN.md`, Kurzfassung in `06-design.md` („Entwurfsstand Oberfläche“).

**Bibliotheken**

- Keine Änderung.

## 2026-09-23 – 3D-Modelle

**Geändert**

- Neue Medienart `MediaKind.Model3D` und `ModelConverter` (ADR-016): STL (binär/Text), 3MF, OBJ, PLY (Text/binär) und glTF 2.0 (GLB, .gltf) lesen; STL, 3MF, OBJ, PLY, GLB schreiben. Nur Geometrie; Millimeter, Z nach oben; glTF wird von/nach Meter, Y nach oben umgerechnet. Keine Vorschau.
- Formaterkennung: GLB- und PLY-Signatur, binäres STL über die Dateigröße, Text-STL/glTF/OBJ über den Inhalt, 3MF im ZIP. Grenzwert 1 GB für 3D-Dateien.
- Schutz: Obergrenzen für Dreiecke, Punkte und PLY-Elemente; 3MF-Modellteil gegen ZIP-Bomben begrenzt, XML ohne DTD; glTF-Puffer nur aus dem Modellordner; glTF mit Pflicht-Erweiterungen wird abgelehnt.
- App: Konverter registriert, Symbol für 3D-Dateien, Texte (DE/EN) und Store-Texte nennen 3D-Modelle; 3D ist gratis.
- Tests: `ModelConverterTests` (48 Fälle).
- Doku: ADR-016, Formatmatrix, Rechtsmatrix, Roadmap, Anforderungen, Store-Texte.

**Bibliotheken**

- Keine Änderung (eigener Code).

## 2026-09-23 – Rechtsrahmen ohne Anwalt

**Geändert (nur Doku)**

- Entscheidung: kein Anwalt; was ohne juristische Prüfung nicht sicher einschätzbar ist, wird weggelassen (`02-rechtssicherheit.md`).
- O-03 erledigt: abgelaufene Patente (MP3, MPEG-1/2, AC-3) gelten als belegt, Decoder bleiben.
- O-08 erledigt: Store-Texte nennen nur DOCX/XLSX/PPTX statt Office-Produktnamen (`07-store.md`).
- O-18 erledigt: E-Rechnung → PDF/HTML verworfen.
- O-17 und `02-rechtssicherheit.md` Abschnitt 10: Checkliste für Verkäuferpflichten ohne Anwalt (Rechtstexte-Abo, Update-Zusage, Cyber Resilience Act, Schutz der Originaldateien).

**Bibliotheken**

- Keine Änderung.

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
