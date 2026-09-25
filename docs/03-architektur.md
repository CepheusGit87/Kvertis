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
│  │ Kvertis.Engine.Windows  (net10.0-windows)           │   │
│  │  Media Foundation, Windows Imaging Component (HEIC)│   │
│  └────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────┘
        │ Prozessgrenze                 │ NuGet
   ffmpeg.exe / ffprobe.exe         Magick.NET, PdfPig, OpenXml
   (LGPL-Build, separater Prozess)
```

Regeln, die aus dem Bild folgen:

- `Kvertis.Engine` und `Kvertis.Queue` referenzieren kein WinUI, kein Windows App SDK und keinen `DispatcherQueue`. Sie sind reine .NET-10-Klassenbibliotheken und bauen auf Linux.
- Windows-spezifischer Code (Media Foundation, Windows Imaging Component, Store-API) liegt in `Kvertis.Engine.Windows` bzw. `Kvertis.App` und wird über Schnittstellen eingehängt (Dependency Injection).
- Die UI kennt die Engine nur über `Kvertis.Queue` und die Schnittstellen in `Kvertis.Engine.Abstractions`-Namespaces. Sie erzeugt Jobs, beobachtet Fortschritt und zeigt Ergebnisse.
- Freemium-Limits (Batch-Größe, kein Video) werden in `Kvertis.App` beim Erzeugen der Jobs durchgesetzt, nicht in Engine oder Queue.

## Projektstruktur

```
Kvertis.sln
Directory.Build.props                 gemeinsame Einstellungen (LangVersion, Nullable, Warnings as Errors)
Directory.Packages.props              zentrale Paketversionen (Central Package Management)
src/
  Kvertis.Engine/                     net10.0
    Abstractions/                     IConverter, IFormatDetector, IInputValidator, IEstimator, IProcessRunner
    Formats/                          FormatId, FormatRegistry, MediaKind, Magic-Byte-Tabellen
    Validation/                       InputValidator, Grenzwerte
    Conversion/
      Images/                         ImageConverter (Magick.NET)
      Audio/                          AudioConverter (FFmpeg)
      Video/                          VideoConverter (FFmpeg + MF-Encoder)
      Documents/                      PdfConverter, OfficeConverter
      Models/                         ModelConverter (3D: STL, 3MF, OBJ, PLY, glTF/GLB; eigene Leser/Schreiber)
    Estimation/                       TimeEstimator, SizeEstimator, SpeedProfile
    Naming/                           OutputNamePattern
    Ffmpeg/                           FfmpegLocator, FfmpegCommandBuilder, FfprobeReader
    Errors/                           ConversionErrorCode, ConversionException
  Kvertis.Engine.Windows/             net10.0-windows10.0.19041.0
    Codecs/                           MediaFoundationCapabilities (welche MF-Encoder verfügbar sind)
    Imaging/                          WicHeicDecoder (HEIC über Windows Imaging Component)
  Kvertis.Queue/                      net10.0
    ConversionJob, JobState, JobQueue, JobScheduler, ProgressAggregator, JobHistory
  Kvertis.App/                        net10.0-windows10.0.19041.0, WinUI 3, MSIX
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

## Schritt 2 „Ziel“: Schnittstellen und Datenfluss (Skizze, 2026-09-25)

Grundlage: `design/ENTSCHEIDUNGEN.md` (Zeilen „Schritt 2“ und „Anpassen aus dem Verlauf“), `design/oberflaeche-mischentwurf.html` (Abschnitt „Ziel“), Entscheidungen ADR-019 und ADR-020 unten. Der Abschnitt legt fest, was in der Engine neu entsteht (Namensraum `Kvertis.Engine.Tuning`), was die App hält und in welcher Form Schritt 3 die Arbeit bekommt. Vollständige Signaturen sind Vorgabe für `engine-entwickler` und `ui-entwickler`; Feinheiten dürfen sie mit Eintrag hier anpassen.

### Was schon da ist und was fehlt

| Element der Ziel-Seite | Vorhanden | Fehlt |
|---|---|---|
| Dateiart links, Dateien als Karten mit Vorschau | `InputInfo` (`MediaKind`, Größe, Maße, Dauer), Miniaturen aus `MainViewModel` (Schritt 1) | Gruppierung je Art als eigenes ViewModel |
| Zielformat je Datei, Empfehlung markiert | `FormatRegistry.SuggestOutputs`, `IConverterResolver.CanConvert`, `FormatOption.IsSuggested` | Modus „Alle gleich / Jede einzeln“ |
| Globale Note 0–100 mit Wort und Größe | `ConversionSettings.Quality` (0–100), `IEstimator.Estimate` | Abbildung Note → alle Parameter einer Art (Auflösung, Bitrate, Bildrate), Wortstufe, **Größenschätzung, die auf die Note reagiert** (`Estimator` rechnet heute nur `SizeBytes × SizeRatio` und ignoriert `Quality` und `maxDimension`) |
| Größenleiste Rot→Grün mit Marken („E-Mail“) | `PresetCatalog` liefert `TargetSizeBytes` je Preset und Art (Messenger 1 MB / 16 MB, E-Mail 2 MB / 20 MB) | Marken als Datenliste, Farbzonen der Leiste (aus den Folgen), Umkehrung Größe → Note |
| Zonen mit eigenem Balken je Merkmal | `AdvancedKeys.MaxDimension`, `AudioBitrateKbps`, `FrameRate`, `SampleRateHz`; Konverter lesen sie schon | Merkmale (Schärfe, Details, Bewegung, Klang) als Typ, Ableitung Merkmal ↔ Parameter |
| „Zielgröße genau“ | `ConversionSettings.TargetSizeBytes`, Halbierungssuche in `ImageConverter`, Bitratenrechnung in `FfmpegArguments`/`TranscodePlan`, `ConversionErrorCode.TargetSizeUnreachable` | Prüfung vorab, ob die Zielgröße plausibel erreichbar ist (Untergrenze der Tabelle) |
| „Weiteres“ (Metadaten, Dateiname) | `MetadataPolicy`, `OutputNamePattern`, `InputWarning.MetadataNotStrippable` | nichts |
| „Was sich ändert“ (Textliste) | `InputWarning` (Transparenz, Animation, Tonspuren) | Folgen der gewählten Einstellungen als **Codes** (Auflösung sinkt, Artefakte, Mono, Farben fallen weg) |
| „damals“-Marken aus dem Verlauf | `HistoryEntry.Settings` (vollständige `ConversionSettings`), `MainViewModel.AddPathsWithSettingsAsync` | Übergabe der alten Einstellungen an die Ziel-Seite, Rückrechnung alte Einstellungen → Note und Merkmale |
| Übergabe an Schritt 3 | `ConversionJob(InputInfo, ConversionSettings, outputDirectory, namePattern, batchIndex)`, `IJobQueue.EnqueueRange` | ein Sitzungsobjekt der App, das Dateien (Schritt 1), Plan (Schritt 2) und Speicherort (Schritt 3) zusammenhält |

`ConversionSettings`, `PresetCatalog`, `IConverter`, `IEstimator` und `ConversionJob` bleiben unverändert. Die Note ist **kein** neues Feld in `ConversionSettings`: Sie ist eine Sicht der App auf die Einstellungen, die die Engine hin- und zurückrechnet (ADR-019). So bleiben Verlauf, Tempo-Profil und alle Konverter kompatibel.

### Namensraum `Kvertis.Engine.Tuning` (neu, reine Funktionen)

Keine Zustände, keine Abhängigkeit auf UI, nur `Kvertis.Engine.Abstractions`, `Formats`, `Conversion.PresetCatalog` und `IEstimator`. Alles deterministisch und ohne Dateizugriff testbar.

```csharp
namespace Kvertis.Engine.Tuning;

/// <summary>One quality grade 0..100 as shown in the ring. 80 is the default and equals ConversionSettings.Quality = 80.</summary>
public readonly record struct QualityGrade(int Value)
{
    public static readonly QualityGrade Default = new(80);
    public int Clamped => Math.Clamp(Value, 0, 100);
    /// <summary>Word band for the ring label; the app translates the band, never the number.</summary>
    public GradeBand Band => Clamped switch { >= 85 => GradeBand.Excellent, >= 65 => GradeBand.Good, >= 45 => GradeBand.Usable, _ => GradeBand.HeavilyReduced };
}

public enum GradeBand { HeavilyReduced = 0, Usable, Good, Excellent }

/// <summary>A perceivable property of the output that one zone bar controls. Which ones apply depends on MediaKind and output format.</summary>
public enum TuningAspect
{
    /// <summary>Images and video: pixel dimensions (maxDimension). Zone "Schärfe".</summary>
    Sharpness = 0,
    /// <summary>Images: encoder quality. Video: bitrate budget per pixel. Zone "Details".</summary>
    Detail,
    /// <summary>Video only: frame rate (frameRate). Zone "Bewegung".</summary>
    Motion,
    /// <summary>Audio (also the audio track of video): bitrate (audioBitrateKbps). Zone "Klang".</summary>
    Sound,
}

/// <summary>Level of one aspect for the current settings. Adjustable = false when the output format fixes it (lossless, audio-only, GIF palette).</summary>
public sealed record AspectLevel(TuningAspect Aspect, int Value, bool Adjustable, AspectDetail Detail);

/// <summary>Numbers the UI puts next to the bar ("1920 statt 4032 px", "128 kbit/s"). Texts come from resources.</summary>
public sealed record AspectDetail(int? Pixels = null, int? SourcePixels = null, int? Kbps = null, int? FrameRate = null, int? Quality = null, bool Lossless = false);

/// <summary>Maps a grade to converter settings and back, per media kind. Pure and deterministic (ADR-019).</summary>
public static class GradeMapper
{
    /// <summary>False for documents and 3D models: no grade, no size bar, only effects and "Weiteres".</summary>
    public static bool SupportsGrade(MediaKind kind, FormatId output, FormatRegistry registry);

    /// <summary>
    /// Returns <paramref name="baseline"/> with Quality, maxDimension, audioBitrateKbps and frameRate set for the grade.
    /// Keeps Output, Metadata, TargetSizeBytes and every advanced key it does not own. Lossless outputs get Quality 100 and no dimension change.
    /// </summary>
    public static ConversionSettings Apply(QualityGrade grade, ConversionSettings baseline, InputInfo input, FormatRegistry registry);

    /// <summary>Levels of all aspects that exist for this kind and output, in display order.</summary>
    public static IReadOnlyList<AspectLevel> Aspects(ConversionSettings settings, InputInfo input, FormatRegistry registry);

    /// <summary>Changes one aspect (zone bar) and leaves the other parameters as they are. Non-adjustable aspects return settings unchanged.</summary>
    public static ConversionSettings WithAspect(ConversionSettings settings, TuningAspect aspect, int value, InputInfo input, FormatRegistry registry);

    /// <summary>
    /// The grade that best describes arbitrary settings (history entries, zone edits): 0.6 × mean + 0.4 × minimum of the adjustable aspects,
    /// like the design prototype. Round trip: GradeOf(Apply(g, …)) == g for every g in 0..100 (test).
    /// </summary>
    public static QualityGrade GradeOf(ConversionSettings settings, InputInfo input, FormatRegistry registry);
}
```

