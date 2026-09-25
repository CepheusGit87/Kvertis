# 09 – Roadmap

## Phasen

### Phase 0 – Grundlagen (in Arbeit)

- [x] Doku-Struktur angelegt und mit dem Briefing gefüllt (2026-09-23)
- [x] Agenten definiert unter `.claude/agents/` (2026-09-23)
- [x] Architektur und ADR-001 bis ADR-010 festgelegt (2026-09-23)
- [x] Lauffähiges Grundgerüst: Solution, `Directory.Build.props`, `Directory.Packages.props`, Projekte Engine / Queue / App / Tests
- [x] CI: Linux-Job für Engine, Queue und Unit-Tests; Windows-Job für App-Build und Integrationstests
- [x] Compliance-Skripte (`tools/compliance/`) und `LibraryRegistryTests`
- [x] Umstellung von .NET 8 auf .NET 10 (LTS bis November 2028), alle Projekte und CI (2026-09-25, Nachtrag zu ADR-001)
- [ ] FFmpeg-LGPL-Build: Bezugsquelle oder eigener Build, Prüfskript, Quellcode-Angebot

### Phase 1 – Kernfunktionen

Reihenfolge laut Briefing: Bilder → Audio → Video → Dokumente → Feinschliff.

- [x] Queue: Enqueue, Parallelität, Pause/Abbruch, Gesamtfortschritt, Verlauf
- [x] Engine: Formaterkennung (Magic Bytes), Eingabeprüfung, Fehlercodes
- [x] Bilder: Konvertierung, Qualitätsregler, Zielgröße, Metadaten entfernen, Vorschau, HEIC über WIC
- [x] Basis-UI (baut und startet unter Windows seit 2026-09-25): Ablagefläche, Job-Karten, Formatvorschlag, Zielordner, Start, Fortschritt, Vertrauenszeile, DE/EN
- [x] Audio: Konvertierung, Zielgröße über Bitrate, Vorschau (Wellenform, Ausschnitt)
- [x] Video (Pro): Konvertierung über `*_mf`, VFR-Behandlung, HEVC-Erkennung, Nur-Ton
- [x] Dokumente: PDF → Text, DOCX/XLSX/PPTX → Text/CSV, Text/Markdown → PDF, Bilder → PDF
- [x] Zeit- und Größenschätzung mit `SpeedProfile`, Speicherwarnung
- [x] Presets, „Mehr“-Panel, Dateinamen-Muster
- [x] Verlauf mit „Nochmal“
- [x] Fehlermeldungen mit Lösungsvorschlag für alle Fehlercodes (DE/EN)
- [ ] Barrierefreiheit: Screenreader-Durchlauf, Tastatur, hoher Kontrast (Code vorhanden, manueller Durchlauf unter Windows offen)
- [x] Animationen (Composition API) und „Animationen reduzieren“ (ungeprüft unter Windows)
- [x] Third-Party-Licenses-Seite, generiert aus `04-bibliotheken.md`
- [x] Freemium: `ILicenseService`, Store-Add-on, Limits
- [ ] Store-Einreichung nach Checkliste in `07-store.md`

### Phase 2

- [ ] Explorer-Kontextmenü „Mit Kvertis konvertieren“
- [ ] Überwachter Ordner
- [ ] Tonspur- und Untertitelauswahl
- [ ] Sammeln/Zerlegen: Bilder → PDF (Batch), PDF → Bilder, Video → GIF/Bildsequenz
- [ ] Nachher-Aktionen: Ordner öffnen, Original verschieben, PC herunterfahren
- [ ] Video-Zielgröße per Two-Pass
- [ ] AV1-Ausgabe (SVT-AV1), wenn Tempo für Laien akzeptabel
- [ ] Weitere Sprachen
- [x] 3D-Modelle: STL, 3MF, OBJ, PLY, glTF/GLB untereinander, eigene Leser und Schreiber, gratis (ADR-016, 2026-09-23)
- [ ] 3D-Vorschau (bräuchte einen Renderer; zusammen mit der Vorschau für Dokumente und Ton)

### Ideen (geprüft, rechtlich unbedenklich, noch nicht eingeplant)

