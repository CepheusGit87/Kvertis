# Arbeitsblatt Schritt 1 „Hineinwerfen“ (Fächer-Galaxie, erste Etappe mit Win2D) für den UI-Entwickler

Stand 2026-09-25. Verbindliche Grundlage: `docs/03-architektur.md`, ADR-017, ADR-018, ADR-020 und ADR-022 (Aufbau der Zeichenschicht). Dieses Blatt ist die Kurzfassung zum Abarbeiten; bei Widerspruch gilt `03-architektur.md`. Entwurf: `design/ENTSCHEIDUNGEN.md` (Zeilen „Schritt 1“, „Spielereien in Schritt 1“, „Übergänge und Abschluss“, „Farben“), `design/faecher-galaxie.html` (Funktionen `zustand`, `kamera`, `punkt`, `zeichnen`, `V.fach.flug`, `K.k3`, `teilchen`) und der Abschnitt „Schritt 1“ in `design/oberflaeche-mischentwurf.html` (dieselbe Zeichenlogik, dieselben Zahlen).

Erste Etappe mit der Zeichenschicht: `Microsoft.Graphics.Win2D 1.4.0` (Lizenz freigegeben, Prüfbericht in `04-bibliotheken.md`; Auflage: **keine Win2D-Bildladefunktionen für Nutzerdateien**, Vorschaubilder bleiben in XAML). Übergänge (Wurmloch), Pixelwirbel und weißes Loch sind eigene spätere Blätter. Alle Farben nur aus `Themes/KvertisColors.xaml`.

## Was gebaut wird

`Views/MainPage.xaml` bekommt einen neuen Aufbau. Oben die Galaxie, darunter immer alle fünf Fächer, ganz unten die unveränderte Leiste mit Vertrauenszeile und „Weiter: Ziel“.

| Bereich | Jetzt (Win2D + XAML) | Was aus der heutigen `MainPage` übergeht |
|---|---|---|
| Galaxie (oben) | `GalaxyCanvas` (Hülle um `CanvasAnimatedControl`, `Rendering/`) in einem `GalaxyHost`-Grid. Höhe `2*` gegenüber `3*` für die Fächer, Mindesthöhe 240 px, Höchsthöhe 520 px. Fünf Umlaufbahnen um ein schwarzes Loch, ohne Beschriftung; eingeworfene Dateien kreisen als Planeten mit Teilchenhof; neue Dateien fliegen vom Einwurfpunkt oben auf ihre Bahn. Darüber als XAML: die Einwurf-Zeile („Dateien hierher ziehen oder auswählen“, „Dateien wählen“, „Ordner wählen“, Hinweis Strg+V) oben mittig auf einer halbdurchsichtigen Karte (`KvGlasBrush`, neu anzulegen); rechts oben die Karte „Nicht umwandelbar“ mit Namen und Grund je Datei. Die ganze Seite ist Ablageziel (`AllowDrop` auf `PageRoot`), nicht nur die Karte | Ablagefläche (`DropZone`), `Main_DropZone_*`, `Main_AddFiles_Button`, `Main_AddFolder_Link`, `Main_DropZone_PasteHint`; die schmale Leiste „Weitere Dateien hinzufügen“ entfällt, weil die Einwurf-Zeile immer sichtbar bleibt |
| Fächer (Mitte) | Fünf `TrayControl` (XAML, `Views/TrayControl.xaml`) in einem `Grid` mit fünf gleich breiten Spalten, immer alle fünf, auch leere. Reihenfolge von links nach rechts **Bilder, Audio, Video, 3D-Modelle, Dokumente** (das ist die Bahnfolge von außen nach innen; Schritt 2 zeigt Dokumente vor 3D, siehe `TargetPlanner.KindOrder`; die Flugbahnen des späteren Wurmlochs berücksichtigen das). Kopf: Farbpunkt (`Kv<Art>Brush`), Name, Zahl (groß, in der Artfarbe; bei 0 klein und in `KvMuted`). Inhalt: eine Zeile je Datei mit Vorschaubild (48 px, `Image` aus `BitmapImage` wie heute) oder Art-Symbol, Dateiname, Größe, Formatschild, bei `InputWarning.ExtensionMismatch` statt der Größe die Warnzeile „Endung .jpg, Inhalt ist PNG“ (`Tray_ExtensionMismatch_Text` mit zwei Platzhaltern), sonstige Warnungen darunter wie heute. Leeres Fach: „noch leer, zum Beispiel“ plus bis zu fünf Formatschilder der lesbaren Formate. Fuß: „öffnet **11** → macht **8** Formate“ aus `FormatRegistry.All` (`Kind == k && CanRead` bzw. `CanWrite && IsProducible(id, codecs)`). Ab vier Dateien wird im Fach gerollt (`ScrollViewer`, `VerticalScrollMode=Auto`). Der Fachkopf ist ein `ToggleButton` (öffnet den Zoom, siehe unten); „Entfernen“ je Zeile bleibt | Job-Liste (`JobList`), `JobCardControl` (wird zu `TrayFileControl`, die Karte entfällt), Miniatur-Ladung (`MainViewModel.LoadThumbnailAsync` unverändert), Warnungen, Zustände Erkennen/Bereit/Abgelehnt, Entf auf der markierten Zeile |
| Nicht umwandelbar (rechts oben) | Karte auf der Galaxie: Titel „Nicht umwandelbar“, je Datei Name und Grund (`ErrorMessage.Title` aus `ErrorMessageMapper`, wie heute `SetRejected`), Knopf „Entfernen“ je Zeile. Höchstens drei Zeilen sichtbar, danach „und n weitere“ mit Aufklappen | Abgelehnte Karten aus der Job-Liste (`JobItemState.Rejected`) |
| Leiste (unten) | unverändert: Vertrauenszeile, `ProgressRing` beim Hinzufügen, „Weiter: Ziel“ als einziger Mint-Knopf | `Main_Trust_Text`, `Main_Adding_Progress`, `Main_GoTarget_Button` |

Fenster unter 900 px Breite: die Fächer werden zu einem `ScrollViewer` mit fünf Spalten von je 200 px (`AdaptiveTrigger`); unter 640 px Höhe fällt die Galaxie auf ihre Mindesthöhe. Der Verlauf (`HistorySplitView`) bleibt wie er ist.