Abbildung Note → Parameter je Art (Vorgabe für `engine-entwickler`, Werte dürfen nach Sichtprüfung angepasst werden, die Form nicht):

| Art | Schärfe (`maxDimension`) | Details (`Quality`) | Bewegung (`frameRate`) | Klang (`audioBitrateKbps`) |
|---|---|---|---|---|
| Bilder (verlustbehaftet: JPG, WebP) | Note ≥ 70: Original; 55–69: 2560; 40–54: 1920; 25–39: 1280; < 25: 800 (nie über die Quelle hinaus) | `Quality = 30 + 0,7 × Note` (Note 0 → 30, Note 100 → 100) | – | – |
| Bilder (verlustfrei: PNG, TIFF, BMP) | wie oben | fest 100, nicht einstellbar | – | – |
| Audio (verlustbehaftet) | – | – | – | Stufen 64, 96, 128, 160, 192, 256, 320 kbit/s; Note < 20 → 64, < 40 → 96, < 55 → 128, < 70 → 160, < 85 → 192, < 95 → 256, sonst 320; AAC auf die Media-Foundation-Stufen (`TranscodePlan.AacBitrateSteps`) gerundet |
| Audio (verlustfrei: FLAC, WAV) | – | – | – | fest, nicht einstellbar |
| Video (MP4, WebM, MKV) | Note ≥ 80: Original; 60–79: 1080; 40–59: 720; < 40: 480 | `Quality = 30 + 0,7 × Note` (Bit je Pixel und Bild in `FfmpegArguments.QualityVideoKbps`, VP9-CRF in `Vp9Crf`) | Note ≥ 50: Original; 30–49: 30; < 30: 24 | wie Audio, Deckel 192 |
| Video → „Nur Ton“ | – | – | – | wie Audio; Schärfe, Details, Bewegung nicht einstellbar (`Adjustable = false`, Wert 0) |
| Dokumente, 3D | `SupportsGrade = false` | | | |

Die Rückrichtung `GradeOf` bildet jeden Parameter auf 0–100 ab (Auflösung relativ zur Quelle, `Quality` linear, Bitrate über die Stufenliste, Bildrate Original = 100, 30 = 90, 24 = 70) und mischt wie oben. Das Ergebnis ist die Note für „damals“ und nach einer Zonen-Änderung.

Folgen („Was sich ändert“) als Codes, keine Texte (ADR-004):

```csharp
public enum EffectSeverity { Fine = 0, Notice, Warning }

public enum EffectCode
{
    ResolutionReduced,        // Pixels, SourcePixels
    FullResolutionKept,
    VisibleArtifacts,         // Quality (< 55)
    SlightArtifacts,          // Quality (55..74)
    NearOriginalQuality,      // Quality (>= 75)
    LosslessOutput,
    PaletteLimited,           // GIF
    TransparencyLost,         // from InputWarning
    AnimationDropped,         // from InputWarning
    BitrateSpeechOnly,        // Kbps (<= 96)
    BitrateSlightlyDull,      // Kbps (<= 128)
    BitrateLikeOriginal,
    FrameRateReduced,         // FrameRate
    MotionBlocky,             // Quality (< 55) on video
    VideoTrackDropped,        // audio-only output
    MetadataStripped,
    MetadataKept,
    MetadataNotStrippable,    // from InputWarning (Media Foundation path)
    FormattingLost,           // documents → TXT
    StructureKept,            // documents → Markdown
    LayoutSimplified,         // documents → HTML
    ColorsAndMaterialsDropped,// 3D
    UnitUnknown,              // 3D → STL
    TargetSizeMayBeUnreachable, // below the smallest size of the grade table
}

public sealed record ConversionEffect(EffectCode Code, EffectSeverity Severity, AspectDetail Detail);

public static class EffectAnalyzer
{
    /// <summary>Effects of these settings on this input, worst first. The app maps Code to a .resw key and fills numbers from Detail.</summary>
    public static IReadOnlyList<ConversionEffect> Analyze(InputInfo input, ConversionSettings settings, FormatRegistry registry);

    public static EffectSeverity Worst(IReadOnlyList<ConversionEffect> effects);
}
```

Größe je Note und Größenleiste:

```csharp
/// <summary>Estimated output size for each grade step of one file, precomputed so the slider never calls the estimator while dragging (ADR-019).</summary>
public sealed record GradeSizeTable(IReadOnlyList<GradeSizePoint> Points)
{
    /// <summary>21 points: grade 0, 5, 10, … 100. Bytes are made monotone (a higher grade never estimates smaller).</summary>
    public const int StepCount = 21;
    public long BytesAt(QualityGrade grade);                 // linear interpolation between points
    public QualityGrade GradeForBytes(long bytes);           // inverse; clamps to 0/100 outside the range
    public long MinBytes => Points[0].Bytes;
    public long MaxBytes => Points[^1].Bytes;
}

public sealed record GradeSizePoint(QualityGrade Grade, long Bytes, EffectSeverity Worst);

public static class GradeSizeTableBuilder
{
    /// <summary>Runs GradeMapper.Apply + estimator.Estimate for every step. Pure apart from reading the estimator's speed profile; call it off the UI thread for large batches.</summary>
    public static GradeSizeTable Build(InputInfo input, ConversionSettings baseline, IEstimator estimator, FormatRegistry registry);
}

/// <summary>Marks on the size bar: presets that prescribe a target size for this kind (Messenger, E-Mail). Labels come from resources via ConversionPreset.</summary>
public static class SizeMarks
{
    public static IReadOnlyList<(ConversionPreset Preset, long Bytes)> For(MediaKind kind);   // from PresetCatalog, ascending
}
```

Damit die Tabelle etwas aussagt, muss `Estimator.Estimate` auf die Einstellungen reagieren. Neu in `Kvertis.Engine.Estimation`:

```csharp
/// <summary>Relative size of the output for these settings compared with the reference (grade 80, no dimension change). Pure, per kind.</summary>
public static class SizeModel
{
    /// <summary>Factor > 0; 1.0 at the reference. Images: (pixels after maxDimension / source pixels) × quality curve (0.06 + 0.45 × ((q − 30) / 70)^2.2, normalised). Audio: kbps / reference kbps. Video: bits per pixel and frame × pixels × frame rate, plus audio. Lossless outputs: only the pixel factor.</summary>
    public static double Factor(InputInfo input, ConversionSettings settings, FormatRegistry registry);
}
```

`Estimator.Estimate` multipliziert `SizeRatio × SizeModel.Factor(...)`; `Record` teilt das gemessene Verhältnis vorher durch denselben Faktor, damit das Tempo-Profil weiter das Verhältnis bei Referenzeinstellungen lernt. Der Profil-Schlüssel (`Kind:Format>Output`) bleibt gleich; bestehende `speed-profile.json` bleiben gültig.

### Datenmodell der Ziel-Seite (App)

Alles unter `Kvertis.App/ViewModels/Target/`. Kein Engine-Typ sickert in XAML; Views binden an ViewModels, ViewModels rufen `Kvertis.Engine.Tuning`.

```
TargetPageViewModel
  Kinds : ObservableCollection<KindGroupViewModel>      eine je vorhandener MediaKind, Reihenfolge Bilder, Audio, Video, 3D, Dokumente (Schritt-2-Reihenfolge)
  SelectedKind : KindGroupViewModel?
  Previous : PreviousSettingsViewModel?                  „Aus dem Verlauf · damals“, null ohne Verlauf
  CanContinue : bool                                     mindestens eine Datei mit Ziel, keine gesperrte Datei allein
  ContinueCommand                                        baut TargetPlan, schreibt ihn in IWorkflowSession, GoTo(Convert)
  BackCommand                                            GoTo(Drop)

KindGroupViewModel
  Kind : MediaKind, ColorKey : string ("KvImage" …)
  Files : ObservableCollection<TargetFileViewModel>
  Mode : TargetMode { AllSame, Individual }              nur sichtbar bei > 1 Datei
  SharedFormats : IReadOnlyList<FormatOption>            Schnittmenge dessen, was IConverterResolver.CanConvert für alle Dateien der Art zulässt; IsSuggested aus FormatRegistry.SuggestOutputs
  SharedFormat : FormatOption?                           gilt in AllSame für alle, in Individual als Vorgabe für Dateien ohne eigene Wahl
  Tuning : TuningPanelViewModel                          Note, Leiste, Zonen, Zielgröße genau, Weiteres, Was sich ändert (je Art, nicht je Datei)
  IsLocked : bool, LockReason : string?                  Freemium (Video ohne Pro), siehe ADR-020
  Paths : IReadOnlyList<PathRowViewModel>                Mitte, Etappe B als Liste „Format → Ziel“: Quelle-Format, Ziel-Format, geschätzte Größe je Datei

TargetFileViewModel
  Input : InputInfo, FileName, SizeText, DetailsText, Thumbnail (aus Schritt 1 übernommen)
  Formats : IReadOnlyList<FormatOption>                  was CanConvert für diese Datei zulässt
  OwnFormat : FormatOption?                              nur in Individual; null = SharedFormat
  EffectiveOutput : FormatId                             OwnFormat ?? SharedFormat
  EstimatedBytes : long, EstimateText                    aus der GradeSizeTable der Datei (siehe Ablauf)
  Warnings : IReadOnlyList<InputWarning>

TuningPanelViewModel
  SupportsGrade : bool                                   GradeMapper.SupportsGrade; false → nur Effects und More sichtbar
  Grade : int (0..100), BandText, LargestBytesText       Ring; Größe = Maximum der Dateien der Art bei dieser Note
  SizeBar : SizeBarViewModel                             Minimum/Maximum (kleinste Tabelle / größte Quelle), Position, Marken (Label aus Preset_* Ressourcen), Zonenfarben aus GradeSizePoint.Worst
  Zones : IReadOnlyList<ZoneViewModel>                   je AspectLevel: Aspect, Value, Adjustable, DetailText, IsExpanded
  ExactTargetBytes : long?, ExactTargetUnreachable : bool „Zielgröße genau“; gesetzt → TargetSizeBytes, Ring zeigt „≈“ und GradeOf der Suchuntergrenze
  StripMetadata : bool, NamePattern : string, NamePreview „Weiteres“
  Effects : IReadOnlyList<EffectViewModel>               Severity, Text aus Effect_<Code>_Text mit Detail-Zahlen
  PreviousGrade : int?, PreviousBytes : long?, PreviousAspects  „damals“-Marken
  ApplyPreviousCommand                                    „Alte Werte übernehmen“
```