Stand 2026-09-23. Alle Punkte nutzen offene Formate, eigenen Code oder bereits vorhandene Bibliotheken. Vor der Umsetzung gilt wie immer: neue Bibliothek → `lizenz-waechter`, Eintrag in `04-bibliotheken.md` und `CHANGELOG.md`; neuer Konverter → Zeile in `10-rechtsmatrix.md`. Keine Programm- oder Herstellernamen in UI und Store-Texten.

| Prio | Idee | Umsetzung | Voraussetzung |
|---|---|---|---|
| 1 | PDFs zusammenfügen und aufteilen | PDFsharp (vorhanden); verschlüsselte PDFs weiter `ProtectedFile` | O-19 (Sammel-Jobs) |
| 1 | Bilder → animiertes GIF | FFmpeg-Prozess mit `palettegen`/`paletteuse`/`split`; Einstellungen: Reihenfolge (natürliche Sortierung, änderbar), Anzeigedauer, Endlosschleife, max. Breite, Einpassen unterschiedlicher Größen, Obergrenze Bildanzahl, Größenwarnung | O-19, O-20 |
| 1 | GIF / animiertes WebP → Einzelbilder (PNG/JPG/WebP) | SkiaSharp `SKCodec` je Bild, Zusammensetzen über Vorgängerbild; nummerierte Dateien wie mehrseitiges TIFF; Auswahl alle / jedes n-te / einzelnes Bild | – |
| 1 | Kontakte vCard (.vcf) ↔ CSV/XLSX, Kalender iCalendar (.ics) ↔ CSV/XLSX | eigener Code (RFC-Formate), OpenXml (vorhanden) | – |
| 2 | Untertitel SRT ↔ VTT (↔ ASS) | eigener Code, reiner Text | – |
| 2 | ODT/ODS/ODP → TXT/CSV/Markdown, EPUB → TXT/Markdown/HTML, RTF → TXT | eigener Code (ZIP + XML); EPUB mit DRM → `ProtectedFile` | – |
| 2 | CSV → XLSX, Markdown → DOCX, JSON/XML ↔ CSV/XLSX | OpenXml, Markdig (vorhanden) | – |
| 3 | GPS-Tracks GPX ↔ KML ↔ CSV | eigener Code; keine Hersteller-Formate | – |
| 3 | XPS/OXPS → PDF, E-Mails EML/MSG → PDF/TXT, Comics CBZ → PDF | offen dokumentierte Formate; CBR (RAR) ausgeschlossen | – |
| 3 | Favicon-Paket (ICO mit mehreren Größen + PNG-Sätze), QOI, TGA | eigener ICO-Writer (vorhanden) | – |
| 3 | Audio: ALAC, WavPack; Lautstärke angleichen | FFmpeg-Decoder bzw. Filter `loudnorm` in der Allowlist | Allowlist-Erweiterung |
| 3 | Schriften TTF/OTF ↔ WOFF/WOFF2 | Brotli (MIT, neue Bibliothek); Hinweis, dass Schriftlizenzen Umwandlung verbieten können | `lizenz-waechter` |
| offen | Texterkennung aus Bildern/Scans | nur über die eingebaute Windows-Texterkennung (`Windows.Media.Ocr`), kein mitgeliefertes Modell | Entscheidung Projektinhaber |

## Offene Punkte

