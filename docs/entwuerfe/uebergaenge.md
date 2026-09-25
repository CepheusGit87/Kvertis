# Arbeitsblatt „Übergänge, Pixelwirbel, weißes Loch und Abschluss“ (Zeichenschicht mit Win2D) für den UI-Entwickler

Stand 2026-09-25. Verbindliche Grundlage: `docs/03-architektur.md`, ADR-018, ADR-021, ADR-022 und ADR-023 (Überlagerung, Geister statt Kopien, Pixelraster ohne Bild, eine Zeichenschleife zur Zeit). Dieses Blatt ist die Kurzfassung zum Abarbeiten; bei Widerspruch gilt `03-architektur.md`. Entwurf: `design/ENTSCHEIDUNGEN.md` (Zeilen „Übergänge und Abschluss“, „Schritt 3“, „Ziel in Schritt 3“, „Schritt 2“), `design/oberflaeche-mischentwurf.html` (Überlagerung `UEB` ab Zeile 5102 mit `WEG.wurmloch`, `WEG.andocken` und dem Blätter-Zweig in `wechsel`; Wirbel `S5`/`s5Zeichnen` ab 4465; weißes Loch und Abschluss `WL` ab 4523 mit `PH1`, `PH2`, `M`, `STILL`, `MAXW`, `nova`, `explosion`, `beben`, `haken`), `design/uebergaenge-konzepte.html` (Konzept 3, Abschluss aus Konzept 2; der Mischentwurf hat beides bereits eingebaut und ist die Zahlenquelle), `design/wirbel-varianten.html` (Variante 5, dieselbe Zeichenlogik wie `S5`), `design/weisses-loch-einfach.html` und `design/weisses-loch-mengen.html` (Entwurf 1 „Gebündelte Bahnen“, dieselben Zahlen wie `WL`).

Vorgänger dieses Blatts: `schritt-1-galaxie.md` (Szene-Modell, Renderer, Hülle, Budget, Thread-Regel) und `schritt-3-umwandeln.md` (Seite, Koordinator, Bericht). Beides bleibt gültig; dieses Blatt ergänzt die Zeichenschicht. Auflage des Lizenz-Wächters gilt weiter: **keine Win2D-Bildladefunktionen für Nutzerdateien**, keine `CanvasBitmap` aus Dateien oder aus `RenderTargetBitmap`. Alle Farben nur aus `Themes/KvertisColors.xaml`.

## Was gebaut wird

| Teil | Wo | Was |
|---|---|---|
| Übergangs-Überlagerung | `MainWindow.xaml`: `Rendering/TransitionOverlay` (UserControl mit eigenem `CanvasAnimatedControl`) in `RootGrid` über `Grid.Row=1` und `Grid.RowSpan=2` (Schrittleiste und `ContentFrame`), `Visibility=Collapsed` außer während eines Übergangs, `IsHitTestVisible=True` während des Übergangs (schluckt Zeigereingaben) | Wurmloch 1→2, Bogen zurück 2→1, Blätter 2→3. Zeichnet **Geister** (stilisierte Ersatzbilder der Fächer, Universum-Symbole und Blätter), Ringe und das schwarze Loch. Die Seiten bleiben unverändert und melden nur Ankerpunkte |
| Anker | `Services/ITransitionAnchors.cs`, umgesetzt von `MainPage`, `TargetPage`, `ConvertPage` | Die Seite misst per `TransformToVisual(overlay)` die Mitten und Rechtecke ihrer Ziele (Fächer, Arten links, Eingang, Wirbelmitte, Loch) und blendet die echten Elemente während des Flugs aus |
| Ablauf | `Services/TransitionService.cs` (`ITransitionService`), eingehängt in `StepNavigationService.GoTo` | Entscheidet Überlagerung oder schlichter Seitenwechsel, misst, navigiert sofort, startet die Szene, sperrt Eingaben, räumt auf, bricht bei Größenänderung ab |
| Wirbel und weißes Loch | `Views/ConvertPage.xaml`: `SwirlHost` wird `Views/SwirlHost.xaml` (Host wie `GalaxyHost`) mit `Rendering/SwirlCanvas` | Pixelwirbel in der Mitte (zwei Dateien zugleich), Eingangsstapel als gezeichnete Blätter links im Canvas, rechts oben im selben Canvas das weiße Loch mit Bahnen und Planeten; die XAML-Karten (Fortschritt, Bericht) bleiben darüber |
| Abschluss | dieselbe Fläche, `FinaleScene` | Anlauf, Tanz, Verschmelzung, Stille, Supernova, Beben (Seite als XAML-Translation), Ring mit Häkchen; der Bericht ist die bestehende XAML-Karte und wird zeitlich eingeblendet |

Wichtig für die Ordnung der Arten: Im Mischentwurf stehen die Arten in Schritt 1 und 2 in **derselben** Reihenfolge (Bilder, Audio, Video, Dokumente, 3D); `ENTSCHEIDUNGEN.md` nennt 3D und Dokumente vertauscht, und im Code sind Fächer (`GalaxyLayout.OrbitOrder` rückwärts) und Artenliste (`TargetPlanner.KindOrder`) heute gleich (Bilder, Audio, Video, 3D, Dokumente). Die Überlagerung kennt keine Reihenfolge: jeder Geist fliegt vom gemessenen Quell-Anker seiner Art zum gemessenen Ziel-Anker derselben Art. Ändert eine Seite ihre Reihenfolge, ändern sich nur die Flugbahnen, nicht der Code.

## Ankerpunkte (`ITransitionAnchors`)

```csharp
namespace Kvertis.App.Services;

public enum TransitionAnchorKind { Tray, KindSymbol, Inbox, SwirlCentre, Hole, Bag }

/// <summary>A rectangle in overlay coordinates (DIP), measured by a page. Kind is null for anchors that are not per media kind.</summary>
public sealed record TransitionAnchor(TransitionAnchorKind Kind, MediaKind? MediaKind, Vector2 Centre, Vector2 Size, float HoleRadius = 0);

public sealed record TransitionAnchorSet(WorkflowStep Step, IReadOnlyList<TransitionAnchor> Anchors)
{
    public TransitionAnchor? Find(TransitionAnchorKind kind, MediaKind? mediaKind = null);
}

/// <summary>Implemented by the three step pages. Called on the UI thread only.</summary>
public interface ITransitionAnchors
{
    /// <summary>Measures the current layout relative to <paramref name="reference"/>; null when the page is not laid out yet.</summary>
    TransitionAnchorSet? MeasureAnchors(UIElement reference);

    /// <summary>Hides (false) or shows (true) the elements the overlay is drawing stand-ins for. Must be cheap and idempotent.</summary>
    void SetFlightVisibility(bool visible, IReadOnlySet<MediaKind> kinds);
}
```

| Seite | Anker | Quelle im XAML | Ausblenden während des Flugs |
|---|---|---|---|
| `MainPage` (Schritt 1) | `Tray` je vorhandener Art (Mitte und Größe des Fachkopfs, nicht des ganzen Fachs), `Hole` (Mitte der Galaxie: `scene.Layout.Center` über `GalaxyCanvas` transformiert, `HoleRadius = scene.HoleRadius`) | `Tray0..Tray4` (`TrayControl.Header`), `GalaxyHost` | Fachköpfe der beteiligten Arten `Opacity=0`; die Galaxie wird pausiert, das Loch zeichnet die Überlagerung weiter |
| `TargetPage` (Schritt 2) | `KindSymbol` je Art in der Artenliste (Mitte des Listeneintrags; später das Universum-Symbol), `Hole` (Mitte des mittleren Bereichs `MiddlePanel`, Radius 12; sobald die Zeichenschicht von Schritt 2 existiert, deren Lochmitte) | `Target_Kinds_List` (`ListViewItem` je `KindGroupViewModel`), `MiddlePanel` | Listeneinträge der beteiligten Arten `Opacity=0` |
| `ConvertPage` (Schritt 3) | `Inbox` (Mitte der Eingangskarte `LeftPanel`), `SwirlCentre` (Mitte von `SwirlHost`), `Bag` (Tasche-Knopf) | `LeftPanel`, `SwirlHost`, Tasche | nichts; die Seite blendet als Ganzes ein |

Messung: `TransformToVisual(overlay).TransformBounds(new Rect(0,0,ActualWidth,ActualHeight))`. Anker, die es nicht gibt (leere Art, Fenster zu klein, Element nicht geladen), fehlen einfach; die Szene lässt die betroffenen Geister aus. Ohne mindestens einen passenden Quell- und Ziel-Anker gibt es keinen Übergang, nur den schlichten Wechsel.

## Ablauf eines Übergangs (`TransitionService`)