`TargetMode.Individual` ändert nur das Format je Datei. Note, Zonen, Zielgröße und Weiteres gelten immer je Art (Entwurf: „globale Note“). Wer eine Datei wirklich anders behandeln will, nutzt später „Ändern“ in Schritt 3 (offen, nicht Teil dieser Skizze).

### Ablauf beim Ziehen (Note und Leiste)

1. Beim Betreten der Seite und bei jeder Änderung von Format, Zonen-Konfiguration oder Metadaten (alles außer der Note) baut das ViewModel je Datei eine `GradeSizeTable` (`GradeSizeTableBuilder.Build`) in `Task.Run`, gebündelt für alle Dateien der Art, mit `CancellationToken` bei erneuter Änderung. 21 Schätzungen je Datei sind Arithmetik; 500 Dateien dauern deutlich unter 100 ms.
2. Beim Ziehen des Rings oder der Leiste rechnet die App nur noch `table.BytesAt(grade)` bzw. `table.GradeForBytes(bytes)` synchron auf dem UI-Thread. Kein Engine-Aufruf, kein Prozessstart.
3. Der Regler ist entprellt: `Grade` ändert sich mit jedem Bild, die abgeleiteten Texte (Note-Wort, Größe, Zonenwerte) sofort aus der Tabelle; `Effects` und die Zonenfarben werden alle 50 ms nachgezogen (`DispatcherQueueTimer`), Endwert beim Loslassen. Verantwortung: **Engine liefert reine Funktionen und die Tabelle, die App entprellt und cached.**
4. Ziehen der Leiste: Position → Bytes (logarithmische Skala wie im Entwurf) → `GradeForBytes` über die Tabelle der **größten** Datei → dieselbe Note für die ganze Art. Die Leiste ist eine zweite Ansicht der Note, sie setzt **keine** `TargetSizeBytes`.
5. Zone ändern: `GradeMapper.WithAspect` → neue Einstellungen → `GradeOf` gibt die neue Note, die Ring und Leiste anzeigen; die Tabellen werden neu gebaut (Schritt 1), weil die Kopplung der Parameter jetzt eine andere ist. Ein späteres Ziehen am Ring setzt alle Zonen wieder auf die gekoppelte Vorgabe (`Apply`).
6. „Zielgröße genau“: setzt `TargetSizeBytes`; die Konverter suchen selbst (Halbierung bzw. Bitrate aus Dauer). Vorab prüft die App `bytes < table.MinBytes` je Datei und zeigt `TargetSizeMayBeUnreachable` als Hinweis; die Engine meldet später endgültig `TargetSizeUnreachable`. Ring und Leiste zeigen in diesem Zustand die aus `GradeForBytes` geschätzte Note als „≈“.

### Übergabe an Schritt 3 (ADR-020)

```csharp
namespace Kvertis.App.Services;

/// <summary>State that travels through the three steps. One instance per window, reset by "Neue Runde".</summary>
public interface IWorkflowSession
{
    IReadOnlyList<StagedFile> Staged { get; }                 // Step 1: detected inputs with thumbnails and warnings
    TargetPlan? Plan { get; set; }                            // Step 2 result
    OutputLocation Location { get; set; }                     // Step 3, default from settings
    HistoryEntry? Previous { get; set; }                      // set by "Anpassen aus dem Verlauf", cleared on "Neue Runde"
    event EventHandler? Changed;
    void Reset();
}

public sealed record StagedFile(InputInfo Input, string? ThumbnailPath, ConversionErrorCode RejectedWith = ConversionErrorCode.None);

/// <summary>Everything Step 3 needs to build ConversionJobs: one entry per file that will be converted.</summary>
public sealed record TargetPlan(IReadOnlyList<PlannedConversion> Items, IReadOnlyList<SkippedFile> Skipped);

public sealed record PlannedConversion(InputInfo Input, ConversionSettings Settings, string NamePattern);

/// <summary>Files left out of the plan and why (Freemium lock, no converter for the chosen output). Reason keys as in FreemiumPolicy.</summary>
public sealed record SkippedFile(InputInfo Input, string Reason);
```

Schritt 3 macht daraus `new ConversionJob(item.Input, item.Settings, OutputDirectoryResolver.Resolve(item.Input.Path, session.Location), item.NamePattern, batchIndex)` und ruft `IJobQueue.EnqueueRange`. Die Queue bekommt also weiterhin **nur `ConversionJob`**; `Kvertis.Queue` ändert sich nicht. `TargetPlan` bleibt in der App, weil er UI-Zustand ist (Modus, Auswahl), keine Warteschlangenlogik.

„Anpassen aus dem Verlauf“: `HistoryViewModel.AgainAsync` setzt `session.Previous = entry`, öffnet den Dateidialog und springt nach Schritt 1 (Dateien) und weiter nach Schritt 2. Dort liest `TargetPageViewModel` für die Art des Verlaufseintrags (`entry.Settings.Output` → `MediaKind` über `FormatRegistry`) die „damals“-Werte: Note = `GradeMapper.GradeOf(entry.Settings, input, registry)` (mit der ersten passenden Datei als `input`), Größe = `estimator.Estimate(input, entry.Settings).OutputBytes`, Zonen = `GradeMapper.Aspects(entry.Settings, …)`, Format = `entry.Settings.Output`. „Alte Werte übernehmen“ setzt `SharedFormat`, `Grade` und die Zonen aus `entry.Settings` (nicht nur die Note, damit abweichende Zonen erhalten bleiben) sowie `Metadata` und `TargetSizeBytes`. Passt das alte Format nicht zur neuen Datei (`CanConvert = false`), bleibt die Empfehlung und der Hinweis zeigt „damals: WebP, hier nicht möglich“ (Ressource, kein Engine-Text).

### Freemium in Schritt 2 (App, nicht Engine)

- `FreemiumPolicy` bleibt die einzige Regel-Quelle und wird um zwei reine Abfragen ergänzt: `bool IsKindLocked(MediaKind kind)` (Video ohne Pro) und `int? BatchLimit` (5 ohne Pro). Beide fragen `ILicenseService.IsPro`.
- `KindGroupViewModel.IsLocked` zeigt die Pro-Karte in der Gruppe (kein Modal, wie `06-design.md` Abschnitt 7). Gesperrte Dateien kommen in `TargetPlan.Skipped` mit `FreemiumPolicy.ReasonVideo`; „Weiter“ bleibt aktiv, sobald mindestens eine Datei geplant ist.
- Batch-Grenze: Über dem Limit zeigt die Seite die Karte „Batch zu groß“; `ContinueCommand` nimmt die ersten fünf in den Plan, der Rest landet in `Skipped` mit `ReasonBatchSize`. Die Admission der Queue prüft dieselbe Regel ein zweites Mal (Netz und doppelter Boden); Engine und Queue kennen keine Lizenz.

### Etappen für `ui-entwickler` und `engine-entwickler`

| Etappe | Inhalt | Wer | Abnahme |
|---|---|---|---|
| A | `Kvertis.Engine.Tuning` (`QualityGrade`, `GradeMapper`, `EffectAnalyzer`, `GradeSizeTable`, `SizeMarks`) und `Estimation.SizeModel`; `Estimator` reagiert auf Einstellungen. Tests: Monotonie von `GradeOf` und Toleranz zur Note (siehe Nachtrag unten), Monotonie der Tabelle, Determinismus, keine Engine-Ausnahme für alle Arten × Formate der Registry | engine-entwickler, tester | `dotnet test tests/Kvertis.Engine.Tests --filter Category!=Integration` |
| B | `IWorkflowSession`, `TargetPlan`, ViewModels unter `ViewModels/Target/` mit Tests ohne WinUI (reine Logik: Modus, Schnittmenge der Formate, Tabellen-Cache, Entprellung über `TimeProvider`) | ui-entwickler, tester | ViewModel-Tests |
| C | `TargetPage.xaml` in reinem XAML: links Artenliste (Text und Farbpunkt, kein Ring, keine Bewegung), Dateikarten, Modus-Umschalter; Mitte schlichte Liste „Format → Ziel“; rechts Ring (`ProgressRing` oder `Ellipse` mit Text), Leiste als `Slider` mit Markenbeschriftung darunter, Zonen als `Expander` mit `Slider`, „Zielgröße genau“ als `NumberBox`, „Weiteres“, „Was sich ändert“ als Liste mit Symbol und Text. Alles mit `AutomationProperties.Name` und Wert; Farben nur aus `KvertisColors.xaml` | ui-entwickler | Sichtprüfung unter Windows, Hoher Kontrast, Tastatur |
| D | Verdrahtung: Schritt 1 füllt `IWorkflowSession.Staged`, „Weiter: Ziel“ nur mit Dateien; Schritt 2 „Weiter: Umwandeln“ schreibt `Plan`; Schritt 3 baut die Jobs; Verlauf setzt `Previous`. `MainPage` gibt Start, Format-Chip, `MorePanel` und `FormatPickerFlyout` ab (bleiben bis dahin bestehen, werden danach entfernt) | ui-entwickler, reviewer | Durchlauf Bild, Audio, Dokument; Verlauf → damals |

Die Zeichenschicht (Bahn und Loch in der Mitte, Universum-Symbole links, Übergänge) ist ausdrücklich **nicht** Teil dieser Etappen (ADR-018, später mit Win2D).

### Nachtrag 2026-09-25 · Abweichungen der Umsetzung (engine-entwickler) von der Skizze oben

Umgesetzt in `src/Kvertis.Engine/Tuning/` und `Estimation/SizeModel.cs`; der Code ist verbindlich, die Skizze oben bleibt als Entwurf stehen.