| Nr. | Punkt | Wer entscheidet | Blockiert |
|---|---|---|---|
| O-01 | **Erledigt 2026-09-23:** Magick.NET entfernt, Bildpfad auf SkiaSharp (geprüft) + Windows-Bildkomponente umgestellt (ADR-014). | — | — |
| O-02 | **FFmpeg-Build erzeugen:** Workflow `.github/workflows/ffmpeg-build.yml` (Allowlist, x64) einmal manuell starten, Artefakt prüfen (`check-build.ps1`), Binärdateien unter `src/Kvertis.App/Tools/ffmpeg/x64/` ablegen, Quellcode-Angebot archivieren. ARM64 offen. | Projektinhaber (Workflow starten), `lizenz-waechter` | Audio, Video |
| O-03 | **Erledigt 2026-09-23 (ohne Anwalt):** Die Patente für MP3 (2017), MPEG-1/2 (2018) und AC-3 (2017) sind abgelaufen und gut belegt; die Decoder bleiben im Build. Alle H.264/HEVC/AAC-Fragen sind durch ADR-015 gegenstandslos. | — | — |
| O-04 | **RAW-Bilder:** Magick.NET/libraw oder WIC mit Raw-Bilderweiterung? | `architekt`, `lizenz-waechter` | RAW-Unterstützung |
| O-05 | **Batch-Limit Gratis:** Vorschlag 5 Dateien pro Durchlauf. | Projektinhaber | Freemium-Umsetzung |
| O-06 | **Datenschutz-URL:** Domain oder Repository-Seite? | Projektinhaber | Store-Einreichung |
| O-07 | **Store-Name reservieren** und Publisher-Identität festlegen. | Projektinhaber | Manifest-Identity |
| O-08 | **Erledigt 2026-09-23:** Store-Texte und UI nennen nur Formatnamen („DOCX, XLSX, PPTX“), keine Office-Produktnamen. Damit ist keine Prüfung der Store-Richtlinien nötig. | — | — |
| O-09 | **Office → PDF layouttreu:** Kein Weg ohne Renderer (ADR-010). Bleibt außerhalb, bis eine lizenzkonforme Lösung existiert. | `architekt` | nichts |
| O-10 | **Entwicklungsumgebung:** Im Cloud-Container kein .NET SDK und kein Windows. Engine/Queue/Tests sollen dort mit installiertem SDK bauen; App nur in Windows-CI und lokal. | Projektinhaber | Grundgerüst-Verifikation |
| O-11 | **Pause per Prozess-Suspend** (`NtSuspendProcess`) ist umgesetzt, aber eine undokumentierte API. Vor der Store-Einreichung prüfen, ob die Zertifizierung das beanstandet; Alternative: Job-Objekte + `SuspendThread`. | `architekt`, `store-release` | Store-Einreichung |
| O-12 | **Media-Foundation-Transcoder verifizieren** (nur unter Windows möglich): Bitrate bei Auto-Profil, Video-Eingabe mit Audio-Profil, stummes Video, HRESULT-Zuordnung, Zugriff auf beliebige Pfade im MSIX, Metadaten-Verhalten (MediaTranscoder hat keinen Strip-Schalter, Phase 2). | `tester` (Windows) | Video Phase 1 |
| O-13 | **Monospace-Schrift für TXT → PDF:** Unter Windows wird ohne freie Mono-Schrift die Sans-Schrift genutzt. Vorschlag: eine OFL-lizenzierte Mono-Schrift mitliefern (Lizenz eintragen). | `lizenz-waechter` | Textqualität |
| O-14 | **Erledigt 2026-09-25:** Erster Windows-Build und Start der App auf dem Entwicklungsrechner (.NET SDK 10, Windows App SDK 2.5.1), ohne XAML-Fehler. Unverpackter Start ohne Entwicklermodus über `PackageInfo`-Rückfall; MSIX-Registrierung braucht den Entwicklermodus. | — | — |
| O-16 | **UI: Ausgabeliste** filtert jetzt über `IConverterResolver.CanConvert`; die MKV-Ausgabeliste hängt damit vom Codec ab. Verhalten unter Windows prüfen. | `ui-entwickler` | UI |
| O-15 | **Platzhalter im Code:** Publisher `CN=Kvertis`, Store-Add-on-ID `9NXXXXXXXXXX`, Logos. | Projektinhaber | Store-Einreichung |
| O-17 | **Pflichten als Verkäufer, ohne Anwalt abgearbeitet** (Checkliste in `02-rechtssicherheit.md`, Abschnitt 10): Rechtstexte (Impressum, AGB, Widerruf, Datenschutz) über ein Rechtstexte-Abo mit Aktualisierung; Update-Zusage für Sicherheits-Updates veröffentlichen (§ 327f BGB); Cyber Resilience Act: SBOM im Build, Sicherheitskontakt, Ablauf für Schwachstellen, Einordnung und CE-Selbstbewertung anhand der offiziellen EU-Leitfäden prüfen; Produkthaftung technisch absichern (Original wird nie überschrieben). Fristen und Einordnung vor dem Verkaufsstart anhand der amtlichen Quellen nachprüfen. | Projektinhaber, `store-release` | Verkaufsstart |
| O-18 | **Erledigt 2026-09-23:** E-Rechnung → PDF/HTML verworfen (Haftung ohne Anwaltsprüfung nicht einschätzbar), siehe „Verworfen wegen Rechtsrisiko“. | — | — |
| O-19 | **ADR Sammel-Jobs** (viele Eingaben → eine Ausgabe) in Queue und UI. Gemeinsame Grundlage für PDFs zusammenfügen, Bilder → GIF, Bilder → PDF (Batch), später ICO mit mehreren Größen. | `architekt` | diese Funktionen |
| O-20 | **FFmpeg-Allowlist ergänzen** um die Filter `palettegen`, `paletteuse`, `split` (Bilder/Video → GIF), möglichst vor dem ersten Build (O-02). Reiner FFmpeg-Code, LGPL, patentfrei. | `lizenz-waechter` | Bilder → GIF, Video → GIF |