1. `StepNavigationService.GoTo(step)` ruft `ITransitionService.TryBegin(from, to)`. Schlichter Wechsel (wie heute, `DrillIn` bzw. `Suppress`) wenn: `IMotionSettings.ReducedMotion` (Animationen aus oder Hoher Kontrast), das Paar nicht 1→2, 2→1 oder 2→3 ist, die Quell-Seite keine Anker liefert, keine Datei gestapelt ist, der Verlauf offen ist, oder gerade ein Übergang läuft (der laufende wird zuerst mit `Finish()` sofort beendet, wie `sofortFertig` im Entwurf).
2. Quelle messen (`MeasureAnchors` der aktuellen Seite), `GalaxyHost.SetPaused(true)` (Schritt 1) bzw. `SwirlHost.SetPaused(true)`, Überlagerung sichtbar, `IsHitTestVisible=True`, `IsTransitioning=true` (Schrittleiste und Seiten prüfen das in `CanExecute` ihrer Navigationsbefehle; Tastenkürzel Strg+O/Strg+V/Entf bleiben wirkungslos, weil `ContentFrame.IsHitTestVisible=false` gesetzt wird und die Befehle `IsTransitioning` prüfen).
3. **Sofort navigieren** (`FrameNavigationService.Navigate(page, keepBackStack:false, transition: Suppress)`), die neue Seite bekommt `Opacity=0` auf ihrem `PageRoot` und `SetFlightVisibility(false, kinds)`. Nach `Loaded` plus einem `LayoutUpdated` (höchstens zwei Bilder warten, sonst Abbruch → schlichter Wechsel mit `Opacity=1`) Ziel messen.
4. `TransitionScene` mit beiden Ankersätzen und Palette bauen, per `Enqueue(new Begin(...))` an die Hülle geben; die Hülle läuft, bis `scene.IsFinished`. Parallel dazu blendet die neue Seite ihre `Opacity` von 0 auf 1 (Composition, 320 ms, Start bei `Duration − 260 ms`); die alte Seite ist nicht mehr im Baum, ihre sichtbaren Teile sind die Geister.
5. Ende (`IsFinished` oder `Finish()`): `SetFlightVisibility(true, …)` auf der neuen Seite, `Opacity=1`, Überlagerung `Paused`, `Visibility=Collapsed`, `IsHitTestVisible=False`, `IsTransitioning=false`, `SwirlHost`/`GalaxyHost` `SetPaused(false)`. Das Loch der Überlagerung blendet 280 ms aus, danach gilt das Loch der Seite (Schritt 3: dasselbe Loch an derselben Stelle, Radius 12).
6. **Abbruch:** `RootGrid.SizeChanged`, `Window.VisibilityChanged` (unsichtbar), Themenwechsel, `IMotionSettings.Changed` → `Finish()` sofort (Endzustand ohne Rest-Animation). Der Nutzer kann nicht abbrechen; die längste Sequenz ist unter 2 s.

Empfehlung zur Frage „abwarten oder parallel“: **parallel starten** (Schritt 3 oben). Grund: Die Ziel-Anker sind erst messbar, wenn die neue Seite im Baum liegt; ein Abwarten vor der Navigation müsste die Zielpositionen raten. Die Seiten sind `NavigationCacheMode=Required`, der Wechsel kostet nichts Sichtbares, und die neue Seite bleibt bis zum Ende des Flugs unsichtbar. `FrameNavigationService` bekommt dafür nur einen optionalen Parameter für die Übergangsart (`Suppress` erzwingen); `INavigationService.Navigated` feuert wie heute sofort, damit `StepHeader` den neuen Schritt zeigt.

## Übergang 1→2 „Wurmloch“ (Fächer → Arten links)

Je Art `i` (Reihenfolge der Fächer von links, nur Arten mit Dateien) ein Paar aus Geist A (Fachkopf: Karte `KvPanel` mit Rand `KvLine`, Farbpunkt, Artname, Zahl in Artfarbe) und Geist B (Artsymbol: Kreis mit Ring in Artfarbe, 24 px, Artname daneben). Stationen mit Deckkraft A/B; zwischen zwei Stationen werden Position, Breite, Streckung und Drehung linear interpoliert, die Zeit mit der Kurve der **vorderen** Station. `S` = Quell-Anker, `T` = Ziel-Anker, `c` = Loch-Anker aus Schritt 1.

| Station (Anteil der Dauer) | Position | Breite | Streckung sx/sy | Drehung | Alpha A / B | Kurve bis zur nächsten Station |
|---|---|---|---|---|---|---|
| 0 | `S` | `S.w` | 1/1 | 0 | 1 / 0 | `cubicBezier(.6, 0, .9, .5)` |
| 0,30 | `lerp(S, c, 0,62)` | `0,4·S.w` | 0,55 / 1,5 | −0,5 rad | 1 / 0 | `cubicBezier(.6, 0, .9, .5)` |
| 0,42 | `c` | 6 px | 0,3 / 2 | −1,2 rad | 0 / 0 | linear |
| 0,56 | `T` | 6 px | 1/1 | 0 | 0 / 0 | `cubicBezier(.3, 1.5, .5, 1)` (Überschwinger) |
| 0,82 | `T` | `1,06·T.w` | 1/1 | 0 | 0 / 1 | `easeOut` (CSS `ease-out` = `cubicBezier(0, 0, .58, 1)`) |
| 1,00 | `T` | `T.w` | 1/1 | 0 | 0 / 1 | – |

| Größe | Wert | Quelle |
|---|---|---|
| Versatz je Art | 0,12 s · `i` | `d: i*120` |
| Dauer je Art | 1,40 s | `dur: 1400` |
| Gesamtdauer | `(n−1)·0,12 + 1,40` → 1,88 s bei fünf Arten | `fertigZeit` |
| Ring im Loch | Start bei Anteil 0,42, 0,65 s, Ellipse (Abplattung 0,36) in Artfarbe, Radius 14 → 44 mit `easeOut`, Alpha `0,9·(1−e)`, Breite `2·(1−0,6e)` | `ringe[0]`, `ringAuf` |
| Ring am Ziel | Start bei Anteil 0,56, an `(T.x, T.y − 8)`, Mint, Radius 2 → 40, sonst gleich | `ringe[1]` |
| Loch-Fahrt | Start `0,55·Gesamt`, Dauer `0,40·Gesamt`, gerade Strecke von `c` (Radius aus Schritt 1, 18·Scale) zum Loch-Anker von Schritt 2 (Radius 12), Kurve `ease` | `lochPlan` |
| Neue Seite einblenden | Start `Gesamt − 0,26 s`, 0,32 s | `neuAnim` |
| Aufräumen | `Gesamt + 0,08 s`; Loch danach 0,28 s aus | `aufraeumen`, `L.aus` |

## Übergang 2→1 „Bogen“ (Arten links → Fächer)

Geist A = Artsymbol, Geist B = Fachkopf. Drei Stationen; der Scheitel liegt je Art auf anderer Höhe, damit sich die Bahnen nicht treffen.

| Station | Position | Breite | Alpha A / B | Kurve bis zur nächsten |
|---|---|---|---|---|
| 0 | `S` | `S.w` | 1 / 0 | `cubicBezier(.45, .05, .3, 1)` |
| 0,5 | `x = lerp(S.x, T.x, 0,5) − 30`, `y = min(S.y, T.y) + 0,35·|S.y − T.y| − 40 − 14·i` | `lerp(S.w, T.w, 0,6)` | 0,45 / 0,55 | `cubicBezier(.3, .1, .25, 1)` |
| 1 | `T` | `T.w` | 0 / 1 | – |

Versatz 0,07 s · `i`, Dauer 0,82 s, Gesamt `(n−1)·0,07 + 0,82` = 1,10 s bei fünf Arten. Loch: Start 0, Dauer `0,9·Gesamt`, gerade Strecke vom Loch-Anker in Schritt 2 (Radius 12) zur Galaxiemitte (Radius `18·Scale`). Keine Ringe. Neue Seite ab `Gesamt − 0,26 s`.

## Übergang 2→3 „Blätter“ (Arten bleiben, Loch gibt Blätter an den Eingang, fährt in den Wirbel)

Die Artsymbole fliegen nicht; ihr Geist pulst am Platz (Versatz 0,06 s · `i`, 1,1 s: Größe 1 → 1,04 bei Anteil 0,35 → 0,9, Alpha 1 → 1 → 0). Je Datei `j` des Plans (Reihenfolge `Plan.Items`) ein **Blatt**-Geist (`SheetGhost`, gezeichnete Attrappe, siehe „Blatt“ unten), das aus dem Loch `a` auftaucht und auf den Eingangsstapel `T_j` fliegt.

| Station | Position | Breite | Drehung | Alpha | Kurve bis zur nächsten |
|---|---|---|---|---|---|
| 0 | `a` (Loch Schritt 2) | 10 px | 0 | 0 | `easeOut` |
| 0,18 | `lerp(a, m, 0,25)` | 26 px | 0 | 1 | `cubicBezier(.3, 0, .3, 1)` |
| 0,55 | Scheitel `m = (lerp(a.x, b.x, 0,5) + 20, min(a.y, b.y) − 50 − 6·j)` | `lerp(26, T.w, 0,7)` | 0,15 rad | 1 | `cubicBezier(.3, .1, .25, 1)` |
| 1 | `b = Mitte(T_j)` | `T.w` = 48 px | `rot_j` | 1 | – |

| Größe | Wert | Quelle |
|---|---|---|
| Stapelplatz `T_j` | im Canvas von Schritt 3: `x = 40 + 4·j'`, `y = cy − 70 − 5·j'`, Größe 48 × 62 (Blatt 96 × 124 bei Maßstab 0,5), `rot_j = ((j' mod 3) − 1)·0,04 rad`, `j' = n − 1 − j`; in Overlay-Koordinaten über den Anker `SwirlCentre` (der Wirbel-Canvas liegt links vom Anker: `x_overlay = SwirlHost.Left + 40 + 4·j'`) | `quelle5` |
| Versatz je Blatt | `0,15 + j·Δ` mit `Δ = min(0,09 s, 0,90 s / max(1, n−1))`, damit die Gesamtdauer 2,0 s nie übersteigt (Entwurf: fest 0,09 s ohne Deckel) | `d = 150 + j*90` |
| Dauer je Blatt | 0,95 s | `dur = 950` |
| Deckel | höchstens 24 Blätter fliegen (die ersten 24 des Plans); weitere liegen beim Einblenden schon auf dem Stapel | eigene Festlegung, im Entwurf kein Deckel |
| Gesamtdauer | `0,15 + (min(n,24)−1)·Δ + 0,95`, also 1,10 s (eine Datei) bis 2,0 s | `fertigZeit` |
| Loch-Fahrt | Start 0, Dauer `0,85·Gesamt`, vom Loch-Anker Schritt 2 zum `SwirlCentre`, Radius → 12, **Bogen** nach oben: `y −= sin(π·k)·30 px`, Kurve `ease` | `bogen: 30` |
| Auflösen im Wirbel | Im Entwurf blendet das Loch nur 0,28 s aus; die Wirbelfläche zeichnet ab dann ihr eigenes Loch an derselben Stelle. Zusätzlich (eigene Festlegung, klein): in den letzten 0,28 s streut die Überlagerung 40 Funken (Quadrate 2 px, Mint und Violett, Radius 12 → 40 mit `easeOut`, Alpha `0,85·(1 − e)`), wie `Z.funken` im Entwurf angelegt, dort aber nie befüllt | `Z.funken` |
| Neue Seite einblenden | `Gesamt − 0,26 s`, 0,32 s | `neuAnim` |