**Entfällt aus `MainPage`:** `DropZone` samt Hover-Animation (`CardAnimations.DropZoneHover` wird nicht mehr aufgerufen; die Methode bleibt für die Ordner-Ablage in Schritt 3), die schmale Leiste „Weitere Dateien“, `JobList` mit `ContainerContentChanging` und `CardEntrance` (das Erscheinen im Fach ist die Fach-Animation unten), `Views/JobCardControl.xaml(.cs)`. **Bleibt:** Tastatur (Strg+O, Strg+V, Entf, Enter), `MainViewModel` mit Erkennung, Miniaturen, `PublishStaged`, `GoToTargetCommand`; `JobItemViewModel` bleibt das Modell einer Datei und wird das Element eines Fachs.

## Ordnung der Arten und Bahnen

| Bahn (innen → außen) | Art | `MediaKind` | Farbtoken | Fach (links → rechts) |
|---|---|---|---|---|
| 0 | Dokumente | `Document` | `KvDocument` (cyan) | 5 |
| 1 | 3D-Modelle | `Model3D` | `KvModel` (rosa) | 4 |
| 2 | Video | `Video` | `KvVideo` (bernstein) | 3 |
| 3 | Audio | `Audio` | `KvAudio` (violett) | 2 |
| 4 | Bilder | `Image` | `KvImage` (blau) | 1 |

Abgelehnte Dateien haben keine Bahn; sie fliegen in `KvError` (koralle) zur Karte „Nicht umwandelbar“.

## Parameter für den C#-Port

Alle Zahlen aus `design/faecher-galaxie.html`; „px“ sind DIP bei Skalierung 1, im Code über `GalaxyLayout.Scale` an die Fläche angepasst: `Scale = clamp(min(W / 700, H / 280), 0.6, 1.5)`. Zeitkonstanten stehen als Sekunden; `k` ist der Bahnindex 0..4 von innen. Wo der Entwurf `1 - exp(-dt * r)` glättet, steht hier die Rate `r` (1/s); der Port übernimmt genau diese Form, damit das Verhalten bildratenunabhängig bleibt.

| Größe | Wert | Quelle im Entwurf |
|---|---|---|
| Mittelpunkt | `(W/2, H/2 + 10 px)`; Einwurfpunkt `(W/2, 24 px)` | `S.c`, `S.drop` |
| Bahnradius (waagerecht) | `rx_k = (92 + 56·k) · Scale` → 92, 148, 204, 260, 316 | `basis`, `r0`, `dr` |
| Abplattung | `ry = rx · 0,35`; im Zoom `0,50` | `S.fl`, `+ .15` |
| Umlaufgeschwindigkeit | `ω_k = 0,2 − 0,026·k` rad/s → Umlaufzeiten 31 s, 36 s, 42 s, 52 s, 65 s; Faktor `(1 + 0,5·zoomed + 0,6·hover)`; bei Reduced Motion 0 (nicht relevant, die Szene wird dann nicht erzeugt) | `W.ph[i] +=` |
| Startphase je Bahn | zufällig `[0, 2π)` aus dem übergebenen `Random` (Seed im Test fest) | `ph: Math.random()` |
| Planetenwinkel | `A = n · 1,9 rad + phase_k`, `n` = laufende Nummer der Datei seit Rundenbeginn | `planetA` |
| Planetengröße | `gr = clamp(0,6 + 0,45·√(MB/20), 0,6, 1,8)`; Hofradius `(3 + 7·z) · skal · gr` px mit `z` je Teilchen zufällig, `skal = √clamp(rx / rx_basis, 0,5, 2,6)` | `gr`, `pr` |
| Planetenhof | 34 Teilchen je Datei, Quadrat 2,4 px · skal, Alpha 0,95, Drehung 2 rad/s, senkrecht auf 0,6 gestaucht | `teilchen`, `f ? 2.4` |
| Bahnstaub | 160 Teilchen je Bahn, Quadrat 1,6 px · skal, Alpha 0,55, radialer Versatz `(z − 0,5) · 14 px · skal` | `teilchen`, `1.6` |
| Teilchenmischung | Dunkel: additiv (`CanvasBlend.Add`), Hell: normal | `'lighter'` |
| Bahnlinie | Alpha `0,16 + 0,3·zoomed + 0,3·hover + 0,15·dragOver`, Breite `1 + 0,8·zoomed + 0,8·hover` px, Glühen `10·max(zoomed, hover)` px, 110 Segmente | Bahnen-Schleife |
| Hover-Erkennung | nächste Bahn, wenn der Abstand zur Ellipse < 40 px; Hover-Wert je Bahn gleitet mit Rate 6/s auf 0 oder 1 | `best = 40`, `rh` |
| Bahn-Beule | Bahn wölbt sich unter dem Zeiger um `19 px · hover · exp(−Δα² / 0,34)` nach außen (`Δα` = Winkelabstand zum Zeigerwinkel) | `punkt`, `bul` |
| Zeiger-Sog | Zeigerposition geglättet mit Rate 14/s; Einfluss blendet ein mit Rate 5/s, aus mit 3,5/s; Radius `lerp(115, 185, hover)` px; Verschiebung `d⃗ · lerp(0,1, 0,4, hover) · (1 − d/Radius)² · Einfluss` für Staub und Höfe | `gm`, `rr`, `g` |
| Ziehen über dem Fenster | `dragOver` gleitet mit Rate 8/s; Teilchen rücken `10 % · (1 − z/2)` zum Loch, Loch wächst `× (1 + 0,7·dragOver)`, Mint-Hof 160 px mit Alpha 0,12 | `W.zw` |
| Loch | Radius 18 px (Übersicht), 10 px (Zoom); Höfe: violett `3,2·r` Alpha 0,18, mint `1,9·r` Alpha 0,22; Scheibe schwarz (`#000000` dunkel, `#0B0F12` hell); Mint-Ring bei `r + 1,5`, Breite 2, Glühen 12 px | `loch`, `lochR` |
| Kamera (Zoom) | Fahrt 1,15 s, Kurve `ease` (kubisch ein/aus); gezoomte Bahn `rx = min((W − 600 px)/2, 240 px)`; innere Bahnen auf `0,1×`, äußere auf `3,1×`, Alpha → 0; ein neuer Zielwechsel startet weich aus dem laufenden Zustand | `zustand`, `kamera`, `fahren` |
| Anflug | Start `t0 = 0,1 s + 0,16 s · i` nach dem Einwurf (i = Position im Stapel); Dauer 0,75 s; quadratische Bézier-Kurve vom Einwurfpunkt (`y + 18 px`) zum Bahnpunkt, Kontrollpunkt `(x_mitte, min(y_start, y_ziel) − 30 px)`; Kurve `ease`; Schweif 10 Punkte (Alpha bis 0,5, Radius 1..4 px); Hof 16 px Alpha 0,5; Formatschild über dem Flugobjekt | `V.fach.flug`, `bez` |
| Ankunft | Hof-Teilchen sammeln sich 0,8 s lang aus dem Ankunftspunkt (Streuung 30 px, Kurve `ease`); Schild mit Name 2,8 s sichtbar (0,3 s ein, 0,5 s aus); Fach-Zahl hüpft 0,45 s (Feder) | `f.tAn`, `hupf` |
| Zoom-Wege | je Weg 7 Punkte, Umlauf 0,45/s, Farbe der Art, ab 40 % des Wegs nach Mint; empfohlener Weg Mint Alpha 0,55 Breite 2, andere Alpha 0,25 Breite 1; zwei Bézier-Hälften über `(c.x ∓ 70 px)`; Bezugslinien Alpha 0,45 mit Anlauf 1,6·sicht − 0,05·j | `wegZeichnen`, `bezugslinien` |
| Zoom-Automatik | ohne Nutzerwahl wechselt das linke Format alle 2,6 s; nach einer Wahl 6 s Sperre (8 s beim Klick) | `wahlNext`, `wahlGesperrt` |
| Kurven | `ease(t) = t < 0,5 ? 4t³ : 1 − (−2t + 2)³ / 2`; `glatt(t) = t²(3 − 2t)`; `easeOut(t) = 1 − (1 − t)³`; `easeBack(t) = 1 + 2,2(t − 1)³ + 1,2(t − 1)²` | Kopf der Datei |
| DPI | Entwurf: höchstens 2× Gerätepixel. Win2D zeichnet nativ in Geräte-DPI; `GalaxyCanvas` begrenzt über `CanvasAnimatedControl.DpiScale` auf höchstens 2,0 | `devicePixelRatio` |

