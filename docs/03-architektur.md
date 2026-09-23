# 03 – Architektur

## Schichten

```
┌──────────────────────────────────────────────────────────┐
│ Kvertis.App  (WinUI 3, MVVM)                             │
│  Views · ViewModels · Ressourcen (.resw) · Store-Anbindung│
│  Übersetzt Fehlercodes in Texte, setzt Freemium-Limits um │
└───────────────▲──────────────────────────────────────────┘
                │ nur über Schnittstellen, kein Engine-Wissen in Views
┌───────────────┴──────────────────────────────────────────┐
│ Kvertis.Queue  (plattformneutral)                         │
│  JobQueue · Scheduler (Parallelität) · Fortschritt/ETA    │
│  Pause · Abbruch · Verlauf                                │
└───────────────▲──────────────────────────────────────────┘
                │ IConverter, IFormatDetector, IInputValidator, IEstimator
┌───────────────┴──────────────────────────────────────────┐
│ Kvertis.Engine  (plattformneutral)                        │
│  Formaterkennung · Eingabeprüfung · Konverter je Kategorie│
│  Schätzung · Zielgrößen-Berechnung · Dateinamen-Muster    │
│  ┌────────────────────────────────────────────────────┐   │
│  │ Kvertis.Engine.Windows  (net8.0-windows)           │   │
│  │  Media Foundation, Windows Imaging Component (HEIC)│   │
│  └────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────┘
        │ Prozessgrenze                 │ NuGet
   ffmpeg.exe / ffprobe.exe         Magick.NET, PdfPig, OpenXml
   (LGPL-Build, separater Prozess)
```

Regeln, die aus dem Bild folgen:

- `Kvertis.Engine` und `Kvertis.Queue` referenzieren kein WinUI, kein Windows App SDK und keinen `DispatcherQueue`. Sie sind reine .NET-8-Klassenbibliotheken und bauen auf Linux.
- Windows-spezifischer Code (Media Foundation, Windows Imaging Component, Store-API) liegt in `Kvertis.Engine.Windows` bzw. `Kvertis.App` und wird über Schnittstellen eingehängt (Dependency Injection).
- Die UI kennt die Engine nur über `Kvertis.Queue` und die Schnittstellen in `Kvertis.Engine.Abstractions`-Namespaces. Sie erzeugt Jobs, beobachtet Fortschritt und zeigt Ergebnisse.
- Freemium-Limits (Batch-Größe, kein Video) werden in `Kvertis.App` beim Erzeugen der Jobs durchgesetzt, nicht in Engine oder Queue.

## Projektstruktur

```
Kvertis.sln
Directory.Build.props                 gemeinsame Einstellungen (LangVersion, Nullable, Warnings as Errors)
Directory.Packages.props              zentrale Paketversionen (Central Package Management)
src/
  Kvertis.Engine/                     net8.0
    Abstractions/                     IConverter, IFormatDetector, IInputValidator, IEstimator, IProcessRunner
    Formats/                          FormatId, FormatRegistry, MediaKind, Magic-Byte-Tabellen
    Validation/                       InputValidator, Grenzwerte
    Conversion/
      Images/                         ImageConverter (Magick.NET)
      Audio/                          AudioConverter (FFmpeg)
      Video/                          VideoConverter (FFmpeg + MF-Encoder)
      Documents/                      PdfConverter, OfficeConverter
    Estimation/                       TimeEstimator, SizeEstimator, SpeedProfile
    Naming/                           OutputNamePattern
    Ffmpeg/                           FfmpegLocator, FfmpegCommandBuilder, FfprobeReader
    Errors/                           ConversionErrorCode, ConversionException
  Kvertis.Engine.Windows/             net8.0-windows10.0.19041.0
    Codecs/                           MediaFoundationCapabilities (welche MF-Encoder verfügbar sind)
    Imaging/                          WicHeicDecoder (HEIC über Windows Imaging Component)
  Kvertis.Queue/                      net8.0
    ConversionJob, JobState, JobQueue, JobScheduler, ProgressAggregator, JobHistory
  Kvertis.App/                        net8.0-windows10.0.19041.0, WinUI 3, MSIX
    Views/  ViewModels/  Services/  Strings/de-DE/  Strings/en-US/  Assets/
tests/
  Kvertis.Engine.Tests/               xUnit, NSubstitute, Shouldly
  Kvertis.Queue.Tests/
  Kvertis.App.Tests/                  ViewModel-Tests (nur Windows)
  TestFiles/                          kleine, selbst erzeugte Testdateien
tools/
  ffmpeg/                             Bezugsquelle, Build-Konfiguration, Prüfskript für den LGPL-Build
third_party/                          Lizenztexte aller Bibliotheken
docs/
```

