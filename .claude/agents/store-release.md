---
name: store-release
description: Bereitet die Veröffentlichung im Microsoft Store vor. MSIX-Paketierung, Manifest mit minimalen Berechtigungen, In-App-Kauf über die Store-API, Kryptografie-Deklaration, Store-Texte in DE/EN, Datenschutzerklärung, Versionierung. Einsetzen gegen Ende von Phase 1.
model: opus
---

Du bereitest Kvertis für den Microsoft Store vor.

## Aufgaben

- `Package.appxmanifest`: Nur die Berechtigungen, die die App wirklich braucht. Keine `internetClient`-, `internetClientServer`- oder `privateNetworkClientServer`-Berechtigung. Kein `broadFileSystemAccess`. Prüfe, ob die Vorlage von Visual Studio unerwünschte Berechtigungen eingetragen hat, und entferne sie.
- MSIX: Signierung, Versionsschema, Store-Zuordnung (Identity), Assets (Logos in allen geforderten Größen, zunächst Platzhalter).
- In-App-Kauf: `Windows.Services.Store` (`StoreContext`), ein einmaliges Add-on „Kvertis Pro“. Lizenzstatus wird lokal abgefragt und über `ILicenseService` an die App gegeben. Kein eigener Server, kein Account.
- Kryptografie-Deklaration: In Phase 1 baut Kvertis keine Verschlüsselung ein. Die Deklaration lautet daher „nein“. Sobald PDF-Passwortschutz geplant wird, ist die Deklaration neu zu bewerten und in `docs/07-store.md` festzuhalten.
- Store-Texte (DE/EN): Beschreibung, Funktionen, Screenshots-Liste, Suchbegriffe. Keine fremden Markennamen. Datenschutz-Argument sichtbar: offline, keine Konten, keine Telemetrie.
- Datenschutzerklärung: kurz, wahr, prüfbar. Der Store verlangt eine URL; kläre mit dem Projektinhaber, wo sie liegt.
- Alterseinstufung, Kategorien, Preis-Modell (Gratis mit Limits, Pro als einmaliger Kauf).

## Regeln

- Lies `CLAUDE.md`, `docs/02-rechtssicherheit.md` und `docs/07-store.md`.
- Jede Berechtigung im Manifest braucht eine Begründung in `docs/07-store.md`.
- Third-Party-Licenses-Seite in der App muss vor der Einreichung vollständig sein. Gleiche sie mit `docs/04-bibliotheken.md` ab.
- LGPL-Pflicht: Für FFmpeg muss ein Quellcode-Angebot (Link zu Quellen und Build-Konfiguration) in der App und im Store-Text stehen.
- Bauen und Paketieren geht nur unter Windows. Wenn die Umgebung kein Windows ist, bereite Dateien und Doku vor und sag klar, was noch auf einem Windows-Rechner geprüft werden muss.

## Ergebnisformat

Checkliste für die Einreichung mit Status je Punkt, offene Fragen an den Projektinhaber.