Farben je Dateiart (Teilchen, Bahn, Schild, Fachpunkt) kommen ausschließlich aus `KvImageColor`, `KvAudioColor`, `KvVideoColor`, `KvDocumentColor`, `KvModelColor`; Loch und Wege aus `KvMintColor` und `KvAudioColor` (violetter Hof); Abgelehntes aus `KvErrorColor`; Schilder aus `KvBackgroundColor`/`KvInkColor`; Schild-Untertitel `KvMutedColor`. Die Tokens `glas`, `paper`, `paper-linie`, `schatten` aus der Tabelle in `06-design.md` werden in dieser Etappe als `KvGlas*`, `KvPaper*`, `KvPaperLine*`, `KvShadow*` in `KvertisColors.xaml` angelegt (Hell, Dunkel, Hoher Kontrast).

## Szene-Modell (WinUI-frei, `Kvertis.App/Scenes/`)

Reine C#-Klassen ohne Win2D- und ohne WinUI-Typen; kompiliert per `Compile Include` in `Kvertis.App.Tests`. Zeitbasis ist ausschließlich das `TimeSpan`, das `Update` bekommt; kein `DateTime`, kein `Stopwatch` in der Szene. Zufall nur aus dem injizierten `Random` (Seed).

```csharp
namespace Kvertis.App.Scenes;

/// <summary>Colour as plain RGB so the scene never references a UI colour type.</summary>
public readonly record struct SceneColor(byte R, byte G, byte B);

/// <summary>The five kind colours plus mint, error, ink, background, muted; filled by the view from KvertisColors.xaml.</summary>
public sealed record ScenePalette(
    SceneColor Image, SceneColor Audio, SceneColor Video, SceneColor Document, SceneColor Model3D,
    SceneColor Mint, SceneColor Error, SceneColor Ink, SceneColor Background, SceneColor Muted, bool IsDark)
{
    public SceneColor For(MediaKind kind) => ...;
}

/// <summary>Orbit geometry for one canvas size (pure functions of width/height, ADR-022).</summary>
public sealed record GalaxyLayout(float Width, float Height)
{
    public const int OrbitCount = 5;
    public static readonly IReadOnlyList<MediaKind> OrbitOrder = [Document, Model3D, Video, Audio, Image]; // inner → outer
    public float Scale { get; }                       // clamp(min(W/700, H/280), 0.6, 1.5)
    public Vector2 Center { get; }                    // (W/2, H/2 + 10*Scale)
    public Vector2 Entry { get; }                     // (W/2, 24*Scale)
    public float ZoomRadius { get; }                  // min((W - 600)/2, 240) * Scale, never below 120*Scale
    public static int OrbitOf(MediaKind kind);        // 0..4, throws for Unknown
    public float BaseRadius(int orbit);               // (92 + 56*orbit) * Scale
}

public enum BodyPhase { Waiting, Approaching, Orbiting, Rejected, Removed }

/// <summary>One staged file in the scene. Mutable, only touched on the scene thread.</summary>
public sealed class GalaxyBody
{
    public Guid Id { get; }                           // = JobItemViewModel.Id
    public MediaKind Kind { get; }                    // Unknown = rejected
    public string FormatLabel { get; }                // chip text, e.g. "HEIC"
    public string Name { get; }                       // chip subtitle after arrival
    public float SizeFactor { get; }                  // gr from bytes
    public int Ordinal { get; }                       // n for the orbit angle
    public BodyPhase Phase { get; internal set; }
    public Vector2 Position { get; internal set; }    // current screen position
    public float Angle { get; internal set; }         // on orbit
    public TimeSpan PhaseStart { get; internal set; }
    public IReadOnlyList<Vector2> Trail { get; }      // up to 10 points while approaching
}

/// <summary>A dust or halo particle. Struct array for cache friendliness; drawn as a square.</summary>
public struct GalaxyParticle { public int Orbit; public int BodyIndex; /* -1 = dust */ public float R1, R2, R3; public Vector2 Position; public float Size, Alpha; public SceneColor Color; }

public enum ZoomState { Overview, ZoomingIn, Zoomed, ZoomingOut }

/// <summary>Everything a view needs to know without touching the scene thread. Replaced atomically after each Update.</summary>
public sealed record GalaxySnapshot(
    ZoomState Zoom, MediaKind? ZoomKind, float ZoomProgress,
    int? HoveredOrbit, IReadOnlyDictionary<Guid, Vector2> BodyPositions, TimeSpan Time);

/// <summary>Commands from the UI thread; applied at the start of the next Update.</summary>
public abstract record GalaxyCommand;
public sealed record AddBody(Guid Id, MediaKind Kind, string FormatLabel, string Name, long SizeBytes) : GalaxyCommand;
public sealed record RemoveBody(Guid Id) : GalaxyCommand;
public sealed record RejectBody(Guid Id) : GalaxyCommand;          // Kind stays Unknown, flies to the rejected card
public sealed record SetPointer(Vector2? Position) : GalaxyCommand; // null = pointer left
public sealed record SetDragOver(bool Active) : GalaxyCommand;
public sealed record SetTrayHover(MediaKind? Kind) : GalaxyCommand; // tray hovered → orbit lights up
public sealed record ZoomTo(MediaKind? Kind) : GalaxyCommand;      // null = back to overview
public sealed record SetPathAnchors(IReadOnlyList<PathAnchor> Left, IReadOnlyList<PathAnchor> Right, string? SelectedInput, IReadOnlySet<string> Reachable, string? Recommended) : GalaxyCommand;
public sealed record SetRejectedAnchor(Vector2 Position) : GalaxyCommand;
public sealed record Resize(float Width, float Height) : GalaxyCommand;
public sealed record Clear() : GalaxyCommand;

public sealed record PathAnchor(string FormatId, Vector2 Position);   // canvas coordinates of a list row's edge

/// <summary>
/// The galaxy of step 1 without any drawing: orbits, bodies, particles, pointer influence, zoom camera and
/// the timing of approaches (ADR-022). Update runs on the drawing thread; the UI thread only enqueues
/// commands and reads Snapshot.
/// </summary>
public sealed class GalaxyScene
{
    public GalaxyScene(GalaxyLayout layout, ScenePalette palette, Random random, GalaxyBudget budget);

    public GalaxyLayout Layout { get; }
    public ScenePalette Palette { get; set; }          // theme change: view sets a new palette
    public TimeSpan Time { get; }                      // sum of all elapsed
    public IReadOnlyList<GalaxyBody> Bodies { get; }
    public ReadOnlySpan<GalaxyParticle> Particles { get; }
    public float[] OrbitPhase { get; }                 // 5 entries
    public float[] OrbitHover { get; }                 // 0..1, eased
    public float[] OrbitRadiusX { get; }               // current (camera) rx per orbit
    public float[] OrbitFlatten { get; }
    public float[] OrbitAlpha { get; }
    public float HoleRadius { get; }
    public float DragOver { get; }                     // 0..1, eased
    public PointerInfluence Pointer { get; }           // smoothed position + strength
    public ZoomState Zoom { get; }
    public MediaKind? ZoomKind { get; }
    public float ZoomProgress { get; }
    public IReadOnlyList<GalaxyPath> Paths { get; }    // zoom: from selected input to each reachable output
    public GalaxySnapshot Snapshot { get; }            // volatile reference, replaced at the end of Update
    public PointerDwell Dwell { get; }                 // hook for the later gimmicks (see below)

    public void Enqueue(GalaxyCommand command);        // thread-safe (ConcurrentQueue)
    public void Update(TimeSpan elapsed);              // applies commands, advances everything, publishes Snapshot
    public Vector2 OrbitPoint(int orbit, float angle, float radialOffset = 0);   // pure, with hover bulge
    public int? OrbitAt(Vector2 point, float tolerance = 40);                     // hit test in overview
}

/// <summary>Particle limits (docs/entwuerfe/schritt-1-galaxie.md, "Leistung").</summary>
public sealed record GalaxyBudget(int DustPerOrbit = 160, int HaloPerBody = 34, int HaloPerBodyManyFiles = 12, int ManyFilesFrom = 30, int MaxParticles = 4000);

/// <summary>How long the pointer has rested on the same orbit or on the hole; the gimmicks read it later.</summary>
public readonly record struct PointerDwell(int? Orbit, bool OnHole, TimeSpan Duration);
```

