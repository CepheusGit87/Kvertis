# Changelog

Format: Datum, Änderung, neue oder entfernte Bibliotheken mit Lizenz. Neueste Einträge oben.

## 2026-09-23 – Projektstart

**Geändert**

- Dokumentationsstruktur unter `docs/` angelegt und mit dem Briefing gefüllt (Anforderungen, Rechtssicherheit, Architektur, Bibliotheken, Formate, Design, Store, Testing, Roadmap).
- `CLAUDE.md` mit oberster Regel, Stack, Build-Befehlen, Konventionen und Doku-Index.
- Agenten unter `.claude/agents/`: `architekt`, `lizenz-waechter`, `reviewer` (Fable), `engine-entwickler`, `ui-entwickler`, `tester`, `store-release` (Opus), `doku-pfleger` (Sonnet).
- Architekturentscheidungen ADR-001 bis ADR-010 in `03-architektur.md`.

**Bibliotheken**

- Noch keine im Code. Geplante Bibliotheken mit Lizenz stehen in `04-bibliotheken.md` (Status `geplant`).
- Abgelehnt: QuestPDF (Umsatzgrenze in der Community-Lizenz) zugunsten von PDFsharp (MIT); FluentAssertions ≥ 8 zugunsten von Shouldly (BSD-3).