1. **Rundreise nicht exakt:** `GradeOf(Apply(g))` kann bei Stufenleitern nicht auf die Note zurückführen. Getestet werden Monotonie und eine Toleranz von 20 Punkten (AAC hat nur vier Bitratenstufen), 12 Punkten bei feinen Leitern. Die Rückabbildung nimmt die Stufenmitte; eine Stufe ohne Reduktion gegenüber der Quelle zählt als 100.
2. **Feinere Leitern:** Schärfe sieben Stufen 640 … 3840 px (Bilder, lange Kante) bzw. 360 … 1440 px (Video, Höhe); Bildrate sechs Stufen 15 / 20 / 24 / 25 / 30 / Original; Details linear `30 + 0,7 × Note`; Audio-Stufen wie in der Tabelle, Deckel 192 kbit/s für Videoton, AAC auf `TranscodePlan.AacBitrateSteps` gerundet.
3. **Referenz von `SizeModel.Factor`:** `ConversionSettings` im Auslieferungszustand (Quality 80, keine Advanced-Schlüssel), nicht Note 80, damit der Faktor dort exakt 1,0 ist und bestehende `speed-profile.json` unverändert gültig bleiben. `SizeMarks.For` liefert `SizeMark(Preset, Bytes)`-Records statt Tupel.
4. **Verlustfreie Bilder** behalten die Schärfe-Stufe als einstellbares Merkmal (nur Details ist fest). `SupportsGrade` ist genau dann falsch, wenn kein einstellbares Merkmal übrig bleibt: Dokumente, 3D-Modelle, verlustfreies Audio (FLAC, WAV, AIFF).

Für die App ändert sich: „damals“-Noten und Noten nach Zonen-Änderungen sind auf ±12 bzw. ±20 Punkte genau, der Ring zeigt sie mit „≈“; die Zone „Schärfe“ erscheint auch bei PNG/TIFF; FLAC/WAV zeigen keinen Ring und keine Leiste, nur „Was sich ändert“ und „Weiteres“.

## Schritt 3 „Umwandeln“: Schnittstellen und Datenfluss (Skizze, 2026-09-25)

Grundlage: `design/ENTSCHEIDUNGEN.md` (Zeilen „Schritt 3“, „Ziel in Schritt 3“, „Speicherort“, „Übergänge und Abschluss“), `design/oberflaeche-mischentwurf.html` (Abschnitt „Schritt 3“), ADR-020 und ADR-021. Kurzfassung zum Abarbeiten: `docs/entwuerfe/schritt-3-umwandeln.md`. Reines XAML; die Zeichenschicht (ADR-018) kommt später und bekommt in der Seite nur eine leere Fläche.

### Was schon da ist und was fehlt

| Element der Umwandeln-Seite | Vorhanden | Fehlt |
|---|---|---|
| Jobs bauen und einreihen | `ConversionJob(InputInfo, ConversionSettings, outputDirectory, namePattern, batchIndex)`, `OutputDirectoryResolver.Resolve`, `IJobQueue.EnqueueRange`, Admission über `FreemiumPolicy` | ein Ort ohne WinUI, der aus `TargetPlan` + Speicherort die Jobs baut und die Ids **dieser Runde** kennt (heute verstreut in `MainViewModel.StartAsync`) |
| Zielpfad je Datei vor dem Start | `OutputNamePattern.Render` + `EnsureUnique`, `FormatRegistry.ExtensionFor`; die Queue rechnet den endgültigen Pfad erst beim Start (`JobRunner`, `OutputPathReserver`) und meldet ihn mit `JobChangeKind.Details` | dieselbe Rechnung als Vorschau für die Liste, mit Nummerierung innerhalb der Runde |
| Speicherort für alle | `OutputLocation` (SameFolder, SubFolder, Custom), `IWorkflowSession.Location`, Einstellungen (`OutputLocationKind`, Ordner-Token über `IFilePickerService`) | Speicherort „noch nicht gewählt“ (Vorgabe aus Einstellungen), Ordner aus dem Explorer ziehen |
| Eigenes Ziel je Datei („Ändern“, „Tasche“) | nichts | Ausnahmen je Datei als Sitzungszustand (ADR-021) |
| Fortschritt, Restzeit, Pause, Abbruch | `IJobQueue.JobChanged`, `OverallProgress.Compute`, `Pause/Resume/Cancel(id)`, `IUiDispatcher` | Aggregat nur über die Jobs der Runde; Marshalling an einer Stelle statt in jedem ViewModel |
| Fehler mit Lösung | `ConversionErrorCode`, `ErrorMessageMapper` (`Error_<Code>_Title/Body`) | nichts |
| Verlauf | `JobQueueOptions.History` → `JobHistory.RecordAsync` (Completed, Failed); `HistoryEntry.FromJob` hält `Settings`, `OutputPath`, Bytes | nichts; die App schreibt keinen Verlauf |
| „Öffnen“, „Im Ordner zeigen“ | `MainViewModel.OpenFolderAsync` (WinRT `Launcher`) | kleiner Dienst mit zwei Nutzern (Zeile, Bericht) |
| Bericht am Ende | `OverallProgress` (Completed, Failed, Cancelled, BytesIn/Out), `ConversionJob.StartedAt/FinishedAt` | Zusammenfassung je Runde |

`Kvertis.Queue` und `Kvertis.Engine` ändern sich für Schritt 3 **nicht**. Alles Neue liegt in `Kvertis.App/Services` (ohne WinUI, testbar) und `Kvertis.App/ViewModels/Convert`.

### Sitzung: Ergänzungen an `IWorkflowSession`

```csharp
public interface IWorkflowSession
{
    // ... wie ADR-020 ...

    /// <summary>Step 3: shared location. Null until the user chose one in this round; the convert page then takes the settings default.</summary>
    OutputLocation? Location { get; set; }

    /// <summary>Step 3: per-file exceptions ("Ändern"), keyed by InputInfo.Path. Empty by default; Reset clears it.</summary>
    IReadOnlyDictionary<string, OutputLocation> OwnLocations { get; }

    /// <summary>Sets or (with null) removes the exception for one file. Raises Changed.</summary>
    void SetOwnLocation(string inputPath, OutputLocation? location);
}
```

### Zielpfad-Vorschau (rein, `Kvertis.App/Services/TargetPathPlanner.cs`)

```csharp
namespace Kvertis.App.Services;

/// <summary>Where one planned file will land, computed the same way JobRunner does it at start (ADR-007: numbered, never overwritten).</summary>
public sealed record TargetPathPreview(
    PlannedConversion Item,
    OutputLocation Location,          // effective: own location or the shared one
    bool IsOwnLocation,
    string? Directory,                // null when NeedsFolder
    string? FileName,
    string? FullPath,
    bool NeedsFolder);                // temporary input (clipboard) with SameFolder/SubFolder: the user must pick a folder first

public static class TargetPathPlanner
{
    public static OutputLocation EffectiveLocation(PlannedConversion item, OutputLocation shared, IReadOnlyDictionary<string, OutputLocation> own);

    /// <summary>True when the input lives under <paramref name="temporaryRoot"/> (clipboard images) and the location is relative to it.</summary>
    public static bool NeedsFolder(PlannedConversion item, OutputLocation location, string temporaryRoot);

    /// <summary>
    /// One preview per plan item, in plan order. Batch index 1..n only when the plan has more than one item.
    /// Numbering ("_1", "_2") considers <paramref name="exists"/> (disk) and the other previews of this call, like OutputPathReserver does at start.
    /// </summary>
    public static IReadOnlyList<TargetPathPreview> PreviewAll(
        TargetPlan plan, OutputLocation shared, IReadOnlyDictionary<string, OutputLocation> own,
        FormatRegistry registry, DateTimeOffset now, Func<string, bool> exists, string temporaryRoot);
}
```

### Koordinator (`Kvertis.App/Services/ConversionCoordinator.cs`, ohne WinUI)

```csharp
namespace Kvertis.App.Services;

public enum RoundState { Ready = 0, Running, Paused, Finished }

/// <summary>Summary of one round for the closing report. Elapsed = first StartedAt to last FinishedAt.</summary>
public sealed record RoundReport(int Total, int Completed, int Failed, int Cancelled, long BytesIn, long BytesOut, TimeSpan Elapsed,
                                 IReadOnlyList<(ConversionJob Job, ConversionErrorCode Error)> Failures);

public sealed class RoundChangedEventArgs(ConversionJob? job, JobChangeKind? kind) : EventArgs
{
    public ConversionJob? Job { get; } = job;          // null for state changes of the round itself
    public JobChangeKind? Kind { get; } = kind;
}

/// <summary>
/// Owns the jobs of the current round (ADR-021): builds them from the plan, enqueues them, filters queue events to
/// its own ids and re-raises them on the UI thread. The only place in the app that calls IJobQueue for conversions.
/// </summary>
public interface IConversionCoordinator
{
    RoundState State { get; }
    IReadOnlyList<ConversionJob> Jobs { get; }         // plan order; replaced jobs (RelocateWaiting) keep their slot
    OverallProgress Overall { get; }                   // OverallProgress.Compute(Jobs)
    RoundReport? Report { get; }                       // set when State == Finished

    /// <summary>Raised on the UI thread via IUiDispatcher.</summary>
    event EventHandler<RoundChangedEventArgs>? Changed;

    /// <summary>Builds one ConversionJob per preview and calls EnqueueRange. Throws JobAdmissionException (nothing enqueued) and ArgumentException (bad folder). Requires State == Ready and no preview with NeedsFolder.</summary>
    void Start(IReadOnlyList<TargetPathPreview> previews);

    /// <summary>After start: replaces jobs that are still Queued (or paused before start) whose directory changed. Remove + new job with the same input, settings, name pattern and batch index. Running and finished jobs are untouched. Returns the number of replaced jobs.</summary>
    int RelocateWaiting(IReadOnlyList<TargetPathPreview> previews);

    void PauseAll();      // Pause(id) for every own job that is Queued or Running
    void ResumeAll();
    void CancelAll();

    /// <summary>Forgets the round: Remove(id) for own finished jobs, clears ids, State = Ready.</summary>
    void Reset();
}
```

Regeln:

- Abhängigkeiten nur `IJobQueue`, `IUiDispatcher`, `TimeProvider`. Registrierung als Singleton. Tests mit Fake-`IJobQueue` und synchronem Fake-`IUiDispatcher` in `tests/Kvertis.App.Tests` (per `<Compile Include>` wie `TargetPlanner`).
- `State` wird aus den eigenen Jobs abgeleitet: `Running`, solange einer `Running` oder `Queued` ist; `Paused`, wenn alle nicht fertigen Jobs `Paused` sind; `Finished`, wenn alle fertig sind; `Ready` ohne Jobs. Beim Übergang nach `Finished` wird `Report` einmal berechnet.
- `PauseAll`/`CancelAll` gehen je Id, nie über `IJobQueue.PauseAll`, damit der Koordinator nur seine Runde anfasst.
- Der Koordinator schreibt **keinen** Verlauf und kennt keine Lizenz; beides bleibt bei Queue und `FreemiumPolicy`.