Übergänge, die es nicht gibt: 3→2 (nach dem Lauf „Zurück“ oder Klick auf Schritt 2) und alles von oder zu Einstellungen, Verlauf, Pro sind schlichte Seitenwechsel wie heute. 1→3 kommt nicht vor (Schrittleiste sperrt Schritt 3 ohne Plan).

## Geister und Blatt (gezeichnete Ersatzbilder)

Keine Kopien der XAML-Elemente (ADR-023): Der Renderer zeichnet aus wenigen Primitiven, die Szene liefert nur Geometrie und Kennung.

| Geist | Zeichnung (Win2D, alles Primitive) |
|---|---|
| `TrayHeader` | abgerundetes Rechteck (Radius 8) `KvPanel` mit Rand `KvLine` 1 px; links Farbpunkt 10 px in Artfarbe; Artname (`Kind_<Kind>_Name`, `CanvasTextFormat` 12 pt 600) in `KvInk`; rechts Zahl 18 pt 700 in Artfarbe. Texte kommen fertig aus dem ViewModel in die Szene (wie `FormatLabel` in der Galaxie) |
| `KindSymbol` | Kreis 24 px in Artfarbe (Alpha 0,9), Ring `r + 4` Breite 1,5 Alpha 0,55, Artname rechts daneben 12 pt in `KvInk` |
| `Sheet` (Blatt 96 × 124) | Reiter: abgerundetes Rechteck (6, 0, 42 × 18, Radius 5) in Artfarbe mit Formatkürzel 9 pt 700 in `#0B0F12`; Papier: (0, 12, 96 × 112, Radius 6) in `KvPaper`; Inhaltsfeld (8, 22, 80 × 52, Radius 3) je Art (siehe `SheetRaster`); Formatkürzel 17 pt 700 bei (8, 102) in `#3B4247`; Dateiname 7 pt bei (8, 115) in `KvMuted`, gekürzt mit „…“; Schatten als `KvShadow`-Rechteck versetzt (0, 6) mit Alpha 0,35 statt `drop-shadow` |

## Pixelwirbel (`SwirlScene`)

**Pixelquelle, Entscheidung:** Das Vorschaubild der Datei darf nicht in Win2D geladen werden, und im Entwurf wird es auch nicht benutzt: `probe()` tastet ein **gezeichnetes Blatt** (`blattBild`) ab, kein Foto. Der Port erzeugt deshalb das Raster **rechnerisch** in `Scenes/SheetRaster.cs` aus Dateiart und Formatkürzel: 24 × 31 Zellen (Blatt 96 × 124, Schritt 4 px), jede Zelle mit Farbe `C0` (altes Blatt) und `C1` (neues Blatt: Reiter und Rahmen Mint) nach Zonen. Das ist deterministisch, testbar, ohne Bitmap, ohne SkiaSharp im App-Projekt und ohne Abhängigkeit vom Vorschaubild. Ein SkiaSharp-Farbfeld je Datei wäre ein zweiter Weg zum selben Ergebnis mit einer weiteren Bilddekodierung im UI-Thread; er lohnt nicht, weil die Pixel ohnehin zu 95 % die Artfarbe annehmen (`f = 0,95`) und die Raster-Struktur nur als Textur wirkt.

| Zone im Blatt (Zellen `gx`, `gy`) | `C0` | `C1` |
|---|---|---|
| Reiter: `gy ≤ 3` und `1 ≤ gx ≤ 11` | Artfarbe | Mint |
| außerhalb Reiter, `gy ≤ 2` | leer (Zelle entfällt) | leer |
| Rahmen des neuen Blatts: Rand des Papiers, 1 Zelle breit | `KvPaper` | Mint |
| Inhaltsfeld `5 ≤ gy ≤ 18`, `2 ≤ gx ≤ 21` | je Art: Bilder Verlauf `#9CC3EE` → `#E6EEF6` (oben 62 %) → `#D9C9A4` mit Bergzug `#5F7C9C` unten und Sonne `#F2C46A`; Audio `#F1ECFB` mit Balken `#7B61C4` (20 Balken, Höhen 8…44 · 0,9); Video `#1D2226` mit Dreieck `#F2B45A`; Dokumente `#FFFFFF` mit Titelbalken in Artfarbe und sechs Zeilen `#C5CCD1`; 3D `#F7EEF4` mit Sechseck `#9A2F7D` | gleich, nur Dokument-Titelbalken Mint |
| Text unten `gy ≥ 24` | `KvPaper` mit Textzellen `#3B4247` (Kürzel) und `KvMuted` (Name) nach festem Muster | gleich |
| sonst | `KvPaper` | `KvPaper` |

Die Zonenfarben sind die festen Hex-Werte des Entwurfs (Zeilen 4381–4403) und werden als Konstanten in `SheetRaster` abgelegt; sie sind Inhalt der Attrappe, keine UI-Tokens. Je Zelle zusätzlich `R1, R2, R3` aus dem injizierten `Random`. Ergebnis: höchstens 744 Pixel je Datei, real etwa 700.

| Größe | Wert | Quelle |
|---|---|---|
| Fläche | `SwirlHost`, Mindesthöhe 220; Mitte `(W/2, H/2)`; `R = 100`, Abplattung `fl = 0,42`; DIP, kein `Scale` (der Entwurf skaliert nicht) | `s5Groesse` |
| Dateien im Wirbel | höchstens 2 (`ConvertPageViewModel.SwirlSlots`), die frühesten laufenden; weitere laufende Dateien erscheinen nur in der XAML-Zeile „und n weitere laufen“ und landen ohne Pixelstrom (nur Ankunftsring) auf dem weißen Loch | `aktivS.size < 2` |
| Einflug | Start `tIn` = Zeitpunkt, an dem die Datei einen Wirbelplatz bekommt; Dauer 0,95 s, `ein = clamp((t − tIn)/0,95)`, Position `lerp(Stapelzelle, Wirbelposition, ease(ein))`; die Blatt-Attrappe auf dem Stapel verschwindet bei `ein > 0` | `tIn`, `ein` |
| Wirbelposition | `w = R1·2π + (t − tIn)·(0,5 + 1,6·(1 − R2))`, `rad = 14 + R2·R`, `x = cx + cos(w)·rad`, `y = cy + sin(w)·rad·fl + (R3 − 0,5)·12` | Zeile 4502–4503 |
| Drehgeschwindigkeit | 0,5 … 2,1 rad/s je Pixel, innen schneller → 0,48° … 2,0° je Bild bei 60 fps (Deckel 16° eingehalten) | dito |
| Farbe im Wirbel | `k = 0,7·Fortschritt` (Mischung `C0 → C1`), dann `f = 0,95·ease(ein)` zur Artfarbe; Alpha `max(0,35, lerp(A0, A1, k))` | `pixel`, `k: it.p*.7` |
| Fortschritt | aus dem Job: `ConversionProgress.Fraction` (0..1), `SetProgress(id, fraction)` bei `JobChangeKind.Progress`; **keine** eigene Dauerformel wie `dauerD` im Entwurf | Koordinator |
| Ausflug | `tOut` = Zeitpunkt `Completed`/`Failed`/`Cancelled`; `aus = clamp((t − tOut)/0,95)`, Position `lerp(Wirbelposition, Zielzelle, ease(aus))`, `k = ease(aus)`, `f = 0,95` | `aus` |
| Ziel je Ausgang | `Completed` ohne eigenes Ziel → Platz am weißen Loch (`p.x − 2, p.y − 2,5`, Maßstab 0,04: alle Pixel fallen auf einen Punkt); mit eigenem Ziel (`OwnLocations`) → Tasche-Anker, Maßstab 0,06; `Failed`/`Cancelled` → zurück auf den Stapelplatz (Maßstab 0,5), das Blatt liegt danach wieder als Attrappe im Stapel | `ziel5` |
| Pixelgröße | 2 × 2 DIP, `FillRectangle` | `size 2` |
| Spur | Linie von der Position des letzten Bilds, nur wenn `6 < d² < 4000`, Alpha `0,35·A`, Breite 1,6 | Zeile 4453 |
| Mischmodus | Dunkel: `CanvasBlend.Add` nur für Pixel mit `f ≤ 0,3`, sonst `SourceOver`; Hell: immer `SourceOver` | Zeile 4450 |
| Hof | Radius `R + 60`, Farbe `lerp(Violett, Mint, mittlerer Fortschritt)`, Alpha 0,20 während des Laufs, sonst `0,07 + 0,02·sin(2t)` | Zeile 4487 |
| Bahn-Ellipse | Radius `R + 24`, `ry = (R + 24)·fl`, dreht 0,05 rad/s, gestrichelt 2/6 (`CanvasStrokeStyle.CustomDashStyle`), `KvLineStrong` Alpha 0,6, Breite 1 | Zeile 4489 |
| Schwarzes Loch | Mitte, `r = 12`: Höfe Violett `3,4r` Alpha 0,22 und Mint `2r` Alpha 0,25; Scheibe `#000000` dunkel / `#0B0F12` hell; Mint-Ring bei `r + 1,2`, Breite 1,6, Alpha 0,95 (dasselbe Loch wie in Schritt 2 und in der Überlagerung) | `s5Loch` |
| Eingangsstapel | Blatt-Attrappen aller wartenden Dateien an `quelle5(j, n)`, oben liegt die nächste; höchstens 24 gezeichnet, darunter nur ein Zähler in XAML (der bestehende Eingang) | `bildAt(d.alt, q)` |
| Ruhe vor dem Start | nur Hof (Ruhe-Alpha), Bahn-Ellipse, Loch und Stapel | `!lauf` |