## Datenfluss

1. **Eingabe:** Nutzer gibt Dateien per Drag-and-drop, Dialog oder Zwischenablage. `Kvertis.App` legt für jede Datei einen `ConversionRequest` an (Pfad, noch kein Format).
2. **Erkennung:** `IFormatDetector` liest die ersten Bytes, bestimmt `FormatId` und `MediaKind`. `IInputValidator` prüft Größe, Header, Schutz (Passwort, DRM) und läuft `ffprobe`/Magick-Header mit Timeout. Ergebnis: `InputInfo` (Format, Dauer, Auflösung, Kanäle, Seitenzahl, Warnungen wie `VariableFrameRate`).
3. **Vorschlag:** `FormatRegistry.SuggestOutputs(InputInfo)` liefert die möglichen Ausgabeformate mit einem Standardvorschlag. Presets (`ConversionPreset`) setzen Qualität, Zielgröße und Metadaten-Richtlinie vor.
4. **Schätzung:** `IEstimator` berechnet Zeit und Ausgabegröße aus `InputInfo`, Einstellungen und dem gemessenen `SpeedProfile`. Bei knappem Speicher im Zielordner erzeugt die App eine Warnung.
5. **Vorschau (Bilder, Audio):** Der Konverter erzeugt auf Wunsch eine Vorschau in eine temporäre Datei (verkleinert oder ein kurzer Ausschnitt). Die UI zeigt Vorher/Nachher mit Größenvergleich.
6. **Warteschlange:** `JobQueue.Enqueue(ConversionJob)`. Der `JobScheduler` startet bis zu `MaxParallel` Jobs (Standard: `Environment.ProcessorCount`, mindestens 1, für Video halbiert, manuell änderbar). Jeder Job läuft in einem eigenen `Task` mit `CancellationTokenSource` und meldet `ConversionProgress` (Prozent, Phase, verarbeitete Einheiten, geschätzte Restzeit).
7. **Konvertierung:** Der zuständige `IConverter` schreibt zuerst in eine temporäre Datei im Zielordner (`.kvertis-tmp`), benennt bei Erfolg atomar um und wendet die Metadaten-Richtlinie an. Externe Prozesse laufen über `IProcessRunner` mit Timeout und werden bei Abbruch beendet.
8. **Ergebnis:** `ConversionResult` (Erfolg, Ausgabepfad, Größe vorher/nachher, Dauer) oder `ConversionErrorCode` plus Details. Die Queue aktualisiert den Gesamtfortschritt und den Verlauf (`JobHistory`, lokal als JSON). Das gemessene Tempo fließt in das `SpeedProfile` zurück.
9. **Anzeige:** Die UI übersetzt Fehlercodes über `.resw` in verständliche Texte mit Lösungsvorschlag.

## Zentrale Schnittstellen (Skizze)

Die Namen sind verbindlich; Details dürfen `engine-entwickler` und `architekt` gemeinsam anpassen, mit Eintrag hier.

