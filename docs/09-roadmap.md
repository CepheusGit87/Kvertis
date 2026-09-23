# 09 – Roadmap

## Phasen

### Phase 0 – Grundlagen (in Arbeit)

- [x] Doku-Struktur angelegt und mit dem Briefing gefüllt (2026-09-23)
- [x] Agenten definiert unter `.claude/agents/` (2026-09-23)
- [x] Architektur und ADR-001 bis ADR-010 festgelegt (2026-09-23)
- [ ] Lauffähiges Grundgerüst: Solution, `Directory.Build.props`, `Directory.Packages.props`, Projekte Engine / Queue / App / Tests
- [ ] CI: Linux-Job für Engine, Queue und Unit-Tests; Windows-Job für App-Build und Integrationstests
- [ ] Compliance-Skripte (`tools/compliance/`) und `LibraryRegistryTests`
- [ ] FFmpeg-LGPL-Build: Bezugsquelle oder eigener Build, Prüfskript, Quellcode-Angebot

### Phase 1 – Kernfunktionen

Reihenfolge laut Briefing: Bilder → Audio → Video → Dokumente → Feinschliff.

- [ ] Queue: Enqueue, Parallelität, Pause/Abbruch, Gesamtfortschritt, Verlauf
- [ ] Engine: Formaterkennung (Magic Bytes), Eingabeprüfung, Fehlercodes
- [ ] Bilder: Konvertierung, Qualitätsregler, Zielgröße, Metadaten entfernen, Vorschau, HEIC über WIC
- [ ] Basis-UI: Ablagefläche, Job-Karten, Formatvorschlag, Zielordner, Start, Fortschritt, Vertrauenszeile, DE/EN
- [ ] Audio: Konvertierung, Zielgröße über Bitrate, Vorschau (Wellenform, Ausschnitt)
- [ ] Video (Pro): Konvertierung über `*_mf`, VFR-Behandlung, HEVC-Erkennung, Nur-Ton
- [ ] Dokumente: PDF → Text, DOCX/XLSX/PPTX → Text/CSV, Text/Markdown → PDF, Bilder → PDF
- [ ] Zeit- und Größenschätzung mit `SpeedProfile`, Speicherwarnung
- [ ] Presets, „Mehr“-Panel, Dateinamen-Muster
- [ ] Verlauf mit „Nochmal“
- [ ] Fehlermeldungen mit Lösungsvorschlag für alle Fehlercodes (DE/EN)
- [ ] Barrierefreiheit: Screenreader-Durchlauf, Tastatur, hoher Kontrast
- [ ] Animationen (Composition API) und „Animationen reduzieren“
- [ ] Third-Party-Licenses-Seite, generiert aus `04-bibliotheken.md`
- [ ] Freemium: `ILicenseService`, Store-Add-on, Limits
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
| O-01 | **Magick.Native-Paketvariante:** Welche Drittbibliotheken sind enthalten (libheif, libde265, x265, libaom, libraw)? x265 wäre GPL und damit ein Ausschluss der Variante. | `lizenz-waechter` | Bilder-Konvertierung (Wahl des Pakets), HEIC, AVIF |
| O-02 | **FFmpeg-LGPL-Build:** Fertigen Build mit dokumentierter LGPL-Konfiguration beziehen oder selbst bauen (x64 und ARM64)? Wo wird das Quellcode-Angebot veröffentlicht? | Projektinhaber, `lizenz-waechter` | Audio, Video |
| O-03 | **Patente bei Software-Dekodierung** von H.264 (FFmpeg-nativ) und AAC: juristische Einschätzung einholen, ob ein Software-Fallback für die Dekodierung vertretbar ist. Bis dahin: H.264-Dekodierung per D3D11VA bevorzugt, Software-Fallback erlaubt (geringes Risiko laut gängiger Praxis, aber ungeprüft); HEVC ohne Software-Fallback. | Projektinhaber (Anwalt) | nichts akut, Risiko-Entscheidung |
| O-04 | **RAW-Bilder:** Magick.NET/libraw oder WIC mit Raw-Bilderweiterung? | `architekt`, `lizenz-waechter` | RAW-Unterstützung |
| O-05 | **Batch-Limit Gratis:** Vorschlag 5 Dateien pro Durchlauf. | Projektinhaber | Freemium-Umsetzung |
| O-06 | **Datenschutz-URL:** Domain oder Repository-Seite? | Projektinhaber | Store-Einreichung |
| O-07 | **Store-Name reservieren** und Publisher-Identität festlegen. | Projektinhaber | Manifest-Identity |
| O-08 | **Nennung von „Word/Excel/PowerPoint“** in Store-Texten: erlaubt laut Store-Richtlinien oder nur „DOCX/XLSX/PPTX“? | `lizenz-waechter` | Store-Texte |
| O-09 | **Office → PDF layouttreu:** Kein Weg ohne Renderer (ADR-010). Bleibt außerhalb, bis eine lizenzkonforme Lösung existiert. | `architekt` | nichts |
| O-10 | **Entwicklungsumgebung:** Im Cloud-Container kein .NET SDK und kein Windows. Engine/Queue/Tests sollen dort mit installiertem SDK bauen; App nur in Windows-CI und lokal. | Projektinhaber | Grundgerüst-Verifikation |
| O-11 | **Pause per Prozess-Suspend** (`NtSuspendProcess`) ist eine undokumentierte API. Alternative: `DebugActiveProcess` oder Job-Objekte. Vor Umsetzung prüfen, ob Store-Zertifizierung das beanstandet. | `architekt` | Pause laufender Video-Jobs |

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