## Weißes Loch (`WhiteHoleScene`, Teil der Wirbelfläche rechts oben)

| Größe | Wert | Quelle |
|---|---|---|
| Breite `tw` | `min(300, 0,3·W)`; Mitte `T = (W − 22 − tw/2, 100)` | `G()` |
| `N` | Dateien des Plans ohne eigenes Ziel (die fliegen in die Tasche) | `haupt` |
| Bahnen `B` | `clamp(N, 1, 10)`; Plätze je Bahn `kap(j) = ⌊N/B⌋ + (j < N mod B ? 1 : 0)` | `B()`, `kap` |
| Platz `k` → Bahn, Index | `j = k mod B`, `p = ⌊k/B⌋`; Plätze werden in der Reihenfolge des Fertigwerdens vergeben (`Completed` und `Failed`, nicht `Cancelled`) | `platzFuer` |
| Bahnradius | `rx(j) = 24 + j·min(12, (tw/2 − 30)/max(1, B − 1))`, `ry = rx·0,42` | `rx` |
| Winkel | `w(k, t) = 2,39·j + p/kap(j)·2π + t·0,9·(24/rx(j))^1,5` → innen 0,9 rad/s (0,86° je Bild), außen langsamer | `winkel` |
| Bahnlinie leer | gestrichelt 2/5, `KvLineStrong` Alpha 0,75, Breite 1 | `bahnen` |
| Bahnlinie belegt | Breite 1,2; `N ≤ 10`: Artfarbe des Planeten Alpha 0,38; `N > 10`: Mint Alpha `0,12 + 0,25·belegt/kap(j)` | dito |
| Leere Plätze (`N > 10`) | Punkt `r = 1,4` in `KvLineStrong` Alpha 0,95, kreist mit; höchstens 150 gezeichnet | Zeile 4623 |
| Planet | Radius `gP = 2,8` (`N ≤ 10`) sonst 2,0; Hof `4·gP` in Artfarbe Alpha 0,5 (dunkel additiv); Schweif 8 Kreise hinter dem Planeten, Winkelschritt 0,07, Alpha `0,38·(1 − i/9)`, Radius `gP·(1 − i/12)` | `punkt` |
| Fehlgeschlagene Datei | Ring `r = gP + 1,5` in `KvError`, Breite 1,4, Alpha 0,8 | `s.fehl` |
| Ankunft | Platz wird „da“ bei `tOut + 0,95 s` (Ende des Pixelflugs); 0,8 s Ring in Artfarbe, `r = 5 + 14·easeOut(e/0,8)`, Alpha `1 − easeOut(e/0,8)`, Breite 1,6 | Zeile 4627 |
| Kern | `g = (1 + 0,35·Anteil)`, `Anteil = fertig/N`, Puls `1 + 0,04·sin(2,2t)`; Hof Mint `(46 + 36·Anteil)·Puls` Alpha 0,20 dunkel / 0,14 hell; Hof Weiß `20g` Alpha 0,75 / 0,35; Kreuz `L = 20g·Puls` waagerecht, `0,7L` senkrecht, Weiß Alpha 0,45 / 0,35; Scheibe `#FFFFFF` `6,5g`; Mint-Ring `7,6g` Breite 1,5. „Weiß“ ist im Hellen Mint | `kern`, `weiss()` |
| Sterne am Loch | 70 Punkte, `x = T.x + cos(w)·√u·(tw/2 + 6)`, `y = T.y + sin(w)·√u·90`, Größe 1 (85 %) oder 1,6; Alpha `(0,25 + 0,5·(0,5 + 0,5·sin(t·f + 9a)))·(dunkel 1 / hell 0,7)·(0,4 + 0,6·Anteil)`, `f ∈ [0,5; 2]`; Farbe `#DFE9EF` dunkel, `KvLineStrong` hell | `sterneUm` |
| „n von N“ | bleibt XAML (`DoneText`, `LiveSetting=Polite`); ~~die Szene zeichnet keine Zahl~~ **überholt (Teil D):** der Renderer zeichnet den Zähler zusätzlich unter dem weißen Loch (Text aus `Swirl_Counter_Of`), siehe `docs/06-design.md` | eigene Festlegung |
| Staubringe ab ~150 Dateien | offen, nicht im Entwurf; die Bündelung auf 10 Bahnen trägt bis dahin, Punkte-Deckel 150 | – |

## Abschluss (`FinaleScene`)

Start: 1,05 s nach dem letzten `Completed`/`Failed` der Runde (`RoundState.Finished` + Wartezeit, damit der letzte Pixelstrom landet). Vorher blendet die Seite Eingang und Speicherort-Karte aus (Composition, 300 ms). Zeit `f` seit Start, Konstanten aus dem Entwurf:

| Konstante | Wert | Bedeutung |
|---|---|---|
| `PH1` | 1,15 s | Anlauf: Planeten drehen hoch, Bahnen ziehen sich zusammen, Staub spiralt ins weiße Loch |
| `PH2` | 0,75 s | beide Löcher beginnen zu kreisen (Winkelbeschleunigung auf das Anfangstempo des Tanzes) |
| `ANL = PH1 + PH2` | 1,90 s | Beginn des Tanzes |
| `M` | 2,40 s | Dauer des Tanzes bis zur Verschmelzung |
| `STILL` | 0,28 s | Stille vor der Supernova |
| `DET = ANL + M + STILL` | 4,58 s | Supernova, `e = f − DET` |
| `RUNDEN` | 4 | Umläufe der Löcher im Tanz (vor dem Deckel) |
| `MAXW` | `16°·60 = 16,76 rad/s` | Deckel der Winkelgeschwindigkeit der Löcher (höchstens 16° je Bild) |

Geometrie: `H` = schwarzes Loch (Wirbelmitte), `T` = weißes Loch, `C = (W/2, 150)` Endmitte, `s0 = Mitte(H, T)`, `fl0 = 0,45·0,55`, `d0 = |v|` mit `v = (H.x − T.x, (H.y − T.y)/fl0)`, `phi0 = atan2(v)`. Gemeinsamer Mittelpunkt `m(f) = lerp(s0, C, ease(phase(f, PH1, ANL + 1,2)))`.