### Datenmodell der Umwandeln-Seite (App)

Alles unter `Kvertis.App/ViewModels/Convert/`. Kein `ConversionJob` und kein Engine-Typ in XAML.

```
ConvertPageViewModel
  Rows : ObservableCollection<ConvertRowViewModel>       eine je Plan.Items, Plan-Reihenfolge
  Inbox : IReadOnlyList<ConvertRowViewModel>              Zeilen mit State Ready/Queued/Paused (links), nach dem Lauf die fehlgeschlagenen
  Swirl : IReadOnlyList<ConvertRowViewModel>              höchstens zwei laufende (früheste StartedAt), Mitte; MoreRunningCount für „und n weitere“
  Done : IReadOnlyList<ConvertRowViewModel>, DoneText     „fertig 3 von 7“ (rechts)
  Location : LocationCardViewModel                       Titel, Erklärung/Pfad, ChangeCommand (Flyout), PickFolderCommand, DropFolder(path), IsEnabled (nur vor dem Start bzw. für wartende Jobs)
  BagText, BagCommand                                    „2 Dateien woanders hin“ / Hinweis
  Skipped : IReadOnlyList<SkippedFile>, ShowProCard, ProReason
  State : RoundState, OverallFraction, OverallText, OverallAutomationText, RemainingText
  CanStart : bool                                        State == Ready, Rows > 0, keine Zeile NeedsFolder
  StartCommand, PauseResumeCommand, CancelAllCommand (mit Rückfrage), BackCommand (nur Ready), NewRoundCommand (nur Finished)
  Report : RoundReportViewModel?                         Titel, Zeile Größe/gespart/Dauer, Fehlerliste, OpenFolderCommand
  Announcement : string                                  Assertive-Text je fertiger/fehlgeschlagener Datei und einmal für den Bericht

ConvertRowViewModel
  Item : PlannedConversion, FileName, KindColorKey, SourceFormat, TargetFormat
  SizeBeforeText, SizeAfterText                           „≈“ aus IEstimator vor dem Lauf, echt aus Result danach
  Preview : TargetPathPreview, TargetDirectoryText (gekürzt), TargetFileName, FullPath (ToolTip, Automation), IsOwnLocation, NeedsFolder
  State : JobState?, ProgressFraction, PercentText, RemainingText, StateText
  ErrorTitle, ErrorBody                                    ErrorMessageMapper.Map(job.Error)
  ChangeTargetCommand (nur vor Start bzw. wartend), OpenCommand, ShowInFolderCommand (nur Completed)
  AutomationName                                           „foto.heic, HEIC nach JPG, wird gespeichert als …, wartend“

LocationCardViewModel
  Kind : OutputLocationKind, CustomPath, TitleText, DetailText, DropCaption
  SetSame/SetSub/SetCustom(path)                           schreibt session.Location, speichert Einstellungen, löst PreviewAll neu aus
```

Ablauf: Betreten → `session.Location ??= aus Einstellungen` → `PreviewAll` → Zeilen. Speicherort oder „Ändern“ → `PreviewAll` → vor dem Start alle Zeilen neu, nach dem Start `coordinator.RelocateWaiting(previews)`. „Umwandeln“ → `coordinator.Start(previews)`. `Changed` → betroffene Zeile aus `job` aktualisieren (`Details` liefert den endgültigen Pfad, `Progress` den Balken, `StateChanged` Zustand, Ergebnis oder Fehler), danach Aggregate. `Finished` → Bericht. „Neue Runde“ → `coordinator.Reset()`, `session.Reset()`, `GoTo(Drop)`.

### Etappen für `ui-entwickler`

| Etappe | Inhalt | Wer | Abnahme |
|---|---|---|---|
| A | `TargetPathPlanner`, `ConversionCoordinator`, `IShellLauncher`, Ergänzungen an `IWorkflowSession`; Tests ohne WinUI (Nummerierung in der Runde, `NeedsFolder`, Start reiht n Jobs, Fremd-Ids ignoriert, `RelocateWaiting` nur wartende, `Report`, `Reset`) | ui-entwickler, tester | `dotnet test tests/Kvertis.App.Tests` |
| B | `ConvertPage.xaml` und ViewModels unter `ViewModels/Convert/`: drei Spalten, Liste, Leiste, Speicherort-Karte mit Flyout und Ordner-Ablage, „Ändern“ je Zeile, Bericht; alle Texte `Convert_*` in DE und EN; `AutomationProperties` überall | ui-entwickler | Sichtprüfung unter Windows, Hoher Kontrast, Tastatur, Erzähler |
| C | Verdrahtung und Abbau: Schritt 1 gibt Start, Speicherort, Gesamtfortschritt, Pause/Abbruch, Pro-Karte, Format-Chip, Preset-Chip, `MorePanel`, `FormatPickerFlyout` ab; `MainViewModel` verliert die Queue-Anbindung; frei gewordene Schlüssel werden gelöscht (Liste im Arbeitsblatt); Schrittleiste sperrt 1 und 2 während des Laufs | ui-entwickler, reviewer | Durchläufe laut Arbeitsblatt; kein `IJobQueue`-Aufruf außerhalb von `ConversionCoordinator` und `HistoryViewModel`; `check.sh` grün |

## Architekturentscheidungen (ADR)

### ADR-001 · 2026-09-23 · Drei Projekte mit harter Grenze zur UI

**Entscheidung:** `Kvertis.Engine` und `Kvertis.Queue` sind reine `net10.0`-Bibliotheken (bis 2026-09-25 `net8.0`) ohne Windows-Abhängigkeit. Windows-spezifisches liegt in `Kvertis.Engine.Windows` und wird per DI eingehängt.
**Alternativen:** (a) Ein Projekt mit Ordnern; (b) Engine direkt auf `net8.0-windows`.
**Grund:** Die Engine soll testbar sein und später unter einer anderen UI (Avalonia für macOS) laufen. Ein Compiler-erzwungener Schnitt ist billiger als Disziplin. Nebeneffekt: Engine und Tests bauen in Linux-CI und in dieser Entwicklungsumgebung.
**Folgen:** Media Foundation und WIC brauchen Schnittstellen (`IMediaFoundationCapabilities`, `IHeicDecoder`) mit einer Windows-Implementierung und einem Null-Objekt für andere Plattformen.

### ADR-002 · 2026-09-23 · FFmpeg als separater Prozess, nie als Bibliothek

**Entscheidung:** Audio und Video laufen über `ffmpeg.exe`/`ffprobe.exe` als Kindprozess, angesteuert über FFMpegCore (MIT) hinter `IProcessRunner`.
**Alternativen:** (a) FFmpeg-DLLs per P/Invoke (FFmpeg.AutoGen); (b) Media Foundation direkt für alles.
**Grund:** Ein separater Prozess erfüllt die LGPL-Auflagen am klarsten (keine Verlinkung, Austausch durch den Nutzer möglich), isoliert Abstürze und erlaubt harte Timeouts und sauberes Beenden. Media Foundation allein deckt Container wie MKV/WebM und Codecs wie VP9/Opus/FLAC nicht ausreichend ab.
**Folgen:** Fortschritt wird aus der `stderr`-Ausgabe (`-progress pipe:2`) geparst. Startzeit pro Job etwa 100 ms, akzeptabel. `IProcessRunner` wird in Tests gemockt.

### ADR-003 · 2026-09-23 · Patentbelastete Codecs nur über Systemcodecs

> **Ergänzt durch ADR-015:** Die Dekodierung läuft nicht mehr über D3D11VA in ffmpeg, sondern vollständig über Media Foundation; die genannten Decoder sind im FFmpeg-Build nicht mehr enthalten.

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

### ADR-011 · 2026-09-23 · HEVC: Software-Fallback von ffmpeg aktiv verhindern

> **Hinfällig seit ADR-015** (kein HEVC-Decoder mehr im Build; `HevcFallbackGuard` entfernt). Bleibt als Historie stehen.

**Entscheidung:** HEVC-Quellen werden nur mit `-hwaccel d3d11va` dekodiert. Da ffmpeg bei fehlgeschlagener Hardware-Initialisierung still auf seinen Software-Decoder zurückfällt und es dafür keine Verbotsoption gibt, überwacht `HevcFallbackGuard` die stderr-Ausgabe und beendet den Prozess beim ersten Fallback-Hinweis mit `MissingSystemCodec`.
**Alternativen:** (a) Software-Fallback dulden; (b) HEVC-Eingaben ganz ablehnen.
**Grund:** ADR-003 verlangt, dass kein mitgelieferter HEVC-Software-Decoder benutzt wird. Die Media-Foundation-Abfrage (`CanDecodeHevc`) ist nur ein Näherungswert; was ffmpeg tatsächlich nutzt, ist der D3D11-Decoder des Grafiktreibers.
**Folgen:** Auf Rechnern ohne HEVC-Hardware-Dekodierung sind HEVC-Videos nicht konvertierbar (klare Meldung). Stream-Copy (Archiv) und reine Tonspur-Extraktion bleiben möglich, weil dort nicht dekodiert wird.

### ADR-012 · 2026-09-23 · ImageMagick mit Sicherheitsrichtlinie betreiben

> **Hinfällig seit ADR-014** (Magick.NET entfernt). Bleibt als Historie stehen.

**Entscheidung:** Vor dem ersten Bildzugriff wird eine `policy.xml` gesetzt (`MagickSecurity`): keine Delegates (externe Programme), keine URL-/Netzwerk- und Skript-Coder (URL, HTTPS, HTTP, FTP, MVG, MSL, TEXT, PS, PDF …), keine Pipes oder `@`-Pfade, feste Ressourcengrenzen (Speicher, Fläche, Zeit). Jeder Lesevorgang übergibt den Coder explizit; Magick darf ihn nie selbst per Inhalt wählen.
**Grund:** Kvertis verspricht „kein Netzwerk“. Eine SVG mit externem Verweis oder ein manipuliertes Bild dürfen die mitgelieferte Bibliothek nicht zu Netzwerkzugriffen oder Ressourcenerschöpfung bringen. Explizite Coder verhindern zudem, dass HEIC/AVIF je über Magicks HEIF-Coder (libde265) laufen.

### ADR-013 · 2026-09-23 · AVIF gesperrt, Entscheidung über Bildbibliothek offen (O-01)

> **Erledigt durch ADR-014:** O-01 ist entschieden (Magick.NET entfernt), AVIF wird über die Windows-Bildkomponente gelesen.