## Verworfen wegen Rechtsrisiko

| Feature | Grund | Datum |
|---|---|---|
| HEIC-Schreiben | HEVC-Encoder (x265 GPL, MF-HEVC-Encoder nicht für Bilder nutzbar ohne Zusatzaufwand); Patentlage | 2026-09-23 |
| Mitgelieferter HEVC-Software-Decoder | Patentlage HEVC | 2026-09-23 |
| Magick.NET (ImageMagick) | Magick.Native enthält libde265 und openh264 | 2026-09-23 |
| H.264/HEVC/AAC/MPEG-4/WMV-Decoder in FFmpeg | Patentpools; Verbreitung genügt | 2026-09-23 |
| PSD, SVG lesen | keine risikofreie Bibliothek in Phase 1 | 2026-09-23 |
| H.264/HEVC-Eingabe → WebM/MKV (Phase 1) | bräuchte MF-Source-Reader-Pipe; Phase 2 | 2026-09-23 |
| Passwort aus PDF entfernen | Briefing, Rechtslage | 2026-09-23 |
| URL-/Stream-Downloads | Briefing, kein Netzwerk | 2026-09-23 |
| Transkription (Ton → Text) | bräuchte ein mitgeliefertes Sprachmodell; Modelllizenzen teils nicht kommerziell, Kennzeichnung automatisch erzeugter Inhalte, Paketgröße; Entscheidung Projektinhaber | 2026-09-23 |
| RAR und CBR | proprietäres Format, Lizenz der Entpack-Bibliothek schränkt ein | 2026-09-23 |
| MOBI/AZW | Herstellerbindung, Markenbezug, meist DRM | 2026-09-23 |
| PDF/A erzeugen | ohne zuverlässige Prüfung zu fehleranfällig, haftungsnah bei Archivpflichten | 2026-09-23 |
| 3D: FBX, USDZ | proprietär bzw. restriktive SDK-Lizenz, Markenbezug | 2026-09-23 |
| MIDI → Audio | bräuchte Klangbibliothek mit oft unklarer Lizenz | 2026-09-23 |
| E-Rechnung (XML) → PDF/HTML | Haftung bei falsch dargestellten Beträgen oder Bankdaten; ohne Anwaltsprüfung nicht einschätzbar | 2026-09-23 |

## Erledigte Meilensteine

| Datum | Meilenstein |
|---|---|
| 2026-09-23 | Projektstart: Doku, Agenten, Architektur |
| 2026-09-23 | Rechtsrahmen ohne Patentrisiko: Magick.NET → SkiaSharp + WIC, FFmpeg-Allowlist-Build, Media-Foundation-Transcoder, Rechtsmatrix (`10-rechtsmatrix.md`) |
| 2026-09-23 | 3D-Modelle als fünfte Medienart (ADR-016): STL, 3MF, OBJ, PLY, glTF/GLB ohne neue Bibliothek |
| 2026-09-23 | Grundgerüst komplett: Engine (Bilder, Audio, Video, Dokumente), Queue, Windows-Plattform, WinUI-3-App (ungebaut), CI, Compliance-Gate, Code-Review mit 21 behobenen Befunden |
