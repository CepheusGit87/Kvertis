# Kvertis

Offline-Dateikonverter für Windows (Microsoft Store). Bilder, Audio, Video, Dokumente. Kein Account, keine Cloud, keine Telemetrie.

## Oberste Regel: Rechtssicherheit vor Features

Im Zweifel wird ein Feature weggelassen. Nur MIT/Apache/BSD/MPL/LGPL-Bibliotheken; LGPL nur als separate DLL oder eigener Prozess. Kein GPL. **Kein mitgelieferter Codec mit Patentpool**: eigener FFmpeg-Allowlist-Build (keine H.264/HEVC/AAC-Decoder), H.264/HEVC/AAC/HEIC nur über Windows (Media Foundation, WIC). Bilder über SkiaSharp. Keine fremden Markennamen. Kein Netzwerkcode. Details: `docs/02-rechtssicherheit.md`.

**Jede neue Bibliothek wird sofort in `docs/04-bibliotheken.md` und `docs/CHANGELOG.md` eingetragen. Ohne Eintrag kein Merge.**

## Tech-Stack

C# / .NET 8, WinUI 3 (Windows App SDK), MSIX. SkiaSharp + Windows Imaging Component (Bilder), eigener FFmpeg-LGPL-Build als Prozess (patentfreie Formate), Media Foundation (H.264/HEVC/AAC), PdfPig, PDFsharp, DocumentFormat.OpenXml, Markdig, CommunityToolkit.Mvvm, xUnit.

## Projektstruktur

```
src/Kvertis.Engine    Konvertierung, Formaterkennung, Schätzung  (plattformneutral, kein WinUI)
src/Kvertis.Queue     Job-Warteschlange, Parallelität, Fortschritt (kein WinUI)
src/Kvertis.App       WinUI 3, MVVM, Ressourcen (DE/EN), MSIX
tests/                xUnit-Tests je Projekt
tools/                Skripte, z. B. FFmpeg-Build beziehen und prüfen
docs/                 Dokumentation (Deutsch)
```

## Build und Test

```
dotnet build Kvertis.sln
dotnet test tests/Kvertis.Engine.Tests
dotnet test tests/Kvertis.Queue.Tests
dotnet test --filter Category!=Integration      # ohne FFmpeg-Binärdateien
bash tools/compliance/check.sh                    # Lizenz-, Marken-, Netzwerk- und Ressourcen-Gate (Pflicht vor Merge)
dotnet build Kvertis.Core.slnf                    # Linux: alles außer der WinUI-App
```

Engine, Queue und Tests bauen auf Linux und Windows. `Kvertis.App` (WinUI 3, MSIX) baut nur unter Windows. CI: siehe `.github/workflows/`.

## Konventionen

- Code und Kommentare Englisch, Docs Deutsch. Namespaces `Kvertis.App`, `Kvertis.Engine`, `Kvertis.Queue`.
- Engine und Queue kennen keine UI. Fortschritt über `IProgress<T>`, Abbruch über `CancellationToken`.
- Fehler aus der Engine sind `ConversionErrorCode` plus Details, keine Texte. Übersetzung in der UI über `.resw`.
- Externe Prozesse: immer Timeout, immer beenden bei Abbruch, temporäre Dateien aufräumen.
- Kein `async void` außerhalb von Event-Handlern, kein `.Result`/`.Wait()`.
- Jeder sichtbare Text aus Ressourcen; `AutomationProperties.Name` an allen Bedienelementen.
- Architekturentscheidungen als ADR in `docs/03-architektur.md` (Datum, Entscheidung, Alternativen, Grund).
- Agenten: `.claude/agents/` (architekt, lizenz-waechter, engine-entwickler, ui-entwickler, tester, reviewer, doku-pfleger, store-release). Ablauf je Schritt: architekt → entwickler → tester → lizenz-waechter (bei neuen Bibliotheken) → reviewer → doku-pfleger.

## Doku-Index

- [docs/README.md](docs/README.md) – Übersicht
- [01-anforderungen](docs/01-anforderungen.md) · [02-rechtssicherheit](docs/02-rechtssicherheit.md) · [03-architektur](docs/03-architektur.md) · [04-bibliotheken](docs/04-bibliotheken.md) · [05-formate](docs/05-formate.md)
- [06-design](docs/06-design.md) · [07-store](docs/07-store.md) · [08-testing](docs/08-testing.md) · [09-roadmap](docs/09-roadmap.md) · [10-rechtsmatrix](docs/10-rechtsmatrix.md) · [CHANGELOG](docs/CHANGELOG.md)