**Entscheidung:** AVIF-Eingaben werden erkannt, aber mit `UnsupportedFormat` („avif blocked until O-01“) abgelehnt, weil ImageMagick AVIF über denselben HEIF-Coder liest, an dem `libde265` hängt. HEIC läuft ausschließlich über Windows Imaging Component.
**Kontext:** Der Prüfbericht in `04-bibliotheken.md` zeigt, dass `Magick.Native` keinen GPL-Code, aber `libde265` (HEVC-Decoder) und `openh264` statisch enthält. Ob das Patentrisiko akzeptiert oder der Bildpfad auf SkiaSharp (MIT, keine Video-Codecs) plus WIC umgestellt wird, entscheidet der Projektinhaber (O-01).

### Ergänzung zu ADR-005 (Umsetzung)

Die Parallelitätsgrenzen sind Zähler unter dem Queue-Lock statt `SemaphoreSlim`, weil ein Semaphor bei einer Verkleinerung von `MaxParallel` zur Laufzeit nicht schrumpfen kann. Pause eines laufenden Jobs: Prozess-Suspend über `NtSuspendProcess` (Windows); wo das nicht geht, wird der Job abgebrochen und mit Zustand „Pausiert“ an den Anfang der Warteschlange gestellt (Neustart bei Fortsetzen). Der Prozess-Timeout zählt während einer Suspendierung nicht weiter. `IConverter` hat keinen Pause-Haken; Pause zwischen Arbeitseinheiten (Seiten, Bilder) ist Phase 2.

### ADR-014 · 2026-09-23 · Bilder über SkiaSharp und Windows Imaging Component, Magick.NET entfernt

**Entscheidung:** Der Bildpfad läuft über SkiaSharp (MIT; native DLL geprüft: nur skia, libjpeg-turbo, libpng, libwebp, zlib, freetype, harfbuzz, expat, ICU, piex, DNG SDK, wuffs). Alles, was Skia nicht kann, übernimmt die Windows Imaging Component über `ISystemImageCodec`: HEIC, AVIF, RAW außer DNG und TIFF lesen; TIFF, BMP, GIF schreiben. ICO schreibt ein eigener kleiner Writer (PNG-in-ICO). Magick.NET ist entfernt.
**Alternativen:** (a) Magick.NET behalten und das Patentrisiko dokumentieren; (b) ausschließlich Windows Imaging Component (dann kein WebP-Schreiben, Engine-Bildteil Windows-abhängig).
**Grund:** Der Projektinhaber hat entschieden, kein Patentrisiko einzugehen. `Magick.Native` enthält libde265 (HEVC-Decoder) und openh264 statisch, auch wenn Kvertis sie nie aufruft; Verbreitung genügt für Patentansprüche. SkiaSharp enthält keine Video-Codecs.
**Folgen:** PSD und SVG entfallen in Phase 1 (keine risikofreie Bibliothek; SVG-Zusatzpakete ziehen LGPL-Abhängigkeiten nach). ICC-Profile werden beim Laden nach sRGB umgerechnet statt eingebettet. HEIC, AVIF, RAW und TIFF brauchen unter Windows die jeweilige System-Erweiterung; auf anderen Plattformen fehlen sie (Null-Implementierung). ADR-012 (ImageMagick-Richtlinie) ist damit hinfällig; Skia kennt weder Delegates noch URL-Loader.

### ADR-015 · 2026-09-23 · Eigener FFmpeg-Build ohne patentbelastete Codecs, Media Foundation für den Rest

**Entscheidung:** Kvertis liefert einen selbst gebauten FFmpeg (LGPL 2.1) aus, in dem Decoder und Encoder für H.264, HEVC, AAC, MPEG-4 Part 2, WMV/WMA/VC-1, H.263, ProRes, DNxHD, DTS, E-AC-3 und AMR **nicht einkompiliert** sind (Allowlist `tools/ffmpeg/configure-allowlist.txt`, `--disable-network`). Eingaben mit diesen Codecs werden vollständig von Windows Media Foundation verarbeitet (`MediaFoundationTranscoder` in `Kvertis.Engine.Windows`, WinRT `Windows.Media.Transcoding.MediaTranscoder`): MP4/MOV/AVI/WMV/MKV mit H.264/HEVC → MP4 (H.264/AAC), M4A/MP3/WAV/FLAC; M4A/WMA → MP3/WAV/FLAC/M4A. Patentfreie Eingaben (WebM, MKV mit VP9/AV1, OGG, Opus, FLAC, WAV, MP3) laufen weiter über FFmpeg, auch nach MP4 (Encoder `h264_mf`/`aac_mf`).
**Alternativen:** (a) Regulärer LGPL-Build mit allen Decodern (verbreitet patentbelastete Decoder, verworfen); (b) Media-Foundation-Source-Reader → Rohdaten per Pipe an FFmpeg (ermöglicht auch H.264 → VP9; deutlich mehr Aufwand, COM-Interop).
**Grund:** Entscheidung des Projektinhabers: keine Verbreitung patentbelasteter Codecs. Media Foundation ist Bestandteil von Windows; Lizenzen liegen beim Betriebssystemhersteller bzw. bei den vom Nutzer installierten Erweiterungen.
**Folgen:** In Phase 1 gibt es keinen Weg von H.264/HEVC-Eingaben nach WebM/MKV (VP9); die Formatmatrix zeigt das (Phase 2: Variante b). Die Routing-Entscheidung trifft `ffprobe` anhand der Codec-Namen der Streams (ffprobe braucht dafür keinen Decoder). `HevcFallbackGuard` (ADR-011) wird gegenstandslos, weil kein HEVC-Decoder mehr existiert; der Build-Nachweis (`FfmpegCompliance`) prüft zusätzlich Decoder und Protokolle. Der FFmpeg-Build muss vor der ersten Auslieferung über den Workflow erzeugt und verifiziert werden (O-02).

### ADR-016 · 2026-09-23 · 3D-Modelle als eigene Medienart, eigene Leser und Schreiber ohne Bibliothek

**Entscheidung:** Neue Medienart `MediaKind.Model3D` mit dem `ModelConverter` (`Kvertis.Engine/Conversion/Models`). Lesen: STL (binär und Text), 3MF (Kernspezifikation), OBJ, PLY (Text, binär little/big endian), glTF 2.0 (GLB und .gltf). Schreiben: STL (binär), 3MF, OBJ, PLY (binär), GLB. Alle Leser und Schreiber sind eigener Code, es kommt keine Bibliothek hinzu. Übernommen wird nur die Dreiecksgeometrie; Farben, Materialien, Texturen und Metadaten fallen weg. Interner Raum: Millimeter, Z nach oben (Konvention des 3D-Drucks); glTF wird beim Lesen und Schreiben von bzw. nach Meter, Y nach oben umgerechnet; 3MF-Einheiten werden ausgewertet. Spiegelnde Transformationen drehen die Dreiecksreihenfolge, damit Flächen außen bleiben.
**Alternativen:** (a) Allzweck-Bibliothek (Assimp, BSD-3): große native Angriffsfläche, bringt einen nachgebauten Importer für ein proprietäres Format mit; (b) SharpGLTF (MIT) nur für glTF: zusätzliche Abhängigkeit für einen kleinen Teil; (c) Windows-3D-Druck-API (`Windows.Graphics.Printing3D`): nur 3MF, nur Windows.
**Grund:** Die Formate sind offen und lizenzfrei; eigene Parser sind klein, prüfbar und plattformneutral (Tests unter Linux). Keine neue Lizenz, keine native DLL, kein Patent- oder Markenbezug.
**Folgen:** Keine Vorschau für 3D in dieser Version (bräuchte einen Renderer; eigenes Thema). Nicht unterstützt: FBX, USDZ, DAE, 3DS, Blend; glTF-Dateien, die Erweiterungen verlangen (z. B. Netzkompression), und dünn besetzte Accessoren (`UnsupportedFormat`). Verknüpfte Dateien werden nie geöffnet (OBJ-Materialdateien, glTF-Bilder); externe glTF-Puffer nur aus dem Ordner des Modells, ohne Schema, ohne absolute Pfade, ohne „..“. Schutz vor feindlichen Dateien: Obergrenzen für Dreiecke (20 Mio.) und Punkte (30 Mio.), PLY-Elementzahlen, 3MF-Modellteil (4 GB entpackt, gegen ZIP-Bomben), XML ohne DTD. 3D ist in der Gratis-Version enthalten.

### Nachtrag zu ADR-001 · 2026-09-25 · Umstellung von .NET 8 auf .NET 10

**Entscheidung:** Alle Projekte (Engine, Engine.Windows, Queue, App, Tests) wechseln von `net8.0` auf `net10.0` bzw. `net10.0-windows10.0.19041.0`. `Directory.Build.props` und `Directory.Packages.props` sind angepasst, die CI baut mit dem .NET-10-SDK.
**Alternativen:** (a) Auf .NET 8 bleiben und erst kurz vor dem Support-Ende wechseln; (b) .NET 9 (kein LTS, Support-Ende bereits Mai 2026).
**Grund:** Der Support für .NET 8 endet im November 2026, also noch vor oder kurz nach der ersten Store-Veröffentlichung. .NET 10 ist LTS mit Support bis November 2028. Windows App SDK 2.5.1 baut damit ohne Änderungen; Engine und Queue sind von der Umstellung nicht betroffen, weil sie nur die Basisklassenbibliothek nutzen.
**Folgen:** Die Schichtenregeln aus ADR-001 gelten unverändert. Neue Bibliotheken müssen `net10.0` oder `netstandard2.0`/`2.1` unterstützen. Wer lokal baut, braucht das .NET-10-SDK (siehe `CLAUDE.md`).

### ADR-017 · 2026-09-25 · Eigene Akzentfarbe statt Systemakzent