```csharp
namespace Kvertis.Engine.Abstractions;

public enum MediaKind { Image, Audio, Video, Document, Unknown }

public readonly record struct FormatId(string Id);            // "png", "mp4", "pdf"; lowercase, stable keys

public sealed record InputInfo(
    string Path, FormatId Format, MediaKind Kind, long SizeBytes,
    TimeSpan? Duration, int? Width, int? Height, int? PageCount,
    IReadOnlyList<InputWarning> Warnings);                     // e.g. VariableFrameRate, ExtensionMismatch

public sealed record ConversionSettings(
    FormatId Output,
    QualityLevel Quality,                                        // slider 0..100, mapped per converter
    long? TargetSizeBytes,                                       // "max. 25 MB"; converter derives parameters
    MetadataPolicy Metadata,                                     // Strip (default) | Keep
    IReadOnlyDictionary<string, string> Advanced);               // format-specific keys, kept small

public sealed record ConversionProgress(double Fraction, ConversionPhase Phase, TimeSpan? Remaining);

public sealed record ConversionResult(
    string OutputPath, long InputBytes, long OutputBytes, TimeSpan Elapsed);

public interface IFormatDetector
{
    Task<InputInfo> DetectAsync(string path, CancellationToken ct);
}

public interface IInputValidator
{
    Task<ValidationOutcome> ValidateAsync(InputInfo info, CancellationToken ct);  // Ok | Rejected(ConversionErrorCode)
}

public interface IConverter
{
    bool Supports(InputInfo input, FormatId output);
    Task<ConversionResult> ConvertAsync(InputInfo input, string outputPath, ConversionSettings settings,
                                        IProgress<ConversionProgress> progress, CancellationToken ct);
    Task<PreviewResult?> PreviewAsync(InputInfo input, ConversionSettings settings, CancellationToken ct);
}

public interface IEstimator
{
    Estimate Estimate(InputInfo input, ConversionSettings settings);   // time + output size, with confidence
    void Record(InputInfo input, ConversionSettings settings, ConversionResult result);  // feeds SpeedProfile
}

public interface IProcessRunner                                         // wraps ffmpeg/ffprobe; mockable in tests
{
    Task<ProcessOutcome> RunAsync(ProcessRequest request, IProgress<string>? stderrLines, CancellationToken ct);
}

public enum ConversionErrorCode
{
    None, UnsupportedFormat, CorruptFile, ProtectedFile, MissingSystemCodec, VariableFrameRateUnsupported,
    InsufficientDiskSpace, OutputExists, OutputNotWritable, Timeout, Cancelled, ToolMissing, ToolFailed, Unknown
}
```

```csharp
namespace Kvertis.Queue;

public enum JobState { Queued, Running, Paused, Completed, Failed, Cancelled }

public sealed class ConversionJob { Guid Id; InputInfo Input; ConversionSettings Settings; string OutputPath; JobState State; ConversionProgress Progress; ConversionResult? Result; ConversionErrorCode Error; }

public interface IJobQueue
{
    int MaxParallel { get; set; }
    IReadOnlyList<ConversionJob> Jobs { get; }
    void Enqueue(ConversionJob job);
    void Pause(Guid jobId);     void Resume(Guid jobId);     void Cancel(Guid jobId);
    void PauseAll();            void ResumeAll();            void CancelAll();
    event EventHandler<JobChangedEventArgs> JobChanged;      // raised on the queue's own thread; UI marshals
    OverallProgress Overall { get; }
}
```

## Architekturentscheidungen (ADR)

### ADR-001 · 2026-09-23 · Drei Projekte mit harter Grenze zur UI

**Entscheidung:** `Kvertis.Engine` und `Kvertis.Queue` sind reine `net8.0`-Bibliotheken ohne Windows-Abhängigkeit. Windows-spezifisches liegt in `Kvertis.Engine.Windows` und wird per DI eingehängt.
**Alternativen:** (a) Ein Projekt mit Ordnern; (b) Engine direkt auf `net8.0-windows`.
**Grund:** Die Engine soll testbar sein und später unter einer anderen UI (Avalonia für macOS) laufen. Ein Compiler-erzwungener Schnitt ist billiger als Disziplin. Nebeneffekt: Engine und Tests bauen in Linux-CI und in dieser Entwicklungsumgebung.
**Folgen:** Media Foundation und WIC brauchen Schnittstellen (`IMediaFoundationCapabilities`, `IHeicDecoder`) mit einer Windows-Implementierung und einem Null-Objekt für andere Plattformen.

### ADR-002 · 2026-09-23 · FFmpeg als separater Prozess, nie als Bibliothek