| Phase | Zeitfenster (s) | Regel | Quelle |
|---|---|---|---|
| Planeten drehen hoch | 0 … `ANL + M` | Zusatzwinkel `0,9·(24/rx(j))^0,7·X(f)`; `X` ist das Integral der Rate `14·(e^{3,2·phase(f,0,PH1)} − 1)/(e^{3,2} − 1) + 4·phase(f, ANL, ANL + M)` (Tabelle mit `dt = 0,002` beim Start berechnet) | `tabelle`, `bahnW` |
| Bahnen ziehen sich zusammen | 0,15 … | Maßstab `lerp(1, 0,58, ease(phase(f, 0,15, PH1 + 0,45)))·lerp(1, 0,75, ease(phase(f − ANL, 0, M)))` | `skalVon` |
| Bahnlinien verblassen | 0,3 … `PH1 + 0,3` | Alpha `1 − phase(f, 0,3, PH1 + 0,3)` | Zeile 4797 |
| Zähler und Sterne | 0 … 0,4 / `PH1` … `ANL + 1` | Zähler-Alpha `1 − phase(f, 0, 0,4)` (XAML `DoneText` blendet aus); Sterne am Loch `1 − phase(f, PH1, ANL + 1)` | Zeile 4792, 4824 |
| Staub (Sog) | ab 0,1 | `sog = phase(f, 0,1, 0,7·PH1)·(1 − phase(f − ANL, M − 0,5, M − 0,1))`; 120 Körner, je Korn Planet `k = i mod N`, Phase `p`, Streuung `d ∈ ±0,15`, 20 % weiß; Lebensdauer `TL = 0,85 s`, `u = (f/TL + p) mod 1`, Start `fr = f − u·TL`; Lage `r = 4 + r0·(1 − v)^1,4·(1 + d·v)` mit `r0 = rx(j)·skalVon(fr)`, Winkel `w0 + (bahnW(k, x) − w0)·(1 + 1,3v)`, um das weiße Loch; Strich zur Lage bei `f − 0,045`, Breite 1,2, Kopf 2 × 2; Alpha `sog·sin(πu)^0,7·(0,35 + 0,65u)`; additiv im Dunkeln | Zeile 4799–4813 |
| Hof am weißen Loch | 0 … | Mint, Radius `50 + 40·hoch`, Alpha `0,22·hoch·(1 − phase(f − ANL, 0, 1))`, `hoch = phase(f, 0, PH1)` | Zeile 4814 |
| Trägheit je Planet | 0 … `ANL + M` | Feder zum Sollplatz `zielAbschluss(k, f)`: Schritt `h = 1/240`, `kf = lerp(150, 45, j/max(1, B − 1))·(1 + 12·nah²)`, Dämpfung `c = √kf`, Auslenkung weich begrenzt `28·tanh(l/28)`; Spur 14 Punkte relativ zum weißen Loch, alle 4 Schritte; gezeichnet `round(8 + 6·hoch)` Punkte, Alpha `aus·0,42·(1 − (i+1)/(n+1))`; höchstens 2400 Schritte je Bild | `simulieren` |
| Löcher kreisen an | `PH1` … `ANL` | Winkel `a = w0·PH2·u²/2` mit `u = phase(f, PH1, ANL)`, Drehung um `s0` mit Abplattung `fl0`, verschoben nach `m(f)`; `w0 = tempo(0)·k` (Anfangstempo des Tanzes) | `loecher`, `lage` |
| Spuren der Löcher | ab `PH1` | 10 Segmente im Abstand 0,018 s, Breite `3·(1 − i/12)`, Alpha `0,6·phase(f, PH1, ANL)·(1 − i/11)`; schwarz Violett, weiß Mint; additiv | `spurLoch` |
| Tanz | `ANL` … `ANL + M` (`fd = f − ANL`) | Abstand `d(fd) = d0·lerp(1, 0,36, ease(phase(fd, 0, 1,1)))·((M − fd)/M)^0,25`; Tempo `(max(d, 0,02·d0)/d0)^−1,5`, normiert auf `RUNDEN` Umläufe (`k = 4·2π/∑tempo·dt`), je Schritt `ω = min(tempo·k, MAXW)`; Winkel `phi = phi0 + w0·PH2/2 + ∑ω·dt`; Abplattung `0,45·(0,55 + 0,45·clamp(1 − d/d0))`; schwarz bei `m + (cos phi, sin phi·fl)·d/2`, weiß gegenüber; `nah = clamp(1 − d/(0,36·d0))` | `tabelle`, `loecher` |
| Kern und Loch im Tanz | | Kern-Maßstab `1 + 0,2·nah + 0,1·hoch`, schwarzes Loch `1 − 0,25·nah`, Hof in `C` Weiß `40 + 80·nah` Alpha `0,35·nah²` | Zeile 4821–4823 |
| Planeten eingesogen | `fd` = `M − 0,5` … `M` | Sollplatz gleitet zur Lochmitte mit `ease(phase(fd, M − 0,5, M))`; Alpha `1 − phase(fd, M − 0,3, M)` | `zielAbschluss`, `aus` |
| Stille | `DET − STILL` … `DET` | nur ein weißer Glühpunkt in `C`: Radius `70·(1 − i/STILL) + 6`, Alpha 0,9 | `nova`, `i < STILL` |
| Supernova | `e = 0` … 1,3 | Radialverlauf in `C`, Radius `20 + easeOut(e/1,3)·0,32·W`, senkrecht ×0,62: Weiß 0,7 → Mint 0,35 (bei 0,55) → Violett 0,25 (bei 0,85) → 0, alle ×(1 − u); **ein** waagerechter Lichtstreifen über die ganze Breite, 2,4 DIP hoch, Mint 0 → Weiß 0,9 → Mint 0, Alpha ×`(1 − e/0,9)`, dazu flacher Mint-Hof (`0,5·W`, senkrecht ×0,05, Alpha 0,5) | `nova` |
| Explosion (`st = 1,3`) | `e = 0` … 1,7 | Hof Weiß `60 + 280·easeOut(e/0,35)·st` Alpha `0,7·(1 − e/0,9)`; Hof Mint `30 + 140·easeOut(e/0,2)` Alpha `0,6·(1 − e/0,6)`; Blitz über die Fläche für `e < 0,45` Weiß Alpha `0,1·st·(1 − e/0,45)`; drei Wellen (Ellipse, Abplattung 0,45) `r = 10 + easeOut(u)·0,62·W·st`, `u = (e − 0,12·i)/1,4`, Alpha `0,8·(1 − u)`, Breite `3,4·(1 − u) + 0,6`, erste Weiß, andere Mint; 320 Funken: Winkel zufällig, `v = (0,25 + 0,75·rand^0,7)·0,5·W`, Dauer 0,9 … 1,7 s, Farbe eines zufälligen Planeten (8 % Weiß), Breite 2,2 (25 %) sonst 1,3, Strich von `v·st·easeOut(u − 0,07)` bis `v·st·easeOut(u)`, senkrecht ×0,5, Alpha `0,85·(1 − u)`; additiv im Dunkeln | `explosion` |
| Beben | `e = 0` | **Canvas:** `e < 0,45` Versatz der ganzen Zeichnung `(sin(97e)·k, cos(83e)·0,6k)`, `k = 5·(1 − e/0,45)`. **Seite (XAML):** Composition-`Offset`-Animation auf `ContentFrame`: Stationen (−6, 4), (5, −3), (−3, 2), (1,5, −1), 0 über 520 ms `ease-out`; Kopfleiste (`StepHeader`) Stationen (5, 3) mit 0,4° Drehung, (−5, 0), (3, 0), (−1,5, 0), 0 über 700 ms ab 40 ms, `cubicBezier(.3, .7, .4, 1)`. ~~Die Zeilen der Liste beben **nicht** einzeln~~ **überholt (Teil E):** die Zeilen beben wie im Mischentwurf einzeln versetzt (40 ms + 70 ms je Zeile, höchstens 12 Zeilen), siehe `docs/06-design.md`. Nur einmal je Runde, nie bei Reduced Motion | `beben` |
| Sternfeld über die Fläche | `e` = 0,4 … 1,8 | 110 Sterne, gleichverteilt, Alpha `(0,25 + 0,5·(0,5 + 0,5·sin(t·f + a)))·(dunkel 0,8 / hell 0,55)·phase(e, 0,4, 1,8)` | `sterneVoll` |
| Ring | `e` ≥ 0,1 | Ellipse um `C`: `Rx = min(0,44·W, 440)`, `Ry = 116`; Linie Mint Alpha `0,22·phase(e, 0,9, 1,6)`; Planeten sortiert nach `TargetPlanner.KindOrder`, dann Platznummer; Planet `o` gleitet von `C` zur Ringposition mit `easeOut(phase(e, 0,1 + 0,35·o/N, 1,2 + 0,35·o/N))`, Winkel `−π/2 + o/N·2π + 0,08·t` (Ring dreht 0,08 rad/s), Alpha `phase(e, 0,1, 0,3)`; fehlgeschlagene Dateien als Korallen-Ring | Zeile 4826–4834 |
| Häkchen | `e` = 0,5 … 1,4 | Mitte `(C.x, lerp(C.y, 96, ease(phase(e, 0,6, 1,4))))`, `k = phase(e, 0,5, 1,4)`: Mint-Scheibe `r = 16·easeOut(k/0,45)`, Hof 58 Mint Alpha `(0,22 dunkel / 0,16 hell)·(1 + 0,1·sin(1,6t))`; Haken ab `k = 0,4` über `h = (k − 0,4)/0,6`: Punkte (−7, 0,5) → (−2, 5,5) → (7,5, −5,5), erstes Stück bis `h = 0,4`, Breite 3, runde Enden, Farbe `KvOnMint` | `haken` |
| Bericht | `e` ≥ 1,2 | die bestehende XAML-Berichtskarte (`HasReport`) wird eingeblendet (Composition 600 ms, Deckkraft 0 → 1, Versatz 8 → 0), zentriert unter dem Häkchen (`Margin.Top` 128), Breite `min(440, 90 %)`; Ansage `LiveSetting=Assertive` wie heute; „Ordner öffnen“ und „Neue Runde“ bleiben Knöpfe der Karte | `w5-bericht` |
| Neue Runde | – | `FinaleScene` wird verworfen, `SwirlScene.Clear()`; Eingang und Speicherort blenden wieder ein | `weg` |

Bei Reduced Motion gibt es keine `FinaleScene`: die Seite zeigt sofort Ring, Häkchen und Bericht in der statischen Ansicht (siehe unten).

## Kurven und Hilfsfunktionen (`Scenes/SceneMotion.cs`, ergänzt `GalaxyMotion`)

| Name | Formel | Verwendung |
|---|---|---|
| `Ease(t)` | `t < 0,5 ? 4t³ : 1 − (−2t + 2)³/2` | Loch-Fahrt, Einflug, Ausflug, Kamera, Anlauf |
| `EaseOut(t)` (`aus3`) | `1 − (1 − t)³` | Ringe, Ankunft, Supernova, Häkchen-Scheibe, Ringplätze |
| `Smooth(t)` (`glatt`) | `t²(3 − 2t)` | vorhanden aus der Galaxie |
| `EaseBack(t)` | `1 + 2,2(t − 1)³ + 1,2(t − 1)²` | vorhanden aus der Galaxie |
| `Phase(f, a, b)` | `clamp((f − a)/(b − a))` | überall im Abschluss |
| `CubicBezier(x1, y1, x2, y2)` | CSS-Kurve, Lösung von `x(s) = t` per Newton (8 Schritte) mit Bisektion als Rückfall, Ergebnis `y(s)`; Instanzen: `(.6, 0, .9, .5)`, `(.3, 1.5, .5, 1)`, `(.45, .05, .3, 1)`, `(.3, .1, .25, 1)`, `(.3, 0, .3, 1)`, `EaseOutCss = (0, 0, .58, 1)`, `Feder = (.3, 1.2, .5, 1)`, `Beben = (.3, .7, .4, 1)` | Stationen der Übergänge, Beben |
| `Keyframe`-Auswertung | Stationen `(Offset, Position, Width, ScaleX, ScaleY, Rotation, AlphaA, AlphaB, EasingToNext)`; für `u ∈ [Offset_i, Offset_{i+1}]` gilt `s = Easing_i((u − o_i)/(o_{i+1} − o_i))`, alle Felder `lerp(feld_i, feld_{i+1}, s)`. Das entspricht der Web-Animations-API des Entwurfs (Easing je Abschnitt, lineare Interpolation der Werte) | `GhostTrack.Evaluate(u)` |