**Entscheidung:** Die App definiert ihre Farben selbst als Theme-Ressourcen in einem `ResourceDictionary` mit den Theme-Wörterbüchern `Light`, `Dark` und `HighContrast`. Der Systemakzent (`SystemAccentColor`) wird nicht verwendet. Die Tokens aus dem Oberflächenentwurf (`design/ENTSCHEIDUNGEN.md`, Abschnitt Farben; Hex-Werte in `06-design.md`) sind verbindlich: Mint ist die einzige Aktionsfarbe, Blau steht für Bilder, Violett für Audio, Bernstein für Video, Cyan für Dokumente, Rosa für 3D-Modelle, Koralle für Fehler, jeweils in Hell und Dunkel. Hell/Dunkel folgt der Systemeinstellung (übersteuerbar in den Einstellungen). Im Hohen Kontrast gelten ausschließlich die Systemfarben (`SystemColor*`); das `HighContrast`-Wörterbuch definiert keine eigenen Farbwerte, sondern verweist auf die Systemressourcen. Dateiart und Zustand werden nie allein über Farbe vermittelt (Symbol und Text bleiben Pflicht).
**Alternativen:** (a) Systemakzent für Aktionen, nur die Dateiart-Farben eigen; (b) alles aus den Systemfarben, keine eigenen Tokens.
**Grund:** Die Dateiart-Farben sind Bedeutungsträger über alle drei Schritte hinweg (Bahnen, Pixel, Planeten, Fächer). Ein frei wählbarer Systemakzent kann mit jeder dieser Farben zusammenfallen (blauer Systemakzent neben Blau für Bilder) und die Unterscheidung zerstören; bei (a) bliebe genau dieses Risiko bestehen. Bei (b) gäbe es keine fünf voneinander unterscheidbaren Artfarben mit geprüftem Kontrast in Hell und Dunkel. Eine feste Palette ist zudem im Entwurf bereits gegen 4,5:1 geprüft und lässt sich in einer anderen UI (Avalonia) 1:1 übernehmen.
**Folgen:** `Kvertis.App/Themes/KvertisColors.xaml` (Name verbindlich; umbenannt am 25.09.2026 gegenüber dem ursprünglich genannten `Colors.xaml`, damit die Datei beim Zusammenführen mehrerer Wörterbücher eindeutig bleibt) hält die Tokens als `Color`- und `SolidColorBrush`-Ressourcen in den Theme-Wörterbüchern `Default` (dunkel), `Light` und `HighContrast`. Schlüsselmuster ist `Kv<Token>Color` und `Kv<Token>Brush`: `KvBackground`, `KvPanel`, `KvPanel2`, `KvDeep`, `KvLine`, `KvLineStrong`, `KvInk`, `KvMuted`, `KvMint`, `KvOnMint`, `KvMintFrame`, `KvMintSurface`, `KvImage` (Bilder), `KvAudio`, `KvVideo`, `KvDocument`, `KvModel`, `KvError`. Statt eigener Stile je Bedienelement überschreibt dieselbe Datei in `Default` und `Light` die Akzent-Ressourcen von WinUI (`AccentFillColor*Brush`, `AccentTextFillColor*Brush`, `TextOnAccentFillColor*Brush`, `AccentControlElevationBorderBrush`, `AccentButton*`, `SliderTrackValueFill*`, `SliderThumbBackground*`, `ToggleSwitch*On*`, `CheckBoxCheck*Checked*`, `RadioButton*Checked*`, `ProgressBarForeground`, `ProgressRingForegroundThemeBrush`, `Hyperlink*Foreground`, `ListViewItemSelectionIndicator*`, `InfoBadge*`) auf Mint; die Blattschlüssel werden einzeln überschrieben, weil die `StaticResource`-Verweise in `generic.xaml` schon beim Parsen aufgelöst werden. Im `HighContrast`-Wörterbuch verweisen alle Schlüssel auf Systemfarben. Der Reviewer prüft, dass kein `SystemAccentColor*` mehr in XAML oder Code vorkommt. Der Abschnitt „Farben und Material“ in `06-design.md` ist entsprechend geändert. Das Logo bleibt einfarbig, jetzt in Mint.

### ADR-018 · 2026-09-25 · Win2D als Zeichenschicht für die animierten Teile der Oberfläche

**Entscheidung:** Die animierten Flächen des Oberflächenentwurfs werden mit Win2D (`Microsoft.Graphics.Win2D`, MIT, NuGet, verwaltete Bibliothek mit nativer DLL von Microsoft) gezeichnet: die Fächer-Galaxie in Schritt 1, Pixelwirbel und weißes Loch in Schritt 3 sowie die Übergangs-Überlagerung (Wurmloch, Abschluss). Dafür werden `CanvasControl` (Neuzeichnen auf Anforderung, z. B. Galaxie in Ruhe) und `CanvasAnimatedControl` (eigener Zeichentakt für Wirbel und Übergänge) verwendet. Alle normalen Bedienelemente (Fächer-Listen, Formatwahl, Note und Größenleiste, Speicherort, Knöpfe) bleiben XAML. Bei „Animationen reduzieren“ (`UISettings.AnimationsEnabled`) und im Hohen Kontrast wird die Zeichenschicht gar nicht erst erzeugt; an ihrer Stelle steht eine statische XAML-Ansicht mit demselben Informationsgehalt (Ring, Häkchen, Bericht, Liste). Bedingung: Die Lizenzprüfung durch `lizenz-waechter` läuft parallel; ohne Freigabe und Eintrag in `04-bibliotheken.md` wird das Paket nicht referenziert.
**Alternativen:** (a) Nur die Composition API (Visual Layer): gut für Bewegung weniger Elemente, aber tausende Partikel mit eigener Farbe und Bahn wären tausende `SpriteVisual`s oder eigene Shader-Effekte, unhandlich und langsam; (b) SkiaSharp-Zeichenfläche für WinUI (`SkiaSharp.Views.WinUI`): rendert auf der CPU und kopiert jedes Bild in eine Bitmap, bei 60 Bildern pro Sekunde und großer Fläche zu langsam und stromhungrig, zudem eine weitere Paketvariante der ohnehin genutzten Bildbibliothek; (c) reines XAML mit Storyboards: skaliert nicht auf Partikel und macht die Ansicht unwartbar.
**Grund:** GPU-Rendering über Direct2D für tausende Partikel bei geringer CPU-Last; Microsoft-Bibliothek, die zum Windows App SDK gehört und dieselben Direct2D-Grundlagen wie WinUI nutzt; MIT-Lizenz; enthält keine Codecs und keinen Netzwerkcode. Die Zeichenlogik (Bahnen, Partikel, Zeitverlauf) liegt als reine C#-Klassen ohne Win2D-Typen in `Kvertis.App/Rendering`, damit sie testbar bleibt und der Zeichner austauschbar ist.
**Folgen:** `Kvertis.App` bekommt eine Abhängigkeit auf `Microsoft.Graphics.Win2D` (Eintrag in `04-bibliotheken.md` und `CHANGELOG.md` bei der ersten Verwendung im Code; Lizenztext nach `third_party/`). Engine und Queue bleiben unberührt. Jede Zeichenfläche wird beim Verlassen der Seite freigegeben (`RemoveFromVisualTree`), damit der Zeichentakt nicht weiterläuft. Die Zeichenschicht liefert keine Bedienelemente; alles, was klickbar oder per Tastatur erreichbar sein muss, liegt als XAML darüber oder daneben, mit `AutomationProperties.Name`. Bilder pro Sekunde werden bei Akkubetrieb und im Hintergrundfenster gedrosselt (`TargetElapsedTime`). Auf Geräten ohne passende Grafik (Direct3D-Feature-Level unter 9.3, WARP) greift dieselbe statische XAML-Ansicht wie bei „Animationen reduzieren“.

### ADR-019 · 2026-09-25 · Qualitätsnote als reine Engine-Abbildung (`Kvertis.Engine.Tuning`), nicht als neues Einstellungsfeld

**Entscheidung:** Die Note 0–100 aus Schritt 2 ist kein Feld in `ConversionSettings`, sondern eine Sicht, die ein neuer Namensraum `Kvertis.Engine.Tuning` mit reinen, deterministischen Funktionen hin- und zurückrechnet: `GradeMapper.Apply(grade, baseline, input)` setzt `Quality`, `maxDimension`, `audioBitrateKbps` und `frameRate` je Medienart; `GradeMapper.GradeOf(settings, input)` liefert die Note zu beliebigen Einstellungen (0,6 × Mittel + 0,4 × Minimum der einstellbaren Merkmale); `GradeMapper.Aspects` und `WithAspect` bilden die Zonen (Schärfe, Details, Bewegung, Klang) ab; `EffectAnalyzer.Analyze` liefert „Was sich ändert“ als `EffectCode` mit Schwere und Zahlen, keine Texte. Die Größe je Note kommt aus einer vorab gebauten `GradeSizeTable` (21 Stützstellen je Datei, monoton) über den bestehenden `IEstimator`; dafür wird `Estimator` um `SizeModel.Factor` erweitert, damit die Schätzung auf Qualität, Auflösung und Bitrate reagiert. Die Größenleiste ist eine zweite Ansicht derselben Note (Umkehrung über die Tabelle der größten Datei) und setzt keine Zielgröße; nur „Zielgröße genau“ setzt `TargetSizeBytes`. Dokumente und 3D-Modelle haben keine Note (`SupportsGrade = false`), nur Folgen und „Weiteres“.
**Alternativen:** (a) Note als neues Feld `Grade` in `ConversionSettings`, das jeder Konverter selbst interpretiert; (b) Abbildung Note → Parameter in der App (ViewModel), Engine bleibt wie sie ist; (c) Größenleiste setzt `TargetSizeBytes`, die Konverter suchen dann die Parameter (wie im HTML-Entwurf `loeseJe`).
**Grund:** Bei (a) müssten alle Konverter, der Verlauf, das Tempo-Profil und die Tests umgestellt werden, und derselbe Wert stünde doppelt in den Einstellungen (Note und Qualität); alte Verlaufseinträge hätten keine Note. Bei (b) wäre die Abbildung nicht ohne WinUI testbar und in einer zweiten UI (Avalonia) doppelt zu schreiben; die Zuordnung Note → Bitrate gehört zur Konvertierung, nicht zur Anzeige. Bei (c) würde jedes Ziehen der Leiste eine versteckte Zielgröße erzeugen, die später `TargetSizeUnreachable` auslösen kann, und die Konverter würden bei jeder Datei eine Suche mit mehreren Kodierungen fahren; eine Tabelle über die Note ist deterministisch, sofort und für Ring und Leiste dieselbe Quelle. Reine Funktionen ohne Zustand brauchen keine Abstraktion (kein Interface, kein zweiter Nutzer), sind auf Linux testbar und bleiben frei von UI.
**Folgen:** Neu in `Kvertis.Engine`: `Tuning/QualityGrade.cs`, `Tuning/GradeMapper.cs`, `Tuning/EffectAnalyzer.cs`, `Tuning/GradeSizeTable.cs`, `Tuning/SizeMarks.cs`, `Estimation/SizeModel.cs`; `Estimator.Estimate` multipliziert mit `SizeModel.Factor`, `Record` normalisiert damit, Profil-Schlüssel und Dateiformat des Tempo-Profils bleiben gleich. Keine neue Bibliothek. `ConversionSettings`, `PresetCatalog`, `IConverter` und `IEstimator` bleiben unverändert; `ConversionPreset` bleibt für Verlauf und Marken bestehen, die neue Oberfläche zeigt keinen Preset-Chip mehr. Die App übersetzt `GradeBand` und `EffectCode` über `.resw` (`Grade_Band_*`, `Effect_<Code>_Text`) und entprellt das Ziehen selbst; die Engine ruft beim Ziehen nichts auf. Tests: Rundreise `GradeOf(Apply(g)) == g` für 0..100 und jede Art, Monotonie der Tabelle, Determinismus, kein Wurf für alle Registry-Kombinationen. Die Abbildungstabelle (Note → Auflösungsstufe, Bitratenstufe, Bildrate) steht im Skizzen-Abschnitt oben und darf nach Sichtprüfung angepasst werden; ihre Form (Stufen, linear für `Quality`) nicht ohne Nachtrag hier.

