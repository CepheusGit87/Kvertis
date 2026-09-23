# 08 – Testing

## Strategie

| Ebene | Was | Werkzeug | Läuft wo |
|---|---|---|---|
| Unit | Formaterkennung, Eingabeprüfung, Zielgrößen-Berechnung, Schätzung, Dateinamen-Muster, Queue-Logik, ViewModels | xUnit, NSubstitute, Shouldly | Linux und Windows, jede CI-Ausführung |
| Integration | Echte Konvertierungen mit ffmpeg und Magick.NET, FFmpeg-Build-Nachweis (LGPL) | xUnit mit Trait `Category=Integration` | Windows-CI und lokal; braucht `ffmpeg.exe` |
| UI | Hauptweg per Tastatur, Screenreader-Namen, Kontrast | manuell nach Checkliste, später WinAppDriver falls Lizenz passt | Windows, vor jedem Release |
| Compliance | Grep nach Netzwerkcode, Markennamen, verbotenen Bibliotheken; Abgleich `04-bibliotheken.md` mit `.csproj` | Skript unter `tools/compliance/` | jede CI-Ausführung |

Regeln:

- Externe Prozesse laufen hinter `IProcessRunner`. Unit-Tests mocken ihn und prüfen die erzeugten Kommandozeilen (z. B. dass für MP4 `-c:v h264_mf` und nie `libx264` erzeugt wird).
- Kein Test wird übersprungen oder deaktiviert, um grün zu werden.
- Jeder Fehlercode hat mindestens einen Test, der ihn erzeugt.
- Zeit- und Größenschätzung werden gegen Toleranzen getestet (±30 % beim ersten Lauf, ±15 % nach fünf gemessenen Läufen desselben Typs).

## Testdateien

Unter `tests/TestFiles/`, jede Datei unter 200 KB, selbst erzeugt (Skript `tools/testfiles/generate.ps1` bzw. `.sh` mit ffmpeg und Magick) oder eindeutig frei lizenziert. Herkunft steht in `tests/TestFiles/README.md`.

| Datei | Zweck |
|---|---|
| `image_rgb.png`, `image_rgb.jpg`, `image_alpha.png`, `image.webp`, `image_multipage.tif` | Bild-Grundfälle, Transparenz, Mehrseitig |
| `image_with_gps.jpg` | EXIF/GPS vorhanden; Test, dass Metadaten entfernt werden |
| `image_wrong_extension.jpg` (eigentlich PNG) | Erkennung über Magic Bytes, Warnung `ExtensionMismatch` |
| `image_truncated.jpg` | Abgeschnittene Datei → `CorruptFile` |
| `empty.bin` | 0 Byte → `CorruptFile` |
| `audio_1s.wav`, `audio_1s.mp3`, `audio_1s.flac`, `audio_1s.ogg`, `audio_1s.opus` | Audio-Grundfälle |
| `video_2s_cfr.mp4`, `video_2s_vfr.mp4`, `video_2s.mkv`, `video_2s.webm` | Video-Grundfälle, VFR-Erkennung |
| `video_hevc_2s.mp4` | HEVC-Quelle; ohne Systemcodec → `MissingSystemCodec` |
| `doc_simple.pdf`, `doc_protected.pdf` | PDF lesen; Passwort → `ProtectedFile`, keine Umgehung |
| `doc_simple.docx`, `doc_simple.xlsx`, `doc_simple.pptx`, `doc_encrypted.docx` | Office lesen; verschlüsselt → `ProtectedFile` |
| `text_utf8.txt`, `text_latin1.txt`, `readme.md` | Text und Markdown |
| `pfad mit leerzeichen/ÄÖÜ ß 日本語.png` | Sonderzeichen in Pfaden |

## Bekannte Grenzfälle