## Farben

Alle aus `ScenePalette` (erweitert um `LineStrong`, `Paper`, `PaperLine`, `OnMint`, `Shadow`); der Renderer baut daraus Pinsel und wirft sie beim Themenwechsel weg.

| Zweck | Token |
|---|---|
| Geister, Planeten, Pixel-Tönung, Ringe im Loch, Bahnen | `KvImage`, `KvAudio`, `KvVideo`, `KvDocument`, `KvModel` |
| Loch-Ring, Zielring, Kern-Ring, Wellen, Häkchen, Ring des Abschlusses, Weiß im Hellen | `KvMint`; Häkchen-Strich `KvOnMint` |
| Höfe der Löcher, Nova-Rand, Spur des schwarzen Lochs | `KvAudio` (Violett) |
| Fehlgeschlagene Dateien | `KvError` |
| Bahnlinien leer, leere Plätze, Sterne im Hellen | `KvLineStrong` |
| Geist-Karten, Blattpapier, Blattlinien, Schatten | `KvPanel`, `KvLine`, `KvPaper`, `KvPaperLine`, `KvShadow` |
| Texte auf Geistern | `KvInk`, `KvMuted` |
| Lochscheibe | `#000000` dunkel, `#0B0F12` hell (feste Werte, wie in der Galaxie) |
| Sterne im Dunkeln, Weiß im Dunkeln | `#DFE9EF`, `#FFFFFF` (feste Werte) |
| Inhalt der Blatt-Attrappe | feste Hex-Werte in `SheetRaster` (siehe oben) |

## Szene-Modelle (WinUI-frei, `Kvertis.App/Scenes/`)

Regeln wie in `schritt-1-galaxie.md`: Zeit nur aus `Update(TimeSpan)` (auf 100 ms geklemmt), Glättung `1 − exp(−dt·rate)`, Zufall nur aus dem injizierten `Random`, keine Texte außer den fertig gelieferten Beschriftungen, Kommandos über `Enqueue`, Lesen vom UI-Thread nur über `Snapshot`.

```csharp
namespace Kvertis.App.Scenes;

// ---- Überlagerung ---------------------------------------------------------------------------------

public enum TransitionKind { WormholeForward /* 1→2 */, ArcBack /* 2→1 */, SheetsForward /* 2→3 */ }

public enum GhostShape { TrayHeader, KindSymbol, Sheet }

/// <summary>What the overlay flies: a stand-in, never a copy of a XAML element (ADR-023).</summary>
public sealed record GhostSpec(GhostShape Shape, MediaKind Kind, string Label, string? Count, string? FormatLabel, string? FileName);

/// <summary>Anchor data the service measured on the UI thread; plain vectors, no UI types.</summary>
public sealed record TransitionEndpoint(Vector2 Centre, Vector2 Size);

public sealed record TransitionPlan(
    TransitionKind Kind,
    IReadOnlyList<(GhostSpec Ghost, TransitionEndpoint From, TransitionEndpoint To)> Ghosts,   // per kind (or per sheet)
    TransitionEndpoint HoleFrom, float HoleRadiusFrom,
    TransitionEndpoint HoleTo, float HoleRadiusTo);

public readonly record struct GhostState(GhostSpec Spec, Vector2 Position, float Width, float ScaleX, float ScaleY, float Rotation, float AlphaA, float AlphaB);
public readonly record struct RingState(Vector2 Centre, float Radius, float Flatten, SceneColor Color, float Alpha, float Width);
public readonly record struct HoleState(Vector2 Position, float Radius, float Alpha);

/// <summary>One transition, deterministic in time. Built from a plan; finished when every track and the hole are done.</summary>
public sealed class TransitionScene
{
    public TransitionScene(TransitionPlan plan, ScenePalette palette, Random random);
    public TimeSpan Duration { get; }                  // fertigZeit + 0.08 s + 0.28 s hole fade
    public TimeSpan Time { get; }
    public bool IsFinished { get; }
    public float PageFadeIn { get; }                   // 0..1, starts at fertigZeit - 0.26 s over 0.32 s; the service drives the page opacity from the snapshot
    public IReadOnlyList<GhostState> Ghosts { get; }
    public IReadOnlyList<RingState> Rings { get; }
    public IReadOnlyList<GalaxyParticle> Sparks { get; }   // only SheetsForward, last 0.28 s
    public HoleState? Hole { get; }
    public void Update(TimeSpan elapsed);
    public void Finish();                              // jump to the end state
    public TransitionSnapshot Snapshot { get; }        // (IsFinished, PageFadeIn, Time)
}

/// <summary>Keyframe tracks and the CSS cubic-bezier solver; pure functions, unit tested.</summary>
public static class SceneMotion
{
    public static float Ease(float t); public static float EaseOut(float t); public static float Phase(float f, float a, float b);
    public static float CubicBezier(float x1, float y1, float x2, float y2, float t);
    public static GhostState Evaluate(IReadOnlyList<GhostKeyframe> track, float u);
}

// ---- Schritt 3 -------------------------------------------------------------------------------------

public enum FileOutcome { Completed, Failed, Cancelled }
public enum PixelTarget { WhiteHole, Bag, Inbox }

public abstract record SwirlCommand;
public sealed record SetPlan(IReadOnlyList<SwirlFileSpec> Files) : SwirlCommand;      // whole round: inbox stack, white hole capacity N
public sealed record BeginFile(Guid Id) : SwirlCommand;                                // took a swirl slot (at most 2)
public sealed record SetProgress(Guid Id, float Fraction) : SwirlCommand;
public sealed record FinishFile(Guid Id, FileOutcome Outcome, PixelTarget Target) : SwirlCommand;   // for files that never had a slot: only the white-hole arrival
public sealed record BeginFinale(int Completed, int Failed) : SwirlCommand;
public sealed record SetBagAnchor(Vector2 Position) : SwirlCommand;
public sealed record SwirlResize(float Width, float Height) : SwirlCommand;
public sealed record SwirlClear : SwirlCommand;

public sealed record SwirlFileSpec(Guid Id, MediaKind Kind, string FormatFrom, string FormatTo, string FileName, bool HasOwnLocation);

/// <summary>A cell of the drawn sheet: colours of the old and new sheet plus three random numbers (worksheet "Pixelwirbel").</summary>
public struct SheetCell { public byte Gx, Gy; public SceneColor C0, C1; public float A0, A1; public float R1, R2, R3; public Vector2 Last; }

public static class SheetRaster
{
    public const int Width = 96, Height = 124, Step = 4;
    public static SheetCell[] Build(MediaKind kind, ScenePalette palette, Random random);   // ≤ 744 cells, deterministic per seed
}

public sealed class SwirlScene
{
    public SwirlScene(float width, float height, ScenePalette palette, Random random, SwirlBudget budget);
    public WhiteHoleScene WhiteHole { get; }           // slots, orbits, planets, core, stars
    public FinaleScene? Finale { get; }                // created by BeginFinale, null before and after Clear
    public IReadOnlyList<SwirlFile> Files { get; }     // stack, in swirl, leaving
    public float MeanProgress { get; }
    public Vector2 Centre { get; }                     // (W/2, H/2); Radius = 100, Flatten = 0.42
    public void Enqueue(SwirlCommand command);
    public void Update(TimeSpan elapsed);
    public SwirlSnapshot Snapshot { get; }             // (FinaleTime, FinalePhase, ShowReport, ShakePage)
}

public sealed class WhiteHoleScene
{
    public int N { get; } public int Orbits { get; }   // B
    public int Capacity(int orbit);                    // kap(j)
    public float OrbitRadius(int orbit);               // rx(j)
    public float Angle(int slot, float t);             // winkel(k, t)
    public Vector2 SlotPosition(int slot, float t, Vector2? centre = null, float scale = 1f);
    public IReadOnlyList<WhiteHoleSlot> Slots { get; } // in order of finishing; Failed = coral ring
}

public enum FinalePhase { RunUp, Dance, Silence, Nova, Ring, Done }

public sealed class FinaleScene
{
    public const float Ph1 = 1.15f, Ph2 = 0.75f, M = 2.4f, Still = 0.28f; public const int Rounds = 4;
    public static readonly float MaxAngularSpeed = 16f / 180f * MathF.PI * 60f;
    public FinalePhase Phase { get; }
    public (Vector2 Black, Vector2 White, float Near) Holes(float f);
    public IReadOnlyList<FinalePlanet> Planets { get; }  // simulated with inertia, trails
    public bool ShakeRequested { get; }                  // true exactly once at e = 0 (the view runs the XAML shake)
    public bool ShowReport { get; }                      // e ≥ 1.2
    public void Complete();                              // reduced motion / skip: jump to the end
}

public sealed record SwirlBudget(int MaxSwirlFiles = 2, int MaxStackSheets = 24, int MaxEmptyPlaces = 150, int Dust = 120, int Sparks = 320, int StarsNear = 70, int StarsFull = 110, int MaxParticles = 4000);
```