**Entscheidung:** Audio und Video laufen über `ffmpeg.exe`/`ffprobe.exe` als Kindprozess, angesteuert über FFMpegCore (MIT) hinter `IProcessRunner`.
**Alternativen:** (a) FFmpeg-DLLs per P/Invoke (FFmpeg.AutoGen); (b) Media Foundation direkt für alles.
**Grund:** Ein separater Prozess erfüllt die LGPL-Auflagen am klarsten (keine Verlinkung, Austausch durch den Nutzer möglich), isoliert Abstürze und erlaubt harte Timeouts und sauberes Beenden. Media Foundation allein deckt Container wie MKV/WebM und Codecs wie VP9/Opus/FLAC nicht ausreichend ab.
**Folgen:** Fortschritt wird aus der `stderr`-Ausgabe (`-progress pipe:2`) geparst. Startzeit pro Job etwa 100 ms, akzeptabel. `IProcessRunner` wird in Tests gemockt.

### ADR-003 · 2026-09-23 · Patentbelastete Codecs nur über Systemcodecs

**Entscheidung:** H.264, HEVC und AAC werden ausschließlich mit den Media-Foundation-Encodern von FFmpeg (`h264_mf`, `hevc_mf`, `aac_mf`) erzeugt. HEVC-Dekodierung nur über Hardware/System (D3D11VA). HEIC nur über Windows Imaging Component (siehe ADR-006).
**Alternativen:** (a) libx264/libx265 (GPL, ausgeschlossen); (b) FFmpeg-Software-Decoder für HEVC; (c) OpenH264 (BSD, aber Patentlizenz nur für den Binär-Download von Cisco).
**Grund:** Die Patentlizenz liegt dann bei Windows oder der vom Nutzer installierten Erweiterung. Kvertis liefert keinen patentbelasteten Encoder mit.
**Folgen:** Auf Systemen ohne den jeweiligen Codec liefert Kvertis `MissingSystemCodec` mit Hinweis. Die Qualität von `h264_mf` ist geringer als bei libx264; für die Zielgruppe ausreichend. `MediaFoundationCapabilities` prüft beim Start, welche Encoder verfügbar sind, und die Formatmatrix blendet fehlende aus.

### ADR-004 · 2026-09-23 · Fehler als Codes, Texte nur in der UI

**Entscheidung:** Die Engine wirft `ConversionException` mit `ConversionErrorCode` und strukturierten Details (Datei, Schritt, Tool-Ausgabe gekürzt). Texte, Übersetzungen und Lösungsvorschläge liegen in `.resw` der App.
**Alternativen:** Übersetzte Texte in der Engine.
**Grund:** Mehrsprachigkeit ab Tag 1 ohne Lokalisierung in der Engine; Tests prüfen Codes statt Strings; Wiederverwendung in anderer UI.
**Folgen:** Jeder neue Fehlercode braucht einen Eintrag in DE und EN. Der Reviewer prüft das.

### ADR-005 · 2026-09-23 · Warteschlange auf `Channel<T>` mit Semaphor, Pause per Job

**Entscheidung:** `JobScheduler` nimmt Jobs aus einem `Channel<ConversionJob>` und begrenzt die Parallelität mit `SemaphoreSlim(MaxParallel)`. Pause eines laufenden Jobs: Bilder und Dokumente pausieren zwischen Arbeitseinheiten (Datei, Seite); FFmpeg-Jobs werden in Phase 1 bei Pause **angehalten, indem der Prozess suspendiert wird** (`NtSuspendProcess`/`NtResumeProcess` in `Kvertis.Engine.Windows`), Fallback auf anderen Plattformen: Abbruch mit Neustart von vorn.
**Alternativen:** (a) Pause nur für wartende Jobs; (b) FFmpeg-Segmentierung mit Fortsetzung.
**Grund:** Nutzer erwarten, dass „Pause“ die CPU sofort freigibt. Prozess-Suspend ist einfach und zuverlässig; Segmentierung wäre komplex und formatabhängig.
**Folgen:** `MaxParallel` kann zur Laufzeit geändert werden; die Änderung gilt für neue Starts. Bei Video wird `MaxParallel` standardmäßig auf `max(1, Kerne / 2)` gesetzt, weil FFmpeg selbst multithreaded ist.