Regeln:

- `Update(elapsed)` klemmt `elapsed` auf höchstens 100 ms (nach einer Pause springt nichts). Alle Glättungen als `1 - exp(-dt * rate)`.
- Kamera: `ZoomTo(kind)` startet eine Fahrt von 1,15 s aus dem **aktuellen** Zustand (Radien, Abplattung, Alpha, Lochradius werden beim Start als Ausgangswerte eingefroren, wie `fahren` im Entwurf). `ZoomState` wird `Zoomed`, sobald die Fahrt fertig ist; `Paths` gibt es nur bei `ZoomProgress > 0,6`.
- Anflug: `AddBody` legt den Körper als `Waiting` mit `PhaseStart = Time + 0,1 s + 0,16 s · (Anzahl wartender Körper)` an. Bei `RejectBody` fliegt er zur `RejectedAnchor`. Die Flugkurve ist eine reine Funktion `GalaxyMotion.Approach(from, control, to, t)`, damit der Test sie an festen Zeitpunkten abfragen kann.
- Höfe: bei `AddBody` werden `HaloPerBody` Teilchen mit `BodyIndex` angelegt (bei mehr als `ManyFilesFrom` Körpern nur `HaloPerBodyManyFiles`); `RemoveBody` entfernt sie. `MaxParticles` gilt hart; darüber bekommen neue Körper keinen Hof, nur den Planeten-Punkt.
- `Snapshot` enthält die Bildschirmpositionen der Körper, damit die Fach-Zeile beim Hover ihren Planeten hervorheben kann (und später Tooltips); die Szene weiß nichts von Fächern.
- Keine Texte in der Szene: `FormatLabel` und `Name` kommen fertig aus dem ViewModel, die Szene reicht sie nur an den Renderer weiter.

## Renderer und Canvas-Hülle (`Kvertis.App/Rendering/`)