Anbindung an den Koordinator (in `ConvertPageViewModel`, UI-Thread, über `Enqueue`): `LoadAsync` → `SetPlan`; `RoundChangedEventArgs` mit `JobChangeKind.StateChanged` und `Running` → `BeginFile`, sobald die Zeile einen der zwei Wirbelplätze bekommt (dieselbe Regel wie `Swirl` heute); `Progress` → `SetProgress(job.Progress.Fraction)`; `Completed`/`Failed`/`Cancelled` → `FinishFile` mit Ziel aus `IsOwnLocation` (Tasche) bzw. Ausgang; `RoundState.Finished` → nach 1,05 s (`TimeProvider`) `BeginFinale(report.Completed, report.Failed)`; „Neue Runde“ → `SwirlClear`. `RelocateWaiting` ändert nichts an der Szene (nur wartende Dateien). `Snapshot.ShakeRequested` löst einmal die XAML-Bebenanimation aus, `Snapshot.ShowReport` blendet die Berichtskarte ein; beides liest das ViewModel per `DispatcherQueueTimer` (100 ms) oder die Hülle meldet es per Ereignis auf dem UI-Thread (wie `ZoomRequested` in `GalaxyCanvas`; bevorzugt, weil kein Polling).

## Renderer und Hüllen (`Kvertis.App/Rendering/`)

- `TransitionRenderer.Draw(ds, TransitionScene)`: Reihenfolge Ringe, Geister mit Alpha B, Geister mit Alpha A, Funken, Loch (wie `lochMalen`: Höfe Violett `3,2r` 0,18 und Mint `1,9r` 0,22, Scheibe, Mint-Ring `r + 1,5` Breite 2; Glühen über `CanvasRadialGradientBrush`, kein `shadowBlur`). Geist-Transformation: `ds.Transform = Matrix3x2.CreateScale(sx·sc, sy·sc) · CreateRotation(rot) · CreateTranslation(pos)` mit `sc = Width/96` bzw. `Width/Quellbreite`, gezeichnet um die Mitte. Texte per `CanvasTextFormat` (Segoe UI Variable), Breiten gecacht.
- `TransitionOverlay` (UserControl, `Rendering/TransitionOverlay.xaml`): eigenes `CanvasAnimatedControl` (`TargetElapsedTime` 1/60 s, `ClearColor` transparent, `DpiScale` Deckel 2,0 wie `GalaxyCanvas`), `Begin(TransitionScene)`, `Finish()`, `Completed`-Ereignis auf dem UI-Thread, `Paused = true` und `Visibility = Collapsed` im Leerlauf. Erzeugt beim ersten Übergang, danach am Leben und pausiert (`RemoveFromVisualTree` wird wegen des bekannten Absturzes nicht gerufen, Nachtrag zu ADR-022). Bei `DrawFailed` fällt der Dienst dauerhaft auf schlichte Wechsel zurück.
- `SwirlRenderer.Draw(ds, SwirlScene)`: Reihenfolge Hof, Bahn-Ellipse, weißes Loch (Sterne, Bahnen, Kern, leere Plätze, Planeten mit Schweif), Stapel-Attrappen, Pixel (Spur, dann Quadrat), schwarzes Loch; im Finale `FinaleRenderer` mit derselben Session. Pixel: `FillRectangle` je Zelle; erst bei gemessenen über 4 ms je Bild Umstellung auf `CanvasSpriteBatch` mit einem zur Laufzeit erzeugten Weißquadrat.
- `SwirlCanvas` (Hülle wie `GalaxyCanvas`) und `Views/SwirlHost.xaml` (Weiche wie `GalaxyHost`): Animationen an → `SwirlCanvas` unter den XAML-Karten; Animationen aus oder Hoher Kontrast → heutige XAML-Ansicht (Fortschrittskarten, Bericht) unverändert. `ConvertPage.OnNavigatedTo/From` → `Resume()/Suspend()`; Fenster unsichtbar → `SetPaused(true)`.
- Thread-Regel unverändert: `Update`/`Draw` nur auf der Spielschleife; UI-Thread nur `Enqueue`, `Snapshot`, `Finish()` (setzt ein Flag, das `Update` liest).
- Verbote (Reviewer-Suche): kein `CanvasBitmap`, `CanvasImageSource`, `RenderTargetBitmap`, `CanvasBitmap.CreateFromBytes` in `Rendering/`; kein `Microsoft.Graphics`/`Microsoft.UI` unter `Scenes/`.

## Reduced Motion und Hoher Kontrast

| Zustand | Übergänge | Schritt 3 | Abschluss |
|---|---|---|---|
| Animationen an, kein Hoher Kontrast | Überlagerung wie oben | Wirbel, weißes Loch | `FinaleScene`, Beben, Bericht bei `e ≥ 1,2` |
| Animationen aus | kein Overlay; Seitenwechsel mit `SuppressNavigationTransitionInfo` (heutiger Stand) | heutige XAML-Ansicht: Fortschrittskarten, „fertig n von N“, Liste | sofort Bericht als Text (heutige Karte); zusätzlich statischer Ring: XAML `ItemsRepeater` mit einem 8-px-Punkt je Datei auf einer Ellipse (Artfarbe, Fehler Koralle) und Häkchen-Symbol darüber, ohne Bewegung |
| Hoher Kontrast | kein Overlay, kein Ring; Seitenwechsel wie heute | heutige XAML-Ansicht | Bericht als Text, kein Ring (Artfarben fallen zusammen, ADR-017) |

Umschalten zur Laufzeit (`IMotionSettings.Changed`): laufender Übergang `Finish()`, `SwirlHost.Rebuild()` wie `GalaxyHost`.

## Leistung

- Zu jeder Zeit läuft höchstens **ein** `CanvasAnimatedControl`: Galaxie (Schritt 1), Wirbel (Schritt 3) oder Überlagerung. Beim Übergang pausiert der Dienst zuerst die Fläche der Quellseite (`SetPaused(true)`), die Zielseite bleibt pausiert, bis `Completed` kommt. Risiko zweier gleichzeitiger Schleifen (doppelte Bildrate, zwei Geräte-Kontexte, Reihenfolge der Zeichnung nicht garantiert) wird damit vermieden; der Reviewer prüft, dass `TransitionService` die Pausen setzt.
- Budget: Überlagerung ≤ 5 Geister + 24 Blätter + 10 Ringe + 40 Funken; Wirbel ≤ 2 × 744 Pixel + 24 Blatt-Attrappen + 70 Sterne + 150 Plätze + Planeten; Finale zusätzlich 120 Staub + 320 Funken + 110 Sterne + 14 Spurpunkte je Planet. Alles zusammen unter 2 500 Zeichenaufrufen je Bild, deutlich unter dem Deckel 4 000 aus `GalaxyBudget`. `SwirlBudget.MaxParticles` gilt hart; die Trägheitssimulation ist auf 2 400 Schritte je Bild begrenzt.
- Bildrate 1/60 s; bei Akku (`PowerManager.EnergySaverStatus`) 1/30 s, sobald das für die Galaxie nachgeholt ist (dort noch offen).
- Pause: Fenster unsichtbar, andere Seite aktiv, Verlauf offen (Schritt 3: `SetPaused` wie `MainPage.UpdatePaused`). Ein laufender Lauf läuft in der Queue weiter; die Szene holt den Fortschritt beim nächsten `Update` nach (Kommandos bleiben in der Warteschlange).
- DPI-Deckel 2,0; die Szenen rechnen in DIP.
- Fenstergröße während des Wirbels: `SwirlResize` setzt Mitte, `T` und Bahnradien neu; Pixel im Flug springen nicht, weil ihre Sollpositionen relativ zur Mitte berechnet werden. Während des Finales wird die Tabelle (`tabelle()`) nicht neu gerechnet; eine Größenänderung im Finale ruft `Complete()`.
- Messung: `KVERTIS_GALAXY_STATS` gilt auch für Wirbel und Überlagerung.

## Barrierefreiheit

- Überlagerung und Wirbelfläche sind dekorativ (`AccessibilityView=Raw`, `IsTabStop=False`). Während eines Übergangs sind Zeiger und Navigationsbefehle gesperrt; der Fokus bleibt auf dem Element, das den Wechsel ausgelöst hat, und wandert nach dem Wechsel wie heute auf die neue Seite. Der Erzähler bekommt nichts vom Übergang zu hören.
- Alle Informationen des Abschlusses stehen im Bericht (Text, `LiveSetting=Assertive`) und in der Liste; Ring und Häkchen sind Schmuck.
- Das Beben bewegt nur `ContentFrame` und `StepHeader` um höchstens 6 DIP für 0,7 s; bei Reduced Motion entfällt es.

## Teilaufgaben

Parallel möglich: **A** (Szenen, Linux-testbar) und die **D-Vorbereitung** (Anker in den Seiten, `SwirlHost`-Weiche, Composition-Beben, Ein- und Ausblenden von Eingang und Speicherort) sind unabhängig voneinander. B und C brauchen A; D braucht A und die D-Vorbereitung; E braucht D.

### A · Szene-Modelle mit deterministischen Tests (ui-entwickler, tester)