| Fall | Erwartetes Verhalten | Status |
|---|---|---|
| Datei mit falscher Endung | Erkennung per Inhalt, Hinweis in der Karte, Konvertierung möglich | geplant |
| 0-Byte- oder abgeschnittene Datei | `CorruptFile` mit Lösungsvorschlag | geplant |
| VFR-Video (Bildschirmaufnahme, Handy) | Warnung, automatisch `-vsync cfr`, Hinweis auf mögliche Asynchronität | geplant |
| HEIC ohne HEIF-Bilderweiterung | `MissingSystemCodec`, Hinweis auf Store-Erweiterung | geplant |
| HEVC-Video ohne HEVC-Videoerweiterung | `MissingSystemCodec` | geplant |
| Passwortgeschütztes PDF / verschlüsselte Office-Datei | `ProtectedFile`, keine Umgehung, klare Meldung | geplant |
| Zielordner voll oder schreibgeschützt | Vorab-Warnung (Größenschätzung vs. freier Platz), sonst `InsufficientDiskSpace` / `OutputNotWritable` | geplant |
| Zieldatei existiert | Nummerierung `_1`, `_2`; Überschreiben nur nach Wahl | geplant |
| Abbruch während FFmpeg läuft | Prozess innerhalb 2 s beendet, `.kvertis-tmp` gelöscht | geplant |
| App-Absturz während Konvertierung | Beim nächsten Start werden `.kvertis-tmp`-Dateien im Verlauf bekannter Zielordner gelöscht | geplant |
| Pause eines FFmpeg-Jobs | Prozess suspendiert, CPU-Last fällt; Resume setzt fort | geplant |
| Pfade mit Umlauten, Leerzeichen, sehr langen Namen (> 260 Zeichen) | Funktioniert; lange Pfade über `\\?\`-Präfix | geplant |
| Sehr große Datei (> Limit) | Rückfrage statt Ablehnung | geplant |
| Animiertes GIF/WebP → Einzelbild | Erstes Bild, Hinweis | geplant |
| Bild mit Transparenz → JPG | Hinweis, weißer Hintergrund | geplant |
| Mehrseitiges TIFF → PNG | Nummerierte Dateien | geplant |
| Audio ohne Dauer-Metadaten (Stream-Dump) | Schätzung über Dateigröße, Fortschritt aus verarbeiteter Zeit | geplant |
| Video mit mehreren Tonspuren | Erste Spur, Hinweis; Auswahl Phase 2 | geplant |
| Interlaced-Video | Hinweis, Deinterlace unter „Mehr“ | geplant |
| Zielgröße kleiner als technisch möglich | Meldung mit erreichbarer Mindestgröße | geplant |
| `MaxParallel` während laufender Jobs geändert | Gilt für neue Starts, laufende bleiben | geplant |
| Zwischenablage enthält kein Bild | Strg+V ohne Effekt, kurze Meldung | geplant |
| Ordner mit Unterordnern abgelegt | Rekursiv einlesen, Rückfrage ab 500 Dateien | geplant |
| Systemsprache weder DE noch EN | Fallback EN | geplant |

## Compliance-Tests (automatisch)

- `FfmpegBuildComplianceTests` (Integration): `ffmpeg -version` enthält `--disable-gpl`, nicht `--enable-gpl`, nicht `--enable-nonfree`; `ffmpeg -encoders` enthält kein `libx264`, `libx265`, `libfdk_aac`.
- `NoNetworkCodeTests`: Quelltext von `src/` enthält keine Verwendung von `HttpClient`, `WebRequest`, `Socket`, `Windows.Networking`, `System.Net.Http`.
- `NoForeignBrandTests`: Ressourcen, Presets und Store-Texte enthalten keine Einträge aus einer gepflegten Markenliste (`tools/compliance/brands.txt`, Liste selbst nicht in Ressourcen).
- `LibraryRegistryTests`: Jede `PackageReference` in `Directory.Packages.props` hat eine Zeile in `docs/04-bibliotheken.md`.

## Abdeckung

Ziel: Engine und Queue ≥ 80 % Zeilenabdeckung (coverlet). UI-ViewModels ≥ 60 %. Zahlen werden im CI-Bericht ausgegeben, nicht als Merge-Blocker in Phase 1.