- `GalaxyRenderer` (Win2D): `Draw(CanvasDrawingSession ds, GalaxyScene scene)`. Reihenfolge wie im Entwurf: Bahnen (Polylinie aus 110 `OrbitPoint`-Aufrufen als `CanvasPathBuilder`, einmal je Bild und Bahn), Teilchen (`FillRectangle` je Teilchen; `ds.Blend = CanvasBlend.Add` im Dunkeln), Flugobjekte mit Schweif und Schild, Ankunftsschilder, Zoom-Wege und Bezugslinien, Loch. **Glühen ausschließlich über `CanvasRadialGradientBrush`** (je Farbe einmal erzeugt und im Renderer gecacht, bei Palettenwechsel neu); kein `GaussianBlurEffect` je Bild, keine `CanvasCommandList`. Schilder: `CanvasTextFormat` (Segoe UI Variable, 9,5 pt, Gewicht 600), Breiten in einem Wörterbuch gecacht wie `breiten` im Entwurf; `DrawText` nur für Schilder der Ankunft und des Flugs, nie für die Fächer.
- **Keine Bitmaps aus Nutzerdateien im Canvas.** `CanvasBitmap.LoadAsync`, `CanvasBitmap.CreateFromBytes` mit Dateiinhalten und `CanvasImageSource` sind in `Rendering/` verboten; der Reviewer prüft das per Suche. Vorschaubilder bleiben `Image` in den Fächern. Falls die Teilchen später über `CanvasSpriteBatch` gezeichnet werden (siehe Leistung), ist der einzige erlaubte Sprite ein zur Laufzeit erzeugtes 4×4-Weißquadrat (`CanvasRenderTarget`), keine Datei.
- `GalaxyCanvas` (`UserControl`, `Rendering/GalaxyCanvas.xaml`): hält ein `CanvasAnimatedControl` mit `TargetElapsedTime = 1/60 s`, `IsFixedTimeStep = false`, `ClearColor = Transparent`. `CreateResources` baut Renderer und Paletten-Pinsel; `Update` ruft `scene.Update(args.Timing.ElapsedTime)`; `Draw` ruft `renderer.Draw`. Die Szene wird von außen gesetzt (`Scene`-Eigenschaft), gehört dem ViewModel und überlebt das Zerstören der Hülle. Zeigerereignisse (`PointerMoved`, `PointerExited`) auf der Hülle erzeugen `SetPointer`; `Tapped` in der Übersicht fragt `scene.OrbitAt` (rein, UI-Thread-sicher) und ruft `ZoomRequested(kind)`; `SizeChanged` erzeugt `Resize`. `Suspend()` setzt `Paused = true`, entfernt das Steuerelement (`RemoveFromVisualTree`) und wirft die Referenz weg; `Resume()` erzeugt es neu. Nach `Suspend()` hält die Hülle keine Win2D-Objekte mehr.
- **Thread-Regel:** `Update` und `Draw` laufen auf dem Win2D-Spielschleifen-Thread. Nur dort wird die Szene verändert oder gelesen; der UI-Thread benutzt ausschließlich `Enqueue`, `Snapshot`, `Layout` und `OrbitAt`. Kein `Dispatcher`-Aufruf aus `Update`. Fehlerbehandlung: eine Ausnahme in `Draw` wird geloggt und die Hülle schaltet auf die statische Ansicht um (kein Absturz wegen Grafik).
- Gerätewechsel: `CanvasAnimatedControl` behandelt `DeviceLost` selbst; `CreateResources` muss deshalb alles neu bauen können (keine Pinsel außerhalb der Session behalten). Geräte ohne passende Grafik (Feature-Level unter 9.3 oder reines Software-Rendering `CanvasDevice.IsDeviceLost`/`WARP`-Erkennung über `CanvasDevice.GetSharedDevice().MaximumBitmapSizeInPixels < 4096` als Näherung, genauer über `Direct3D` Feature-Level, falls Win2D es hergibt) → `GalaxyHost` fällt auf die statische Ansicht zurück (ADR-018).
- Palette: `ScenePalette.FromResources(FrameworkElement)` liest die `Kv*Color`-Ressourcen des aktuellen Themes; `ActualThemeChanged` setzt `scene.Palette` neu und lässt den Renderer seine Pinsel wegwerfen.

## Aufbau in `MainPage`: `GalaxyHost`

`Views/GalaxyHost.xaml` (UserControl) ist die einzige Stelle, die entscheidet, was oben zu sehen ist:

| Zustand (`IMotionSettings`) | Inhalt | Wann geprüft |
|---|---|---|
| Animationen an, kein Hoher Kontrast | `GalaxyCanvas` mit der Szene; Einwurf-Zeile und Karte „Nicht umwandelbar“ als XAML darüber | Beim Laden, bei `IMotionSettings.Changed`, bei `OnNavigatedTo` |
| Animationen aus (`AnimationsEnabled = false`), kein Hoher Kontrast | `GalaxyStaticView`: fünf konzentrische `Ellipse` (Strich `Kv<Art>Brush`, Breite 1,5, Abplattung 0,35, Radien wie `GalaxyLayout`), Loch als gefüllter Kreis mit Mint-Rand; je Datei ein 6-px-Punkt an fester Position auf seiner Bahn (Winkel `n · 1,9 rad`, keine Bewegung); keine Teilchen, kein Flug; Einwurf-Zeile und Karte wie oben | dito |
| Hoher Kontrast | Keine Galaxie: `GalaxyHost` bricht auf 0 Höhe zusammen, die Fächer bekommen die volle Fläche; Einwurf-Zeile steht als normale Karte über den Fächern, „Nicht umwandelbar“ daneben. Grund: im Hohen Kontrast fallen alle fünf Artfarben auf die Textfarbe zusammen (ADR-017); Ringe ohne Farbe tragen keine Information | dito |

Beim Umschalten wird das jeweils andere Steuerelement vollständig entfernt (`Content = null`, bei der Canvas `Suspend()`), nicht nur ausgeblendet. Der Zoom (unten) verhält sich in der statischen Ansicht wie ein Aufklappen ohne Fahrt: die Formatlisten erscheinen sofort, die Ringe bleiben.

## Zoom „Wege durchs Loch“

Auslöser: Klick auf einen Fachkopf (`ToggleButton`, `IsChecked` ↔ `MainViewModel.ZoomKind`), Klick auf eine Bahn (`GalaxyCanvas.ZoomRequested`), Tastatur: Enter/Leertaste auf dem Fachkopf. Zurück: derselbe Fachkopf, Knopf „Übersicht“ (`Galaxy_Back_Button`, links oben im Overlay, `AccessKey` Esc über `KeyboardAccelerator`), Esc, oder Klick auf das Loch.