### ADR-006 · 2026-09-23 · HEIC über Windows Imaging Component, nicht über Magick.NET

**Entscheidung:** HEIC/HEIF werden mit `Windows.Graphics.Imaging.BitmapDecoder` (WIC) dekodiert und als unkomprimiertes Bild an Magick.NET übergeben. HEIC-Schreiben ist nicht Teil von Phase 1.
**Alternativen:** Magick.NET-Pakete mit eingebauter HEIC-Unterstützung (`libheif` + `libde265`, teils `x265`).
**Grund:** HEVC ist patentbelastet; `x265` ist GPL. Ob und welche dieser Bibliotheken in `Magick.Native` enthalten sind, muss `lizenz-waechter` je Paketvariante prüfen (offener Punkt, blockierend für HEIC). WIC nutzt die Systemerweiterungen, deren Lizenz beim Nutzer liegt.
**Folgen:** Fehlt die HEIF-Bilderweiterung, meldet Kvertis `MissingSystemCodec` mit Hinweis. Die Magick.NET-Paketvariante wird so gewählt, dass keine GPL-Komponenten enthalten sind; bis zur Prüfung gilt HEIC über Magick.NET als gesperrt.

### ADR-007 · 2026-09-23 · Ausgabe atomar, nie überschreiben

**Entscheidung:** Konverter schreiben in `<ziel>.kvertis-tmp` und benennen bei Erfolg um. Existiert die Zieldatei, wird nach Dateinamen-Muster nummeriert (`_1`, `_2`); Überschreiben nur nach ausdrücklicher Wahl des Nutzers pro Batch.
**Grund:** Kein Datenverlust bei Abbruch oder Absturz; Laien erwarten, dass nichts verloren geht.
**Folgen:** Aufräumen von `.kvertis-tmp` beim Start der App und bei Abbruch.

### ADR-008 · 2026-09-23 · Einstellungen, Verlauf und Tempo-Profil als JSON lokal

**Entscheidung:** `ApplicationData.Current.LocalFolder` mit `System.Text.Json`. Verlauf auf 200 Einträge begrenzt. Keine Datenbank.
**Alternativen:** SQLite (Microsoft.Data.Sqlite, MIT).
**Grund:** Datenmenge klein, keine Abfragen nötig, eine Abhängigkeit weniger.
**Folgen:** Wächst der Verlauf oder kommen Suchfunktionen, wird SQLite als ADR neu bewertet.

### ADR-009 · 2026-09-23 · MVVM mit CommunityToolkit.Mvvm und DI mit Microsoft.Extensions

**Entscheidung:** ViewModels über `CommunityToolkit.Mvvm` (MIT), Dienste über `Microsoft.Extensions.DependencyInjection` (MIT), Logging über `Microsoft.Extensions.Logging` in eine lokale Datei (opt-in, nie versendet).
**Grund:** Standard im WinUI-Umfeld, gut dokumentiert, keine Lizenzfragen.

### ADR-010 · 2026-09-23 · Dokument-Konvertierung ohne Rendering-Engine in Phase 1

**Entscheidung:** Phase 1 bei Dokumenten: PDF → Text, DOCX → Text/Markdown, XLSX → CSV, TXT/Markdown → PDF, Bilder → PDF. PDF → Bilder über `Windows.Data.Pdf` (System). DOCX/XLSX/PPTX → PDF (layouttreues Rendering) ist **nicht** in Phase 1.
**Alternativen:** Eigenes Layout-Rendering (Monate Arbeit), externe Office-Suite als Prozess (sehr groß, eigene Lizenzfragen), Bezahlbibliotheken.
**Grund:** Layouttreues Rendering von Office-Dokumenten ist ohne Renderer nicht seriös machbar. Lieber wenige Dokument-Funktionen, die zuverlässig sind.
**Folgen:** Die Formatmatrix in `05-formate.md` zeigt das ehrlich. Office → PDF steht als offener Punkt in der Roadmap.