- `Scenes/SceneMotion.cs`, `TransitionScene.cs` (+ `TransitionPlan`, `GhostSpec`, `GhostKeyframe`, `GhostState`, `RingState`, `HoleState`), `SheetRaster.cs`, `SwirlScene.cs`, `SwirlCommand.cs`, `WhiteHoleScene.cs`, `FinaleScene.cs`, `SwirlBudget.cs`; `ScenePalette` erweitert. Alle unter `Compile Include` in `Kvertis.App.Tests` (Glob ist schon da).
- Tests (`tests/Kvertis.App.Tests/Scenes/`): `CubicBezier(.6,0,.9,.5)` an 0, 0,5, 1 gegen bekannte Werte (±0,01), monoton; Wurmloch mit fünf Arten: Gesamt 1,88 s, Geist 0 bei `t = 0,588 s` in der Lochmitte mit Alpha A 0, bei 1,4 s am Ziel mit Breite `T.w` und Alpha B 1, Geist 4 startet 0,48 s später, zwei Ringe je Art zur richtigen Zeit, Loch startet bei 1,034 s und endet bei 1,786 s; Bogen: Scheitel bei Anteil 0,5 um `40 + 14i` über der höheren Kante, Gesamt 1,10 s; Blätter: Gesamt ≤ 2,0 s für `n = 1, 7, 11, 24, 60`, höchstens 24 Blätter, Loch-Bogen 30 DIP in der Mitte der Fahrt, Funken nur in den letzten 0,28 s; `Finish()` liefert den Endzustand; `SheetRaster`: 24 × 31 Zellen minus leere Ecken, jede Zelle mit `A0 ≥ 0,35` oder `A1 ≥ 0,35`, Reiter-Zellen in Artfarbe, Determinismus je Seed; `SwirlScene`: nach `BeginFile` und 0,95 s liegt jedes Pixel auf seiner Wirbelposition, Winkelgeschwindigkeit je Pixel in `[0,5; 2,1]` rad/s, `SetProgress(0,5)` → `k = 0,35`, `FinishFile(Completed)` → nach 0,95 s alle Pixel am Platz des weißen Lochs, `Failed` → zurück auf den Stapel, dritte laufende Datei bekommt keinen Wirbelplatz; `WhiteHoleScene`: `Orbits`/`Capacity` für `N = 1, 7, 10, 11, 25` (z. B. `N = 25`: 10 Bahnen, fünf mit 3 Plätzen, fünf mit 2), `OrbitRadius` bei `tw = 300` → 24, 36, …, 132, Plätze in Reihenfolge des Fertigwerdens; `FinaleScene`: Winkelgeschwindigkeit der Löcher nie über `MaxAngularSpeed`, Abstand monoton fallend und 0 bei `ANL + M`, beide Löcher spiegelbildlich zu `m`, Mittelpunkt bei `ANL + 1,2` gleich `C`, `Phase` durchläuft `RunUp → Dance → Silence → Nova → Ring → Done`, `ShakeRequested` genau einmal, `ShowReport` ab `e ≥ 1,2`, Planeten am Ende auf dem Ring (Abstand zur Ellipse < 1 DIP), `Complete()` springt ans Ende; Determinismus: zwei Szenen mit gleichem Seed und gleichen Kommandos liefern nach 8 s identische Snapshots; `Update` klemmt auf 100 ms.

### B · Überlagerung, Anker, Wurmloch 1→2 und Bogen 2→1 (ui-entwickler, reviewer)

- `Services/ITransitionAnchors.cs`, `Services/TransitionService.cs` (`ITransitionService`: `TryBegin`, `Finish`, `IsTransitioning`, `Changed`), Registrierung als Singleton; `StepNavigationService.GoTo` fragt den Dienst; `FrameNavigationService.Navigate` bekommt den optionalen Übergangsparameter; `MainWindow.xaml` bekommt `TransitionOverlay` und hängt `SizeChanged`/`VisibilityChanged` an `Finish()`.
- `Rendering/TransitionRenderer.cs`, `Rendering/TransitionOverlay.xaml(.cs)`; `MainPage` und `TargetPage` implementieren `ITransitionAnchors` (Fachköpfe, Artenliste, Loch); `MainViewModel`/`TargetPageViewModel` liefern Beschriftung und Zahl je Art für die Geister.
- Abnahme unter Windows: „Weiter: Ziel“ mit drei Arten: Fächer fliegen nacheinander ins Loch, Symbole springen links heraus, Loch fährt in die Mitte, Schritt 2 blendet ein, danach ist alles bedienbar; „Zurück“: Bogenflug in die Fächer; während des Flugs reagieren weder Zeiger noch Strg+O; Fenster in der Mitte des Flugs verkleinern → sofortiger Endzustand ohne Rest; Reduced Motion → schlichter Wechsel; Prozessor-Last im Leerlauf des Overlays null (pausiert); Galaxie steht während des Flugs still und läuft danach weiter; `check.sh` grün.

### C · Blätter 2→3 (ui-entwickler)

- `ConvertPage` implementiert `ITransitionAnchors` (`Inbox`, `SwirlCentre`, `Bag`); `SheetsForward` in Dienst und Renderer; Übergabe der Dateiliste (Art, Kürzel, Name) aus `session.Plan`.
- Abnahme: „Weiter: Umwandeln“ mit 7 und mit 40 Dateien: Blätter erscheinen aus dem Loch, landen gestaffelt links, Loch fährt im Bogen in die Wirbelmitte und geht in Funken auf; bei 40 Dateien fliegen 24, Gesamt ≤ 2 s; danach zeigt der Wirbel dasselbe Loch an derselben Stelle (Sichtprüfung ohne Sprung).

### D · Wirbel und weißes Loch in `ConvertPage` (ui-entwickler, tester)

- D-Vorbereitung (parallel zu A): `Views/SwirlHost.xaml(.cs)` als Weiche über `IMotionSettings` mit der heutigen XAML-Ansicht als Rückfall; `ConvertPage` bettet den Host anstelle von `SwirlHost`-Grid ein, Karten bleiben darüber; Ein-/Ausblenden von Eingang und Speicherort-Karte per Composition (`CardAnimations.FadeOut/FadeIn`, 300 ms, respektiert `IMotionSettings`); `CardAnimations.PageShake(ContentFrame, StepHeader)` mit den Stationen aus der Beben-Zeile; Tasche-Anker.
- Dann: `Rendering/SwirlRenderer.cs`, `Rendering/SwirlCanvas.xaml(.cs)`, `ConvertPageViewModel.Scene` (`SwirlScene`, Seed `Random.Shared`) mit der Kommando-Verdrahtung aus „Anbindung an den Koordinator“; `ScenePaletteReader` um die neuen Tokens ergänzt.
- Abnahme: Lauf mit 7 Dateien (davon eine mit eigenem Ziel, eine fehlschlagend): zwei Pixelströme zugleich, Farbe der Art, Fortschritt sichtbar in der Farbe; fertige Dateien werden Planeten auf eigenen Bahnen, die Ausnahme fliegt in die Tasche, der Fehler zurück in den Stapel und als Korallen-Ring aufs weiße Loch; Lauf mit 25 Dateien: 10 Bahnen mit mehreren Plätzen, leere Plätze als blasse Punkte; Pause/Weiter friert die Pixel nicht ein (sie kreisen weiter, Fortschritt steht); Fenster minimieren pausiert; Reduced Motion zeigt die heutige Ansicht; `check.sh` grün.

### E · Abschluss (ui-entwickler, reviewer)

- `Rendering/FinaleRenderer.cs` (Anlauf, Sog, Tanz, Spuren, Stille, Supernova, Wellen, Funken, Sternfeld, Ring, Häkchen); `BeginFinale` 1,05 s nach `Finished`; `ShakeRequested` → `CardAnimations.PageShake`; `ShowReport` → Berichtskarte einblenden und zentrieren; „Neue Runde“ → `SwirlClear`, Eingang und Speicherort wieder einblenden; statischer Ring für Reduced Motion (`Views/FinaleStaticView.xaml`).
- Abnahme: nach dem letzten Planeten drehen die Planeten hoch, Staub spiralt in ihrer Laufrichtung ins weiße Loch, beide Löcher kreisen im selben Drehsinn wie die Planeten um einen Mittelpunkt, der in die Mitte wandert, Verschmelzung ohne sichtbaren Sprung, kurze Stille, Supernova mit einem waagerechten Lichtstreifen, Seite und Kopfleiste beben einmal, Ring aus Planeten mit Häkchen, Bericht erscheint nach etwa 1,2 s und wird angesagt; fehlgeschlagene Datei als Korallen-Ring im Ring, Bericht „6 von 7“; Themenwechsel während des Finales führt zum Endzustand ohne Absturz; Reduced Motion: Bericht, Ring und Häkchen sofort; kein Bild über 16 ms bei 25 Dateien auf integrierter Grafik (Debug-Statistik).

## Ressourcen-Schlüssel

Keine neuen sichtbaren Texte in der Zeichenschicht; die Geister nutzen `Kind_<Kind>_Name` und die Zahl aus `TrayViewModel`, die Blätter Formatkürzel und Dateiname aus dem Plan. Neu nur für die statische Abschluss-Ansicht: `Convert_Finale_Ring_AutomationName` („Ergebnis: {0} von {1} Dateien“) und `Convert_Finale_Check_AutomationName`. Bestehende `Convert_Report_*` bleiben.

## Nicht in diesem Blatt

Universum-Symbole und gezeichnete Bahn in Schritt 2 (die Anker zeigen bis dahin auf die Artenliste und die Mitte des mittleren Bereichs), Staubringe ab 150 Dateien, Strudel und Urknall in Schritt 1, Bilddrossel bei Akkubetrieb, Einzel-Pause je Datei, Abbruch eines Übergangs durch den Nutzer.

## Offene Fragen an den Projektinhaber

Keine, die die Umsetzung blockieren. Die Zahl 24 (Deckel der fliegenden Blätter) und die Funken beim Auflösen des Lochs sind eigene Festlegungen ohne Vorbild im Entwurf und werden nach der ersten Sichtprüfung angepasst.