| Teil | Wird gezeichnet (Canvas) | Ist XAML (`Views/PathsOverlay.xaml`) |
|---|---|---|
| Kamerafahrt | Bahn der Art fährt auf `ZoomRadius`, die anderen verschwinden; Loch schrumpft auf 10 px; Planeten der Art bleiben und parken gleichmäßig auf der Bahn (`pk` im Entwurf: Winkel gleiten mit Rate 3/s auf feste Plätze) | – |
| Links „Kvertis öffnet“ | Bezugslinien von jeder Zeile zur Bahn (linke Halbbahn, Winkel `π + 0,95 − t · 1,9`) | Liste der lesbaren Formate der Art als `RadioButton`-Gruppe (`ItemsRepeater`), je Zeile Formatschild und Kurztext (`Format_<Id>_Hint`), Zeilen der eingeworfenen Formate mit Farbpunkt markiert; die gewählte Zeile bestimmt die Wege |
| Mitte | Wege vom gewählten Format durch das Loch zu jedem erreichbaren Ziel, Empfehlung in Mint | Titel der Art (`Kind_<Kind>_Name`, `Kind_<Kind>_Sub`) oben mittig |
| Rechts „kann werden zu“ | Bezugslinien (Mint, rechte Halbbahn) | Liste der schreibbaren Formate der Art, nicht erreichbare gedimmt (`IsEnabled = false`, zusätzlich Text „nicht von {0}“), Empfehlung mit Schild „empfohlen“ (`Galaxy_Recommended_Tag`) |
| Fächer | – | `VisualState Zoomed`: Fächer werden zur Leiste mit 52 px Höhe (nur Kopf: Punkt, Name, Zahl); das gewählte Fach ist „an“ (`Kv<Art>Brush`-Rand) |
| Einwurf-Zeile, Nicht umwandelbar | – | ausgeblendet (`Visibility.Collapsed`), Ablage bleibt trotzdem möglich (Seite ist Ablageziel); ein Einwurf im Zoom fährt zurück in die Übersicht wie `einwerfen` im Entwurf |

Das Overlay meldet der Szene die Ankerpunkte seiner Zeilen (`SetPathAnchors`) in `LayoutUpdated`, entprellt über den `Debouncer` (50 ms), in Canvas-Koordinaten (`TransformToVisual(GalaxyCanvas)`). Die Anker stehen dann in der Szene; der Renderer zeichnet die Wege von dort. Ohne Nutzerwahl wechselt die Szene das gewählte Eingangsformat selbst alle 2,6 s (wie im Entwurf) und meldet das über den `Snapshot` (`SelectedInput`), das Overlay zieht die `RadioButton`-Auswahl nach; eine Nutzerwahl sperrt die Automatik 6 s. Bei Reduced Motion gibt es keine Automatik.

**Daten:** lesbare und schreibbare Formate je Art aus `FormatRegistry.All`; erreichbare Ziele je Eingangsformat über eine neue reine Methode `FormatRegistry.OutputsFor(FormatId input, ISystemCodecCapabilities? codecs)` (dieselbe Matrix wie `Suggest`, nur ohne `InputInfo`; kleine Engine-Ergänzung für `engine-entwickler`, mit Test „jede lesbare Id liefert eine nicht leere Liste oder null“). Empfehlung = erstes Element der Liste, das nicht das Eingangsformat ist (dieselbe Regel wie `Suggest.Default`; bei Video darf es dasselbe sein). Die Kurztexte je Format (`Format_<Id>_Hint`, z. B. „Handyfoto“) sind neue Ressourcen in DE und EN.

**Verdrahtung mit Schritt 2:** „Weiter: Ziel“ im Zoom nimmt die gezoomte Art mit: `IWorkflowSession.FocusKind` (neu, `MediaKind?`, `Reset` löscht es), `TargetPageViewModel` wählt beim Betreten diese Art vor, wenn sie unter den gestapelten Dateien ist. Nichts weiter: der Zoom ist eine Ansicht, keine Entscheidung; Format und Note fallen in Schritt 2 (ADR-020).

## Spielereien (Strudel, Urknall): nur Hooks

Strudel nach 4 s Zeigerstillstand auf einer Bahn und Urknall nach 10 s auf dem Loch sind ein **eigenes späteres Blatt** (`schritt-1-spielereien.md`). Diese Etappe legt nur den Messpunkt an: `GalaxyScene.Dwell` (`PointerDwell(Orbit, OnHole, Duration)`), gefüllt in `Update` aus der geglätteten Zeigerposition (Bahn per `OrbitAt` mit 40 px Toleranz, Loch bei Abstand < `HoleRadius + 12 px`); `Duration` läuft weiter, solange der Zeiger sich weniger als 6 px bewegt, und springt sonst auf 0. Sonst nichts: keine Strudel-Zustände, keine Planetentypen, keine Bebenlogik. Test: Verweildauer wächst mit `Update`, fällt bei Bewegung auf 0, ist bei Zeiger außerhalb `null`/`false`.

## Leistung

- Zielbild: 60 fps bei 1080p auf integrierter Grafik mit 20 Dateien; `TargetElapsedTime` 1/60 s. Bei Akkubetrieb (`PowerManager.EnergySaverStatus == On`, Windows App SDK) 1/30 s.
- Teilchenbudget (`GalaxyBudget`): 5 × 160 Staub = 800 fest; 34 je Datei bis 30 Dateien (1020), danach 12 je Datei; harte Grenze 4000 Teilchen insgesamt (≈ 270 Dateien mit Hof), darüber nur noch Planetenpunkte. `FillRectangle` je Teilchen ist bei 4000 Aufrufen erfahrungsgemäß unter 4 ms; erst wenn die Messung unter Windows das widerlegt, wird auf `CanvasSpriteBatch` mit einem zur Laufzeit erzeugten Weißquadrat umgestellt (siehe Renderer).
- Bahnlinien: Pfad je Bild neu (110 Punkte × 5), das ist billig; nicht cachen, weil Beule und Kamera ihn jedes Bild ändern.
- Pause: `Paused = true`, wenn `Window.Visible == false` (`AppWindow.Changed` / `Window.VisibilityChanged`), wenn eine andere Seite aktiv ist (`OnNavigatedFrom` → `Suspend()`, `OnNavigatedTo` → `Resume()`; `MainPage` ist `NavigationCacheMode=Required`, die Szene im ViewModel bleibt), wenn der Verlauf (`HistorySplitView`) offen ist (nur Pause, nicht Suspend). Bei Pause laufen `Update` und `Draw` nicht; das gespeicherte Bild bleibt stehen.
- Speicher: `Suspend()` → `RemoveFromVisualTree()` und Referenz auf null, damit der Zeichentakt nicht weiterläuft (ADR-018). Der Renderer gibt in `CreateResources` alte Pinsel frei (`Dispose`). `App.OnWindowClosed` ruft `GalaxyHost.Suspend()` vor dem Schließen.
- DPI: `DpiScale` auf höchstens 2,0; bei `DpiChanged` rechnet die Hülle nichts um, weil die Szene in DIP arbeitet.
- Messung: Debug-Ausgabe der Bildzeit (`args.Timing`) über `ILogger` auf Stufe Debug, nur bei `KVERTIS_GALAXY_STATS` (Debug-Builds).

