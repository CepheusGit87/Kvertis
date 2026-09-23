# 01 – Anforderungen

## Ziel

Kvertis ist eine native Windows-App, die Bilder, Audio, Video und Dokumente lokal konvertiert. Vollständig offline, kein Account, keine Cloud, keine Telemetrie. Verkauf als Freemium im Microsoft Store: Gratis mit Limits, Pro als einmaliger In-App-Kauf. Start im deutschsprachigen Raum, später weltweit.

## Zielgruppe

Laien. Menschen, die eine Datei „in ein anderes Format“ bringen wollen, ohne Codecs, Bitraten oder Farbräume zu kennen. Daraus folgt:

- Der Hauptweg hat höchstens drei Klicks: Datei rein → Format bestätigen → Start.
- Die App schlägt das passende Ausgabeformat vor. Der Nutzer bestätigt nur.
- Erweiterte Einstellungen sind vorhanden, aber hinter „Mehr“ versteckt.
- Fehlermeldungen erklären, was passiert ist und was der Nutzer tun kann.
- Einfachheit hat Vorrang vor Funktionsvielfalt. Ein Feature, das den Hauptweg komplizierter macht, wird nicht gebaut.

## Name und Marke

Produktname: **Kvertis**. Er wird konsistent verwendet in App, Manifest, Store-Texten, Doku und Namespaces (`Kvertis.App`, `Kvertis.Engine`, `Kvertis.Queue`). Keine fremden Markennamen irgendwo im Projekt. Logo und Icon sind eigenständig; zunächst Platzhalter, später ersetzbar.

## Kernfunktionen Phase 1

| Nr. | Funktion | Details |
|---|---|---|
| 1 | Dateien hineingeben | Drag-and-drop und Dateidialog, auch mehrere Dateien und ganze Ordner. Zwischenablage (Strg+V) für Bilder. |
| 2 | Formaterkennung und Vorschlag | Erkennung anhand des Dateiinhalts (nicht nur Endung). Vorschlag passender Ausgabeformate, Standardvorschlag mit einem Klick bestätigen. Erweiterte Einstellungen hinter „Mehr“. |
| 3 | Warteschlange | Mehrere Jobs parallel (Anzahl nach CPU-Kernen, manuell änderbar). Fortschritt pro Datei, einzeln pausieren und abbrechen, Gesamtfortschritt. |
| 4 | Ausgabeordner | Wählbar und merkbar. Optionen „gleicher Ordner wie Eingabe“ und „Unterordner“. Dateinamen-Muster `{name}_{datum}_{format}` mit Nummerierung bei Batches. Vorhandene Dateien werden nie still überschrieben. |
| 5 | Zeitschätzung | Audio/Video: anhand Dauer, Auflösung und gemessener Geschwindigkeit, laufend verfeinert. Bilder/Dokumente: nach Anzahl und Größe. Vorab-Größenschätzung und Warnung bei knappem Speicher. |
| 6 | Komprimierung | Regler „Qualität vs. Größe“ und Zielgröße-Eingabe („max. 25 MB“); die App berechnet die Einstellungen. Phase 1: Bilder und Audio. Phase 2: Video per Two-Pass. |
| 7 | Ziel-Presets | „Für Messenger“, „Für E-Mail“, „Für Social Media“, „Für Website“, „Archiv (verlustfrei)“. Keine Produktnamen. |
| 8 | Vorschau | Vorher/Nachher mit Größenvergleich vor dem Start. |
| 9 | Metadaten | EXIF/GPS übernehmen oder entfernen. Standard: entfernen (Datenschutz-Argument). |
| 10 | Verlauf | Liste der letzten Konvertierungen mit „Nochmal mit denselben Einstellungen“. |
| 11 | Fehlermeldungen | Verständlich, mit Lösungsvorschlag: beschädigte Datei, fehlender Codec, VFR-Video, geschützte Datei, zu wenig Speicher. |
| 12 | Vertrauenshinweis | Sichtbar in der App: „Ihre Dateien verlassen nie diesen PC.“ |

Weitere Anforderungen ab Tag 1:

- Mehrsprachigkeit DE/EN über Ressourcendateien; weitere Sprachen später.
- Barrierefreiheit: Screenreader, hoher Kontrast, vollständige Tastaturbedienung.
- Dark/Light nach Systemeinstellung.

## Phase 2

- Explorer-Kontextmenü „Mit Kvertis konvertieren“.
- Überwachter Ordner (automatische Konvertierung neuer Dateien).
- Tonspur- und Untertitelauswahl bei Video.
- Sammeln und Zerlegen: Bilder → PDF, PDF → Bilder, Video → GIF oder Bildsequenz.
- Nachher-Aktionen: Ordner öffnen, Original verschieben, PC herunterfahren.
- Video-Zielgröße per Two-Pass.
- Weitere Sprachen.

## Bewusst nicht enthalten

- Cloud-Sync, Online-Downloads, Stream-/URL-Downloads.
- Eingebauter Editor oder Videoschnitt.
- Account-System, Anmeldung, Lizenzserver.
- Werbung, Wasserzeichen, Telemetrie.
- DRM-Umgehung, Entfernen von Passwörtern aus fremden PDFs.

## Monetarisierung

| | Gratis | Pro (einmaliger Kauf) |
|---|---|---|
| Bilder, Audio, Dokumente | ja | ja |
| Video | nein | ja |
| Batch-Größe | begrenzt (Wert siehe `09-roadmap.md`, offener Punkt) | unbegrenzt |
| Werbung / Wasserzeichen | keine | keine |

Der Kauf läuft über die Store-API. Kein eigener Server. Die Limits werden in der App (Queue/UI) durchgesetzt, nicht in der Engine, damit die Engine frei wiederverwendbar bleibt.

## Nicht-funktionale Anforderungen

- Die App startet in unter 2 Sekunden auf aktueller Hardware.
- Eine Konvertierung blockiert die UI nie.
- Abbruch beendet externe Prozesse innerhalb von 2 Sekunden und räumt temporäre Dateien auf.
- Keine Datei wird ohne ausdrückliche Wahl des Nutzers gelöscht oder überschrieben.
- Kein Netzwerkzugriff. Prüfbar über Manifest und Code-Review.
