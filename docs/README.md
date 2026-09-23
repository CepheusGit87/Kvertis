# Kvertis – Dokumentation

Kvertis ist ein nativer Offline-Dateikonverter für Windows, der Bilder, Audio, Video und Dokumente lokal umwandelt. Kein Account, keine Cloud, keine Telemetrie. Verkauf als Freemium im Microsoft Store. Zielgruppe sind Laien: Einfachheit geht vor Funktionsvielfalt, und Rechtssicherheit geht vor Features.

Kurzfassung der Regeln und der Einstieg für Entwickler stehen in [`../CLAUDE.md`](../CLAUDE.md).

## Inhalt

| Datei | Inhalt |
|---|---|
| [01-anforderungen.md](01-anforderungen.md) | Ziel, Zielgruppe, Kernfunktionen Phase 1 und 2, was bewusst nicht enthalten ist. |
| [02-rechtssicherheit.md](02-rechtssicherheit.md) | Lizenzregeln, Codec- und Patentregeln, verbotene Funktionen, Store-Vorgaben. |
| [03-architektur.md](03-architektur.md) | Schichten UI / Engine / Queue, Projektstruktur, Datenfluss, Schnittstellen, Architekturentscheidungen (ADR). |
| [04-bibliotheken.md](04-bibliotheken.md) | Tabelle aller Bibliotheken mit Version, Lizenz, Zweck und Einbindungsart. |
| [05-formate.md](05-formate.md) | Matrix Eingabeformat → Ausgabeformate, Standardvorschlag, zuständige Engine. |
| [06-design.md](06-design.md) | UI-Konzept, Screens, Farben, Animationen, Barrierefreiheit. |
| [07-store.md](07-store.md) | Manifest-Berechtigungen, MSIX, In-App-Kauf, Kryptografie-Deklaration, Store-Texte. |
| [08-testing.md](08-testing.md) | Teststrategie, Testdateien, bekannte Grenzfälle. |
| [09-roadmap.md](09-roadmap.md) | Phasen, offene Punkte, erledigte Meilensteine. |
| [10-rechtsmatrix.md](10-rechtsmatrix.md) | Pro Konverter: Bibliothek, Lizenz, Patentlage, Begründung, Nachweis. Interne Prüfunterlage. |
| [CHANGELOG.md](CHANGELOG.md) | Was wurde wann geändert, welche Bibliothek mit welcher Lizenz kam hinzu. |

## Regeln für die Doku

- Docs auf Deutsch, Code und Kommentare auf Englisch.
- Jede neue Bibliothek wird sofort in `04-bibliotheken.md` und `CHANGELOG.md` eingetragen. Ohne Eintrag kein Merge.
- Architekturentscheidungen stehen in `03-architektur.md` mit Datum, Entscheidung, Alternativen und Grund.
- Keine fremden Markennamen, auch nicht in der Doku.