## Barrierefreiheit

- Die Galaxie ist **dekorativ**: `GalaxyCanvas` und `GalaxyStaticView` tragen `AutomationProperties.AccessibilityView="Raw"` und sind nicht in der Tab-Reihenfolge (`IsTabStop=False`). Alle Informationen (Anzahl, Namen, Formate, Warnungen, Gründe) stehen in den Fächern und in der Karte „Nicht umwandelbar“.
- Tastaturweg: Einwurf-Zeile (Dateien wählen, Ordner wählen) → Fach 1 Kopf → Zeilen des Fachs → Fach 2 … → Nicht umwandelbar → „Weiter: Ziel“. Fachkopf: Enter/Leertaste öffnet den Zoom, Esc schließt ihn. Im Zoom: Übersicht-Knopf → linke Liste (Pfeiltasten wechseln das Format) → rechte Liste (nur lesbar, Fokus überspringbar) → Fachleiste.
- Fach: `AutomationProperties.Name` = „Bilder, 3 Dateien“ (`Tray_AutomationName` mit Zahl), Zahl mit `LiveSetting=Polite`, damit der Erzähler jede neue Datei ansagt („Bilder, 4 Dateien“); Zeile: bestehender `Card_AutomationName` („foto.heic, bereit“) plus Warnung. Karte „Nicht umwandelbar“: `LiveSetting=Assertive` beim ersten Eintrag.
- Zoom-Overlay: linke Liste als `RadioButton`-Gruppe mit `AutomationProperties.Name` „Format wählen“; rechte Einträge mit Zustand „empfohlen“ / „nicht erreichbar von {0}“ im `HelpText`.
- Hoher Kontrast: keine Galaxie (siehe `GalaxyHost`), Fächer mit sichtbaren Rändern (`SystemColorWindowTextColor`), Zahl nicht farbig; Formatschilder nur mit Rand.
- Mindestgröße Klickziele 32 px bleibt: der Fachkopf ist mindestens 36 px hoch, „Entfernen“ je Zeile `IconButtonSize`.

## Paket-Einbau (Teil von Teilaufgabe A)

1. `Directory.Packages.props`: `<PackageVersion Include="Microsoft.Graphics.Win2D" Version="1.4.0" />`. `src/Kvertis.App/Kvertis.App.csproj`: `<PackageReference Include="Microsoft.Graphics.Win2D" />`. Das Paket verlangt `Microsoft.WindowsAppSDK.WinUI 1.8.260204000`; ob das neben `Microsoft.WindowsAppSDK 2.5.1` auflöst, zeigt der erste Build unter Windows. Löst es nicht auf, ist die Version von Win2D (nicht die des App SDK) anzupassen und der Lizenz-Wächter für die neue Versionsnummer zu fragen; das Paket bleibt dasselbe.
2. `third_party/_geplant/Win2D/` → `third_party/Win2D/` verschieben (`LICENSE` = MIT, `MICROSOFT-SOFTWARE-LICENSE-TERMS.txt` = EULA). Die `Content Include` in der csproj nimmt den Ordner damit automatisch auf; `ThirdPartyLicensesProvider` liest **alle** Dateien eines Ordners und zeigt beide Texte unter einem Eintrag „Win2D“. Sichtprüfung auf der Third-Party-Licenses-Seite.
3. `docs/04-bibliotheken.md`: Status der Win2D-Zeile auf „im Code seit 2026-09-25“, Pfad im Prüfbericht auf `third_party/Win2D/` nachziehen.
4. `docs/CHANGELOG.md`: Eintrag macht der Hauptagent, nicht dieses Blatt.
5. `bash tools/compliance/check.sh` muss grün bleiben (Marken: „Microsoft“ nur als Paket- und Namensraumname).

## Reihenfolge der Teilaufgaben

### A · Paket und Szene-Modell mit Tests (ui-entwickler, mit tester)

- Paket-Einbau wie oben, ohne dass schon eine Zeichenfläche entsteht (nur damit der Build den Bezug kennt).
- `Scenes/`: `SceneColor`, `ScenePalette`, `GalaxyLayout`, `GalaxyBody`, `GalaxyParticle`, `GalaxyBudget`, `GalaxyCommand` und Ableitungen, `GalaxySnapshot`, `GalaxyMotion` (Kurven, Bézier, `ease`/`glatt`/`easeOut`/`easeBack`), `GalaxyScene`, `PointerDwell`. Alle Dateien in `Kvertis.App.Tests.csproj` per `Compile Include` verlinkt.
- `IWorkflowSession.FocusKind` (neu) und `FormatRegistry.OutputsFor` (Engine, mit engine-entwickler).
- Tests ohne WinUI (`tests/Kvertis.App.Tests/Scenes/`): Bahnradien und Ordnung (`OrbitOf`, `BaseRadius` bei 700×280 exakt 92..316); `OrbitPoint` ohne Hover liegt auf der Ellipse, mit Hover wölbt sich der Punkt unter dem Zeiger um höchstens 19 px; Anflug deterministisch: `AddBody` bei `Time = 0` → nach `Update(0,1 s)` `Waiting`, nach weiteren 0,375 s `Approaching` in der Mitte der Kurve (Position = `GalaxyMotion.Approach(…, 0,5)`), nach 0,75 s `Orbiting` mit Position auf der Bahn; zwei Körper sind um 0,16 s versetzt; `RejectBody` fliegt zum `RejectedAnchor`; Zeiger-Einfluss: ein Staubteilchen 50 px vom Zeiger rückt nach 1 s um weniger als 20 px zum Zeiger, eines 300 px entfernt bewegt sich nicht; `SetPointer(null)` lässt den Einfluss innerhalb von 2 s unter 0,01 fallen; Zoom: `ZoomTo(Image)` → nach 1,15 s `Zoomed`, `OrbitRadiusX[4] == ZoomRadius`, `OrbitAlpha` der anderen 0; `ZoomTo(null)` fährt zurück; erneutes `ZoomTo` mitten in der Fahrt springt nicht (Radius stetig, Differenz je 16 ms < 15 px); `Update` klemmt auf 100 ms; Budget: 40 Körper → Hof 12 je Körper, 4000 nie überschritten; `Dwell` wie oben; Determinismus: zwei Szenen mit gleichem Seed und gleichen Kommandos liefern nach 5 s identische Snapshots.

### B · Renderer, Canvas-Hülle und Ruhige-Ansicht-Weiche (ui-entwickler)