> **Nachtrag 2026-09-25 (Umsetzung):** Die Rundreise `GradeOf(Apply(g)) == g` gilt nicht exakt, weil Auflösung, Bitrate und Bildrate Stufenleitern sind; verbindlich sind Monotonie und eine Toleranz (20 Punkte bei AAC, 12 bei feinen Leitern), die Rückabbildung nimmt die Stufenmitte und wertet „wie Quelle“ als 100. Die Leitern sind feiner als in der Skizze (Schärfe 7 Stufen, Bildrate 6 Stufen). Referenz von `SizeModel.Factor` ist der Auslieferungszustand von `ConversionSettings` (Quality 80, keine Advanced-Schlüssel), nicht Note 80. Verlustfreie Bilder behalten die Schärfe-Zone; `SupportsGrade` ist nur ohne einstellbares Merkmal falsch (Dokumente, 3D, verlustfreies Audio). `SizeMarks.For` liefert `SizeMark`-Records. Einzelheiten im Nachtrag des Skizzen-Abschnitts.

### ADR-020 · 2026-09-25 · Ablaufzustand der drei Schritte in der App (`IWorkflowSession`), Queue bekommt weiter nur `ConversionJob`

**Entscheidung:** Ein Dienst `IWorkflowSession` in `Kvertis.App` hält, was zwischen den Schritten wandert: die erkannten Dateien aus Schritt 1 (`StagedFile` mit `InputInfo` und Miniatur), den `TargetPlan` aus Schritt 2 (`PlannedConversion(InputInfo, ConversionSettings, NamePattern)` je Datei plus `SkippedFile` mit Grund) und den Speicherort aus Schritt 3, dazu `Previous` (der `HistoryEntry` für „Anpassen aus dem Verlauf“). Schritt 3 baut daraus `ConversionJob`s und ruft `IJobQueue.EnqueueRange`. Die Freemium-Grenzen (Video nur Pro, Batch-Limit) prüft die App in Schritt 2 über `FreemiumPolicy` (Gruppe gesperrt, Pro-Karte, Datei in `Skipped`); die Queue-Admission bleibt als zweites Netz.
**Alternativen:** (a) `TargetPlan` in `Kvertis.Queue` als neues Auftragsobjekt, das die Queue selbst in Jobs zerlegt; (b) Zustand in `MainViewModel` belassen und die Seiten daran binden (heutiger Stand); (c) Schritt 2 legt die Jobs schon in die Queue und Schritt 3 startet sie nur.
**Grund:** Bei (a) käme UI-Zustand (Modus „Alle gleich / Jede einzeln“, gesperrte Gruppen) in die Queue, die bewusst nur Jobs, Parallelität und Fortschritt kennt (ADR-005). Bei (b) wächst `MainViewModel` weiter und die drei Seiten hängen an einem Modell, das schon jetzt Ablage, Liste, Start und Fortschritt mischt; die Aufteilung in drei Schritte ist genau die Gelegenheit, das zu trennen. Bei (c) müsste die Queue Jobs ohne Speicherort halten und ein „Ändern“ des Ziels in Schritt 3 hieße Job entfernen und neu anlegen; außerdem würde der Verlauf verfrühte Einträge sehen. `ConversionJob` trägt bereits alles (Eingabe, Einstellungen, Zielordner, Namensmuster, Stapelindex); ein zweites Auftragsobjekt in der Queue hätte keinen zweiten Nutzer.
**Folgen:** Neu in `Kvertis.App/Services`: `IWorkflowSession`, `WorkflowSession` (Singleton je Fenster, `Reset` bei „Neue Runde“), `TargetPlan`, `PlannedConversion`, `SkippedFile`, `StagedFile`. `FreemiumPolicy` bekommt `IsKindLocked(MediaKind)` und `BatchLimit`. `MainViewModel` gibt in Etappe D die Formatwahl, das „Mehr“-Panel und den Start ab; `MorePanel` und `FormatPickerFlyout` werden danach entfernt. `Kvertis.Queue` und `Kvertis.Engine` ändern sich für diese Entscheidung nicht. Der Rückweg „Zurück“ zu Schritt 1 lässt `Plan` bestehen, damit Zielwahl und Note beim erneuten Vorwärtsgehen erhalten bleiben; `Reset` löscht alles.

### ADR-021 · 2026-09-25 · Schritt 3 über einen WinUI-freien `ConversionCoordinator`; Ziel je Datei als Sitzungszustand; Queue bleibt unverändert

**Entscheidung:** Schritt 3 startet die Umwandlung erst auf den Knopf „Umwandeln“, nicht beim Betreten der Seite. Ein Dienst `IConversionCoordinator` in `Kvertis.App/Services` (ohne WinUI, nur `IJobQueue`, `IUiDispatcher`, `TimeProvider`) baut aus `TargetPlan`, gemeinsamem Speicherort und den Ausnahmen je Datei die `ConversionJob`s, ruft `IJobQueue.EnqueueRange`, merkt sich die Ids **seiner Runde**, filtert `JobChanged` darauf, marshallt einmal auf den UI-Thread und liefert Zustand, Gesamtfortschritt und den Abschlussbericht. Er ist die einzige Stelle der App, die für Konvertierungen mit der Queue spricht. Der Zielpfad je Zeile wird vor dem Start als Vorschau mit derselben Rechnung wie `JobRunner` berechnet (`TargetPathPlanner`: `OutputDirectoryResolver` + `OutputNamePattern.Render` + `EnsureUnique` gegen Datenträger und die übrigen Zeilen der Runde); der endgültige Pfad kommt weiterhin von der Queue (`JobChangeKind.Details`). Ausnahmen je Datei („Ändern“, „Tasche“) sind Sitzungszustand (`IWorkflowSession.OwnLocations`), nicht Teil von `TargetPlan` und nicht Teil der Queue; `IWorkflowSession.Location` wird nullable (null = in dieser Runde noch nicht gewählt, dann gilt die Einstellung). Ein Speicherort-Wechsel nach dem Start ersetzt nur wartende Jobs (`IJobQueue.Remove` + neuer Job mit gleichem `BatchIndex`); laufende und fertige bleiben. Zwischenablage-Bilder (unter `AppPaths.ClipboardFolder`) verlangen einen ausdrücklich gewählten Ordner, bevor „Umwandeln“ frei wird. Der Verlauf wird weiter allein von der Queue geschrieben. `Kvertis.Queue` und `Kvertis.Engine` ändern sich nicht.
**Alternativen:** (a) Start beim Betreten von Schritt 3 (kein Start-Knopf); (b) Start-, Fortschritts- und Verlaufslogik in `ConvertPageViewModel` belassen, wie heute in `MainViewModel`; (c) `IJobQueue` um `Replace(jobId, newDirectory)` oder ein veränderliches `OutputDirectory` erweitern; (d) Ausnahmen je Datei als Feld in `PlannedConversion` (Schritt 2 schreibt den Plan neu); (e) Zielpfad erst nach dem Start anzeigen, wenn die Queue ihn gemeldet hat.
**Grund:** (a) nimmt dem Nutzer die letzte Prüfung (Speicherort, Zielpfade, „Ändern“) und widerspricht dem Entwurf, der „Speicherort ändern“ und „Ändern“ genau vor dem Lauf zulässt. (b) wiederholt den heutigen Zustand: `MainViewModel` mischt Ablage, Start, Fortschritt und Verlauf, ist nicht ohne WinUI testbar und wächst mit jeder Anforderung; ein Koordinator ohne UI-Typen ist wie `TargetPlanner` unter Linux testbar und in einer zweiten UI wiederverwendbar. (c) würde `ConversionJob` veränderlich machen, obwohl die Queue bewusst nur unveränderliche Auftragsdaten plus Zustand kennt (ADR-005, ADR-020); `Remove` + neuer Job reicht, weil nur wartende Jobs betroffen sind und die Reihenfolge innerhalb der ersetzten Jobs erhalten bleibt. (d) vermischt die Ergebnisse von Schritt 2 (Format, Einstellungen) mit einer Entscheidung aus Schritt 3 und zwänge Schritt 3, den Plan zu ändern; als eigener Sitzungszustand bleibt der Plan unangetastet und „Zurück“ zu Schritt 2 verliert nichts. (e) ließe die Liste vor dem Start ohne Pfad, obwohl der Entwurf den vollen Zielpfad als Prüfmöglichkeit zeigt; die Rechnung ist dieselbe wie beim Start und billig, Abweichungen sind selten und werden durch das `Details`-Ereignis korrigiert.
**Folgen:** Neu in `Kvertis.App/Services`: `TargetPathPlanner` (rein), `ConversionCoordinator` (`IConversionCoordinator`, Singleton), `ShellLauncher` (`IShellLauncher`, WinRT `Launcher` für „Öffnen“, „Im Ordner zeigen“, „Ordner öffnen“); `IWorkflowSession` bekommt `Location` als `OutputLocation?`, `OwnLocations` und `SetOwnLocation`. Neu `ViewModels/Convert/` mit `ConvertPageViewModel`, `ConvertRowViewModel`, `LocationCardViewModel`, `RoundReportViewModel`. `StepHeader` sperrt Schritt 1 und 2, solange der Koordinator läuft oder pausiert. Nach Etappe C entfallen in `MainViewModel` Start, Speicherort, Gesamtfortschritt, Pause/Abbruch, Verlaufs-Neustart, Pro-Karte und die Queue-Anbindung; `MorePanel` und `FormatPickerFlyout` werden gelöscht, die frei werdenden Ressourcen-Schlüssel ebenfalls (Liste in `docs/entwuerfe/schritt-3-umwandeln.md`). „Abbrechen“ fragt nach; Einzel-Pause, „Erneut versuchen“ je Zeile und die Rückfrage beim Schließen des Fensters während eines Laufs sind spätere Blätter. Keine neue Bibliothek.
