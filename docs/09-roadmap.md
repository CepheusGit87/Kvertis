# 09 – Roadmap

## Phasen

### Phase 0 – Grundlagen (in Arbeit)

- [x] Doku-Struktur angelegt und mit dem Briefing gefüllt (2026-09-23)
- [x] Agenten definiert unter `.claude/agents/` (2026-09-23)
- [x] Architektur und ADR-001 bis ADR-010 festgelegt (2026-09-23)
- [x] Lauffähiges Grundgerüst: Solution, `Directory.Build.props`, `Directory.Packages.props`, Projekte Engine / Queue / App / Tests
- [x] CI: Linux-Job für Engine, Queue und Unit-Tests; Windows-Job für App-Build und Integrationstests
- [x] Compliance-Skripte (`tools/compliance/`) und `LibraryRegistryTests`
- [ ] FFmpeg-LGPL-Build: Bezugsquelle oder eigener Build, Prüfskript, Quellcode-Angebot

### Phase 1 – Kernfunktionen

Reihenfolge laut Briefing: Bilder → Audio → Video → Dokumente → Feinschliff.

- [x] Queue: Enqueue, Parallelität, Pause/Abbruch, Gesamtfortschritt, Verlauf
- [x] Engine: Formaterkennung (Magic Bytes), Eingabeprüfung, Fehlercodes
- [x] Bilder: Konvertierung, Qualitätsregler, Zielgröße, Metadaten entfernen, Vorschau, HEIC über WIC
- [x] Basis-UI (geschrieben, Windows-Build steht aus): Ablagefläche, Job-Karten, Formatvorschlag, Zielordner, Start, Fortschritt, Vertrauenszeile, DE/EN
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

## Offene Punkte

| Nr. | Punkt | Wer entscheidet | Blockiert |
|---|---|---|---|
| O-01 | **Magick.Native enthält Video-Codecs:** Prüfung erledigt (`04-bibliotheken.md`): kein GPL, aber `libde265` (HEVC-Decoder) und `openh264` statisch eingebettet, obwohl Kvertis sie nie aufruft. **Entscheidung nötig:** (a) Patentrisiko akzeptieren und dokumentieren oder (b) Bildpfad auf SkiaSharp (MIT, ohne Video-Codecs) + Windows Imaging Component umstellen (Aufwand: Bild-Konverter neu, TIFF/PSD/SVG-Umfang schrumpft). Bis dahin: HEIC nur über WIC, AVIF gesperrt. | **Projektinhaber** | Store-Einreichung |
| O-02 | **FFmpeg-LGPL-Build:** Fertigen Build mit dokumentierter LGPL-Konfiguration beziehen oder selbst bauen (x64 und ARM64)? Wo wird das Quellcode-Angebot veröffentlicht? | Projektinhaber, `lizenz-waechter` | Audio, Video |
| O-03 | **Patente bei Software-Dekodierung** von H.264 (FFmpeg-nativ) und AAC: juristische Einschätzung einholen, ob ein Software-Fallback für die Dekodierung vertretbar ist. Bis dahin: H.264-Dekodierung per D3D11VA bevorzugt, Software-Fallback erlaubt (geringes Risiko laut gängiger Praxis, aber ungeprüft); HEVC ohne Software-Fallback. | Projektinhaber (Anwalt) | nichts akut, Risiko-Entscheidung |
| O-04 | **RAW-Bilder:** Magick.NET/libraw oder WIC mit Raw-Bilderweiterung? | `architekt`, `lizenz-waechter` | RAW-Unterstützung |
| O-05 | **Batch-Limit Gratis:** Vorschlag 5 Dateien pro Durchlauf. | Projektinhaber | Freemium-Umsetzung |
| O-06 | **Datenschutz-URL:** Domain oder Repository-Seite? | Projektinhaber | Store-Einreichung |
| O-07 | **Store-Name reservieren** und Publisher-Identität festlegen. | Projektinhaber | Manifest-Identity |
| O-08 | **Nennung von „Word/Excel/PowerPoint“** in Store-Texten: erlaubt laut Store-Richtlinien oder nur „DOCX/XLSX/PPTX“? | `lizenz-waechter` | Store-Texte |
| O-09 | **Office → PDF layouttreu:** Kein Weg ohne Renderer (ADR-010). Bleibt außerhalb, bis eine lizenzkonforme Lösung existiert. | `architekt` | nichts |
| O-10 | **Entwicklungsumgebung:** Im Cloud-Container kein .NET SDK und kein Windows. Engine/Queue/Tests sollen dort mit installiertem SDK bauen; App nur in Windows-CI und lokal. | Projektinhaber | Grundgerüst-Verifikation |
| O-11 | **Pause per Prozess-Suspend** (`NtSuspendProcess`) ist umgesetzt, aber eine undokumentierte API. Vor der Store-Einreichung prüfen, ob die Zertifizierung das beanstandet; Alternative: Job-Objekte + `SuspendThread`. | `architekt`, `store-release` | Store-Einreichung |
| O-12 | **HEVC-Hardware-Dekodierung** wird über Media Foundation nur angenähert geprüft; ffmpeg nutzt D3D11VA. Der `HevcFallbackGuard` (ADR-011) fängt den Software-Fallback ab, muss aber unter Windows mit echten Dateien verifiziert werden. | `tester` (Windows) | Video Phase 1 |
| O-13 | **Monospace-Schrift für TXT → PDF:** Unter Windows wird ohne freie Mono-Schrift die Sans-Schrift genutzt. Vorschlag: eine OFL-lizenzierte Mono-Schrift mitliefern (Lizenz eintragen). | `lizenz-waechter` | Textqualität |
| O-14 | **Windows-Build der App** ist noch nie gelaufen (nur C#-Teile gegen WinUI-Assemblies kompiliert). Erster Windows-CI-Lauf bzw. lokaler Build unter Windows nötig; XAML-Fehler sind wahrscheinlich und schnell behebbar. | Projektinhaber (Windows-Rechner) oder Windows-CI | Alles Sichtbare |
| O-15 | **Platzhalter im Code:** Publisher `CN=Kvertis`, Store-Add-on-ID `9NXXXXXXXXXX`, Logos. | Projektinhaber | Store-Einreichung |

## Verworfen wegen Rechtsrisiko

| Feature | Grund | Datum |
|---|---|---|
| HEIC-Schreiben | HEVC-Encoder (x265 GPL, MF-HEVC-Encoder nicht für Bilder nutzbar ohne Zusatzaufwand); Patentlage | 2026-09-23 |
| Mitgelieferter HEVC-Software-Decoder | Patentlage HEVC | 2026-09-23 |
| Passwort aus PDF entfernen | Briefing, Rechtslage | 2026-09-23 |
| URL-/Stream-Downloads | Briefing, kein Netzwerk | 2026-09-23 |

## Erledigte Meilensteine

| Datum | Meilenstein |
|---|---|
| 2026-09-23 | Projektstart: Doku, Agenten, Architektur |
| 2026-09-23 | Grundgerüst komplett: Engine (Bilder, Audio, Video, Dokumente), Queue, Windows-Plattform, WinUI-3-App (ungebaut), CI, Compliance-Gate, Code-Review mit 21 behobenen Befunden |