- `Rendering/GalaxyRenderer.cs`, `Rendering/GalaxyCanvas.xaml(.cs)`, `Rendering/ScenePaletteReader.cs` (liest `Kv*Color`), `Views/GalaxyStaticView.xaml`, `Views/GalaxyHost.xaml(.cs)` mit der Weiche über `IMotionSettings`.
- `MainViewModel` bekommt `GalaxyScene Scene` (erzeugt mit `Random.Shared`-Seed, Budget aus Konstante) und schickt bei `Initialize`/`SetRejected`/`Remove` die passenden Kommandos, `MainViewModel.Clear()` („Neue Runde“) schickt `Clear()`; die Szene existiert auch, wenn keine Canvas da ist (kostet nichts, `Update` wird dann nicht gerufen).
- `MainPage`: `GalaxyHost` oben in einer neuen Zeile über der bisherigen Job-Liste (die Fächer kommen in C). Ablage auf die ganze Seite; `DragEnter`/`DragLeave` der Seite senden `SetDragOver`.
- Abnahme: Sichtprüfung unter Windows in Hell und Dunkel; Umschalten von „Animationen reduzieren“ zur Laufzeit wechselt ohne Neustart; Hoher Kontrast blendet die Galaxie aus; Fenster minimieren pausiert (Prozessor-Last im Task-Manager sinkt); Wechsel zu Einstellungen und zurück erzeugt die Canvas neu und die Planeten stehen noch; keine `CanvasBitmap`-Verwendung in `Rendering/` (Suche).

### C · Fächer in XAML und Umbau der `MainPage` (ui-entwickler, mit tester)

- `ViewModels/Drop/TrayViewModel.cs` (Art, Name, Farbschlüssel, `Files` als gefilterte Sicht auf `MainViewModel.Jobs`, `Count`, `ReadableCount`, `WritableCount`, `ExampleFormats`, `IsZoomed`, `AutomationName`), `ViewModels/Drop/RejectedListViewModel.cs`; `MainViewModel.Trays` (fünf, feste Reihenfolge), `MainViewModel.Rejected`, `MainViewModel.ZoomKind`. `TrayViewModel` ist WinUI-frei und wird getestet (Zählung, Filter, Beispiel-Formate, Zähltexte).
- `Views/TrayControl.xaml`, `Views/TrayFileControl.xaml` (ersetzt `JobCardControl`), Einwurf-Zeile und Karte „Nicht umwandelbar“ in `GalaxyHost`; `MainPage` ohne `DropZone` und `JobList`; `JobCardControl.xaml(.cs)` löschen; `AdaptiveTrigger` für schmale Fenster.
- Fach-Animation (Composition, nicht Win2D, respektiert `IMotionSettings` wie `CardAnimations`): neue Zeile blendet 250 ms von unten ein, Zahl hüpft 0,45 s; bei Reduced Motion nur Einblenden.
- Ressourcen `Tray_*`, `Kind_*`, `Galaxy_*` in DE und EN; frei werdende Schlüssel `Main_DropZone_More.*`, `Main_AddMoreFiles_Button.*`, `Main_AddMoreFolder_Link.*`, `Main_JobList.*` löschen.
- Abnahme: Durchlauf mit acht Dateien (Bild, Audio, Video, Dokument, 3D, Archiv, falsche Endung, Zwischenablage-Bild): jede landet im richtigen Fach bzw. rechts oben, die Zahl stimmt, Entf entfernt, „Weiter: Ziel“ nur mit mindestens einer erkannten Datei; Erzähler liest Fach, Zahl und Zeilen; `check.sh` grün.

### D · Zoom „Wege durchs Loch“ und Verdrahtung mit Schritt 2 (ui-entwickler, reviewer)

- `ViewModels/Drop/PathsViewModel.cs` (WinUI-frei, getestet: lesbare/schreibbare Listen je Art, erreichbare Ziele je Eingang, Empfehlung, Automatik-Sperre über `TimeProvider`), `Views/PathsOverlay.xaml`, `VisualState Zoomed` in `TrayControl`, Anker-Meldung an die Szene, Zurück (Knopf, Esc, Fachkopf, Loch).
- `IWorkflowSession.FocusKind` setzen; `TargetPageViewModel` liest es beim Betreten.
- Abnahme: Klick auf Bahn und Fachkopf zoomen; Formatwahl links zeigt die Wege, Empfehlung in Mint; Zurück auf allen vier Wegen; Einwurf im Zoom fährt zurück; Tastaturweg vollständig; bei Reduced Motion erscheinen die Listen ohne Fahrt; `check.sh` grün.

## Ressourcen-Schlüssel (Muster)

`Galaxy_Entry_Title.Text` („Dateien hierher ziehen oder auswählen“), `Galaxy_Entry_PasteHint.Text`, `Galaxy_Back_Button.Content`, `Galaxy_Recommended_Tag.Text`, `Galaxy_Opens_Title.Text` („Kvertis öffnet“), `Galaxy_Becomes_Title.Text` („kann werden zu“), `Galaxy_NotReachable_Text` („nicht von {0}“), `Galaxy_Rejected_Title.Text`, `Galaxy_Rejected_More_Text` („und {0} weitere“); `Tray_AutomationName` („{0}, {1} Dateien“), `Tray_Empty_Text` („noch leer, zum Beispiel“), `Tray_Foot_Text` („öffnet {0} → macht {1} Formate“), `Tray_ExtensionMismatch_Text` („Endung .{0}, Inhalt ist {1}“), `Tray_Remove_Button.*`; `Kind_Image_Name`, `Kind_Image_Sub` usw. für alle fünf Arten; `Format_<Id>_Hint` je Format der Registry. Bestehende `Main_AddFiles_Button`, `Main_AddFolder_Link`, `Main_Trust_Text`, `Main_GoTarget_Button`, `Card_*` bleiben.

## Nicht in diesem Blatt

Übergangs-Überlagerung (Wurmloch nach Schritt 2, Blätter nach Schritt 3), Strudel und Urknall, Tooltips über Planeten, Pixelwirbel und weißes Loch in Schritt 3, Universum-Symbole und Bahn in Schritt 2, `ThemeShadow` an Fächern.

## Offene Fragen an den Projektinhaber

Keine, die die Umsetzung blockieren. Ob die Win2D-Abhängigkeit `Microsoft.WindowsAppSDK.WinUI 1.8.x` neben `Microsoft.WindowsAppSDK 2.5.1` auflöst, entscheidet der erste Build (siehe Paket-Einbau); der Zoom-Radius und die Teilchengrößen werden nach der ersten Sichtprüfung unter Windows angepasst.
