# Arbeitsblatt „Abgleich mit dem Mischentwurf“ (Optik Seite für Seite) für den UI-Entwickler

Stand 2026-09-25. Verbindliche Grundlage: `docs/03-architektur.md` (ADR-017 eigene Farben, ADR-018/022/023 Zeichenschicht), `docs/06-design.md` (Token-Tabelle, Abweichungslisten der Etappen), `design/ENTSCHEIDUNGEN.md`. Quelle der Maße ist ausschließlich der CSS-Teil von `design/oberflaeche-mischentwurf.html`: Haupt-`<style>` Zeilen 5–715 (Tokens, `.win`, `.tb`, `.faden`, Schritt 3 `.w5-*`, Verlauf `.verlauf`/`.rk`/`.vd`, Bausteine `.btn`/`.taste`/`.link`/`.ikon`/`.etikett`), Template `#gal-stil` Zeilen 869–1247 (Schritt 1: `.einwurf`, `.fach*`, `.fz`, `.fc`, `.abweis`, Zoom `.zurueck`, `.seite-l/-r`, `.knopf-l/-r`) und Template `#ziel-stil` Zeilen 1248–2276 (Schritt 2: `.z-*`, `.karte-e`, `.art-kopf`, `.e5-*`, `.gleiste`, `.zielfeld`, `.schalter`). Die JS-Teile ab Zeile 2277 sind keine Quelle.

Dieses Blatt gleicht **nur die Optik** an: Maße, Abstände, Radien, Schrift, Farben, Schatten, Glas, Schilder, Knöpfe, Karten, Listen, Ausklapper, Schalter. Aufbau, Ablauf, ViewModels, Zeichenschicht und Übergänge bleiben, wie sie sind. Zwei Entwickler arbeiten parallel an Übergängen (`Rendering/TransitionOverlay`, `MainWindow.xaml`, `Services/TransitionService`) und am Wirbel (`Views/SwirlHost`, `Views/ConvertPage.xaml` Mitte, `Scenes/Swirl*`); siehe Abschnitt „Reihenfolge und Zusammenarbeit“.

## 1. Grundsätze für die ganze Etappe

### 1.1 Maßstab und Fensterbreite

Der Entwurf ist eine Seite von 1440 px Breite; das Fenster `.win` darin ist `min-width: 1060px`, `height: 900px`, Raster `42px` Titelleiste, `62px` Schrittleiste, Inhalt, Fußleiste (`auto`); Grundschrift im Fenster `13px`, außerhalb `15px/1.55`. Ein CSS-Pixel des Entwurfs ist ein DIP in XAML (Skalierung 100 %). Regeln:

| Fensterbreite (Client) | Regel |
|---|---|
| ≥ 1060 px | Alle Maße 1:1. Keine Streckung von Schrift oder Radien. Seitliche Spalten mit fester Breite (Schritt 2 links 236, rechts 286; Zoom 250; Schritt 3 rechts `min(300px, 30 %)`), die Mitte nimmt den Rest. |
| 900–1059 px | Feste Seitenspalten schrumpfen linear bis auf 85 % (Schritt 2: 200/243, Zoom: 212), Abstände von 16 auf 12, Kartenpolster von 10 auf 8. Schriftgrößen bleiben. |
| 640–899 px | Die heutigen schmalen Aufbauten bleiben (`MainPage` Code-behind: fünf Fächer à 200 px mit seitlichem Rollen; `TargetPage`/`ConvertPage` `AdaptiveTrigger` stapeln die Spalten). Nur Tokens und Vorlagen gelten weiter. |
| Höhe < 720 px | Galaxie auf Mindesthöhe 240; Schritt 3 Bühne von 300 auf 220. |

Die Umsetzung legt die Zahlen als `x:Double`-Ressourcen in `Themes/KvertisControls.xaml` ab (`KvSideColumnWidth`, `KvGutter`, `KvCardPadding`) und schaltet sie im Code-behind der Seite, nicht in `VisualState`-Settern auf `ColumnDefinition` (Compiler-Absturz, siehe Abweichungsliste Schritt 1 in `06-design.md`).

### 1.2 Schrift

Geist entfällt (nicht freigegeben, `ENTSCHEIDUNGEN.md` „Offen“). Alle Textstellen: **Segoe UI Variable** (`FontFamily="Segoe UI Variable Text"`, ab 20 px `Segoe UI Variable Display`), Gewichte des Entwurfs 1:1: 400 `Normal`, 500 `Medium`, 600 `SemiBold`, 700 `Bold`. Mono-Stellen (Zähler, Zeiten, Größen, Pfade, Format-Chips, Kleinüberschriften mit Sperrung): **Cascadia Mono**, sonst Consolas: eine Ressource `KvMonoFontFamily` = `"Cascadia Mono, Consolas"` (XAML-Fallback-Liste), nirgends ein zweiter Schriftname im Markup. Der vorhandene `FontFamily="Consolas"` in `LicensesPage.xaml` wird auf die Ressource umgestellt.

Die WinUI-Textstufen werden nicht mehr direkt benutzt, sondern eigene `TextBlock`-Stile mit den Werten des Entwurfs (alle in `KvertisControls.xaml`):

| Stil | Entwurf | Größe/Gewicht | Farbe |
|---|---|---|---|
| `KvTitleTextStyle` | `.v-kopf h4`, `.z-titel b`, `.z-frage h3` | 17 / SemiBold | `KvInk` |
| `KvHeadTextStyle` | `.fach-kopf b`, `.rk-titel b`, `.w-titel` etc. | 13,5 / SemiBold | `KvInk` |
| `KvBodyTextStyle` | Fenster-Grundschrift | 13 / Normal, Zeilenhöhe 1,45 | `KvInk` |
| `KvRowTextStyle` | `.w5-nm b`, `.wo b`, `.w5-opt b` | 12,5 / SemiBold | `KvInk` |
| `KvSmallTextStyle` | `.einwurf small`, `.w5-akt`, `.abweis` | 11,5 / Normal | `KvMuted` |
| `KvMonoTextStyle` | `.w5-pf`, `.fz .nm small`, `.vd .dn small` | 10,5 / Medium, `KvMonoFontFamily` | `KvMuted` |
| `KvLabelTextStyle` | `.label`, `.w5-ort small`, `.tag-titel`, `.ueber` | 10,5 / Medium, Mono, `CharacterSpacing="100"` (≈ .1em), `Text` in Großbuchstaben aus der Ressource | `KvQuiet` |
| `KvCountTextStyle` | `.fach-kopf em` | 20 / Bold, Mono, Zeilenhöhe 1 | Artfarbe |
| `KvBigNumberTextStyle` | `.e5-gross .ring b`, `.zielfeld input` | 26 bzw. 17 / Bold, Mono | `KvInk` |

`CharacterSpacing` in XAML ist in 1/1000 em: `.08em` → 80, `.1em` → 100, `.12em` → 120, `-.01em` → −10.

### 1.3 Farben, fehlende Tokens

Alle Werte aus `Themes/KvertisColors.xaml`. Neu anzulegen (Werte aus der Token-Tabelle in `06-design.md`, Hoher Kontrast wie dort auf Systemfarben):

| Neuer Schlüssel | Token | Dunkel | Hell | Hoher Kontrast |
|---|---|---|---|---|
| `KvQuiet` | `leise` | `#7f8a91` | `#5b646a` | `SystemColorGrayTextColor` |
| `KvHover` | `hover` | `#08ffffff` | `#0a000000` | `SystemColorHighlightColor`, Opacity 0,2 |
| `KvVideoFrame` | `amber-rahmen` | `#5e4722` | `#e0c692` | `SystemColorWindowTextColor` |
| `KvErrorFrame` | `coral-rahmen` | `#5e2f29` | `#e8b5ac` | `SystemColorWindowTextColor` |
| `KvEdge` | `karton-kante` | `#0dffffff` | `#14000000` | transparent |
| `KvPaperInk` | `paper-ink` | `#23272a` | `#23272a` | `SystemColorWindowTextColor` |

Der Entwurf bildet außerdem viele Mischfarben mit `color-mix(in srgb, <token> X%, <grund>)`. Das wird **nicht** je Stelle im Code nachgerechnet, sondern als feste Pinsel angelegt, jeweils in Dunkel und Hell:

| Schlüssel | Entwurf | Verwendung |
|---|---|---|
| `KvPanel30Brush`, `KvPanel55Brush`, `KvPanel70Brush`, `KvPanel80Brush`, `KvPanel85Brush` | `color-mix(panel X%, transparent)` | Leisten, Listenhintergrund, Zeilen über der Zeichenfläche |
| `KvBg70Brush`, `KvBg78Brush`, `KvBg80Brush`, `KvBg82Brush` | `color-mix(bg X%, transparent)` | Einwurf-Karte, Fächer, Zurück-Pille, Karten in Schritt 2 |
| `Kv<Art>Tint12Brush`, `Kv<Art>Frame40Brush`, `Kv<Art>Frame60Brush` je Art (Image, Audio, Video, Document, Model) | `color-mix(k 12%, panel)`, `color-mix(k 40/60%, transparent)` | Format-Chips, gewählte Zeile, Fachrahmen |
| `KvMintDark45Brush` | `color-mix(mint 45%, #000)` | Sockel des Primärknopfs |
| `KvCardGradientBrush` | `linear-gradient(180deg, panel2, panel)` | Karten Verlauf, Zeilen Schritt 2 |

Diese Pinsel gehören in `KvertisColors.xaml` (sie sind Farben), die Stile in `KvertisControls.xaml`. Alle sind `SolidColorBrush` mit `Opacity` bzw. `LinearGradientBrush`; keine Pinsel im Code, keine Konverter.

### 1.4 Glas-Material

`--glas` ist im Entwurf zwar als Token definiert (`#14181bcc` / `#ffffffcc`), wird aber **an keiner Stelle des CSS verwendet**. Halbdurchsichtige Flächen entstehen im Entwurf über `color-mix(panel|bg X%, transparent)`; `backdrop-filter: blur(4–8px)` gibt es nur bei Flächen **über der Zeichenfläche**: `.einwurf` (4 px), `.fach`, `.wahl`, `.meldung`, `.planet-tip` (8 px), `.karte-e` (6 px). Entscheidung:

- **Mica** bleibt Fensterhintergrund (`MainWindow`, `docs/06-design.md`). Der Entwurf hat statt Mica `--bg` mit zwei sehr schwachen Radialverläufen (Mint 9 % oben links, Violett 7 % unten rechts); das wird nicht nachgebaut. Bewusste Abweichung, weil Mica der Windows-Look der App ist und die Verläufe unter 10 % Deckung liegen.
- **Acrylic** (`AcrylicBrush`, in-app) wird **nicht** eingesetzt. Innerhalb des Fensters kostet In-App-Acrylic je Fläche eine Kompositionsschicht, die Fächer sind fünf davon über einer laufenden `CanvasAnimatedControl`-Fläche. Flyouts und Menüs behalten das Standard-Acrylic von WinUI (Systemverhalten).
- Der Blur der Karten über der Galaxie wird durch **8 Prozentpunkte mehr Deckung** ersetzt: `.fach` `bg 78 %` + Blur → `KvBg86Brush`-Deckung; `.einwurf` 70 % → 78 %; `.karte-e` 82 % → 90 %. Ohne Blur muss die Fläche etwas dichter sein, damit die Bahnen dahinter nicht in die Schrift laufen.
- Der heutige `KvGlasBrush` (0,8 Deckung) bleibt als Schlüssel bestehen und wird für die Karte „Nicht umwandelbar“ und die Einwurf-Karte durch die oben genannten `KvBg*`-Pinsel ersetzt; wo er sonst noch steht, bleibt er.

### 1.5 Schatten

Der Entwurf setzt farbige, versetzte Schatten (`0 4px 0 <dunkles Mint>` Sockel, `0 12px 24px -8px` Glühen, `0 8px 18px -14px var(--schatten)` Karten, `0 26px 50px -18px` Menü). `ThemeShadow` in WinUI ist grau, ungefärbt und nicht versetzbar. Entscheidung:

- **Sockel** des Primärknopfs (`.taste`, `0 4px 0 …`): Teil der Vorlage, als zweiter `Border` 4 px unter dem Knopf in `KvMintDark45Brush` (Abschnitt 2.1). Gedrückt: Sockel 1 px, Knopf rutscht 3 px nach unten (`RenderTransform`), ohne Storyboard-Dauer über 120 ms.
- **Karten** (`.rk`, `.zrow`): `ThemeShadow` mit `Translation="0,0,8"`, nur auf Verlauf-Karten und dem Speicherort-Menü; keine Schatten auf Zeilen in Listen (`.w5-zeile` hat keinen), keine auf Fächern. Im Hohen Kontrast entfallen Schatten (Vorlage prüft `KvShadowDepth`-Ressource = 0 im `HighContrast`-Wörterbuch).
- **Glühen** (`box-shadow: 0 0 8px var(--k)` am Farbpunkt, `0 0 14px var(--mint)` am Griff, `0 0 12px` an Chips): ein zweiter, größerer Kreis mit 25 % Deckung hinter dem Element (wie der Renderer es für Bahnen macht, ADR-022); kein Blur-Effekt.
- Innere Kante `inset 0 1px 0 var(--karton-kante)`: oberer `BorderThickness="0,1,0,0"` in `KvEdgeBrush` auf einem inneren `Border`; nur bei Karten mit Verlauf.

### 1.6 Fokus, Hover, Hoher Kontrast, Barrierefreiheit

- Fokusring des Entwurfs (`outline: 2px solid var(--mint); outline-offset: 2px`): global über die Ressourcen `FocusVisualPrimaryBrush` = `KvMintBrush`, `FocusVisualSecondaryBrush` = `KvBackgroundBrush`, `FocusVisualPrimaryThickness` 2, `FocusVisualMargin` −2 in `Default` und `Light`. **Keine** eigene Fokusdarstellung in Vorlagen, `UseSystemFocusVisuals="True"` überall. Im Hohen Kontrast bleiben die Systemwerte.
- Hover (`var(--hover)`): `KvHoverBrush` als `ButtonBackgroundPointerOver` für Ghost- und Icon-Knöpfe; alle anderen Hover-Zustände sind Randfarbwechsel (`line` → `line-stark`, `line` → `mint`).
- Hoher Kontrast: Jede eigene Vorlage nutzt nur `{ThemeResource Kv*}`-Pinsel, die im `HighContrast`-Wörterbuch auf Systemfarben liegen; Ränder bleiben 1 px sichtbar (kein `BorderThickness="0"` in Vorlagen außer bei Ghost-Knöpfen, die dort einen 1-px-Rand in `SystemColorButtonTextColor` bekommen). Schatten, Sockel und Glühen sind aus. Halbdurchsichtige Pinsel liegen dort auf `SystemColorWindowColor` mit Deckung 1.
- Mindestklickziel 32 × 32 bleibt (docs/06): Icon-Knopf 32 (Entwurf `.ikon` 32), Verlauf-Rundknopf `.vk.rund` 30 → **32** (bewusste Abweichung), Entfernen-Kreuz `.fz-weg` 18 → **24 Sichtfläche, 32 Klickfläche** (Polster). Chips und Schilder sind nie Klickziele.
- Bewegung: Übergänge der Vorlagen (Hover 0,2 s, Knauf 0,2 s `--feder`, Chevron 0,3 s) nur bei `IMotionSettings.AnimationsEnabled`; sonst sofort.

## 2. Eigene Vorlagen und Standard-Elemente

### 2.1 Neue Datei `Themes/KvertisControls.xaml`

Eingehängt in `App.xaml` nach `KvertisColors.xaml`. Enthält (a) Ressourcen-Überschreibungen, mit denen Standard-Elemente den Entwurf tragen, ohne Vorlage, (b) Stile ohne Vorlage, (c) fünf `ControlTemplate`s.

**(a) Überschreibungen (Standard-Element bleibt, nur Werte):**

| Element | Ressourcen | Entwurf |
|---|---|---|
| Alle `Button` (Sekundärknopf `.btn`) | `ButtonBackground` `KvPanel2`, `ButtonBorderBrush` `KvLine`, `…PointerOver` Rand `KvLineStrong`, `…Pressed` Hintergrund `KvPanel`, `ControlCornerRadius` 9, `ButtonPadding` 12,7, Schrift 12,5 / Medium | `.btn { border 1px line; bg panel2; radius 9; padding 7px 12px; 500 12.5px }` |
| `HyperlinkButton` (`.link`) | Vordergrund `KvMint`, Hover-Hintergrund `KvMintSurface`, Radius 8, Polster 8,6, 12,5 / Medium, keine Unterstreichung | `.link` |
| `TextBox`, `NumberBox`, `AutoSuggestBox` (`.eingabe`) | `TextControlBackground` `KvPanel2`, `TextControlBorderBrush` `KvLine`, Fokus-Rand `KvMint` 1 px (nicht 2 px unten wie WinUI: `TextControlBorderThemeThicknessFocused` 1), Radius 9, Polster 10,7, 12,5 Mono | `.eingabe` |
| `ComboBox` (`.ueb-wahl select`) | Hintergrund `KvPanel`, Rand `KvLineStrong`, Radius 8, Polster 8,6, 13 | `select` |
| `ToggleSwitch` | Farben sind bereits Mint; Stil `KvSwitchStyle`: `OnContent`/`OffContent` leer, `MinWidth` 0; Maße bleiben WinUI (40 × 20; Entwurf 38 × 22 bzw. 30 × 17) | `.toggle`, `.schalter i` |
| `Slider` | Standard; Spur `KvDeep` (`SliderTrackFill`), Wert Mint (vorhanden), Griff 20 px | `input[type=range]` mit `accent-color` |
| `Expander` | `ExpanderHeaderBackground` `KvPanel85`, `ExpanderHeaderBorderBrush` `KvLine`, `ExpanderContentBackground` transparent, `ExpanderContentBorderBrush` transparent, `ExpanderMinHeight` 34 (WinUI 48), Radius 11, Chevron 10 px in `KvMuted` | `.e5-z > button` |
| `FlyoutPresenter` (`.w5-menue`) | Stil `KvMenuFlyoutPresenterStyle`: Hintergrund `KvPanel`, Rand `KvLineStrong`, Radius 13, Polster 8, `ThemeShadow` | `.w5-menue` |
| `ListViewItem` | Auswahlbalken Mint (vorhanden), Hover `KvHover`, Radius 8, `MinHeight` 32 | – |
| `ProgressBar` | Höhe 6 statt 3, Spur `KvDeep`, Radius 4 (`ProgressBarTrackFill`, `ProgressBarMinHeight`) | `.bal` |
| `ToolTip` | Standard | kein Tooltip im Entwurf |
| `CommunityToolkit SettingsCard` | `SettingsCardBackground` `KvBg80`, Rand `KvLine`, Radius 12, Polster 10,9, `SettingsCardMinHeight` 44 | `.einst-k` |

**(b) Stile ohne Vorlage:**

`KvIconButtonStyle` (Button 32 × 32, transparent, Rand 0, Radius 8, Vordergrund `KvMuted`, Hover `KvHover` + `KvInk`; ersetzt die vier handgesetzten Icon-Knöpfe in `MainWindow`, `HistoryPanel`, `TrayFileControl`, `GalaxyHost`), `KvOutlineMintButtonStyle` (`.demo-knopf`/`.vk.haupt`/`.w5-akt.haupt`: Rand `KvMintFrame`, Hintergrund `KvMintSurface`, Text Mint, Hover Rand Mint), `KvSmallButtonStyle` (`.w5-akt`/`.vk`: Polster 9,5, 11,5 / Medium, Radius 8), `KvCardStyle` für `Border` (ersetzt `CardBorderStyle`: Radius 13, Rand `KvLine`, Hintergrund `KvCardGradientBrush`, Polster 12; `CardBorderStyle` bleibt als Alias auf denselben Werten, damit keine Seite bricht), `KvGlassCardStyle` (Radius 12–14, Rand `KvLine`, Hintergrund `KvBg80`), `KvChipStyle` für `Border` + `KvChipTextStyle` (Format-Chip `.fc`: Polster 7,2, Radius 6, 10,5 Mono Bold; Farbe je Art über `Kv<Art>Tint12`/`Frame40`), `KvPillStyle` (`.ersparnis`/`.zuletzt button`: Radius 999, Polster 8,3, 12 Mono SemiBold), `KvProBadgeStyle` (`.pro`: Rand `KvVideoFrame`, Text `KvVideo`, 10,5 Mono SemiBold, Sperrung 80, Polster 7,2, Radius 6), `KvPaperTagStyle` (`.etikett`: `KvPaper`/`KvPaperInk`, Radius 3, Polster 8,3, 11 Mono SemiBold, `RenderTransform` Drehung −1,2°, unten 2 px `#0003`), `KvRowStyle` für `Border` (`.w5-zeile`/`.vd`: Polster 12,4, Radius 11, Rand `KvLine`, Hintergrund `KvPanel`), `KvBottomBarStyle` (`.leiste`: Polster 16,10 bzw. 18,12, Rand oben `KvLine`, Hintergrund `KvPanel70`), `KvDashedBoxStyle` (`.abweis`, `.w5-tasche`, `.damals-hinweis`: gestrichelter Rand über `Rectangle` mit `StrokeDashArray="3,3"` im Hintergrund eines `Grid`, weil `Border` keine Strichelung kann).

**(c) Fünf `ControlTemplate`s (nur hier ist eine Vorlage nötig):**

1. `KvPrimaryButtonStyle` (`.taste`): Mint-Fläche, Radius 12, Polster 20,11, 14 / SemiBold, `KvOnMint`, `MinWidth` 190 (die Schritt-Blöcke nennen 18,10 / 13,5 / 190; die Fußleiste des Hauptfensters 20,11 / 14; es gilt die größere); 4-px-Sockel darunter (Abschnitt 1.5); Hover hebt um 1 px, gedrückt senkt um 3 px; deaktiviert Deckung 0,4 ohne Sockel. Ersetzt `AccentButtonStyle` an allen Stellen (Weiter: Ziel, Weiter: Umwandeln, Umwandeln · n Dateien, Neue Runde, Pro kaufen).
2. `KvExpanderStyle` (`.e5-z`, `.e5-tuer`): Kopf als Zeile `82 | 1fr | 12`: Name 12, Kleinzeile 10,5 `KvMuted`, Balken `.bal` 6 px im Kopf, Chevron ▾ 10 px rechts; geöffnet Rand `KvMintFrame`, Inhalt mit oberer Trennlinie `KvLine` und Polster oben 10. Die Standardvorlage von `Expander` trägt den Kopf in einem `ToggleButton` mit festem 48-px-Raster und Chevron 12 px links/rechts, das lässt sich nicht allein mit Ressourcen auf 34 px, Balken im Kopf und Chevron rechts bringen.
3. `KvSizeSliderStyle` (`.gleiste`): Spur 12 px hoch, Radius 6, 24 Farbsegmente aus `ItemsControl` (heute schon), Griff 28 px Kreis, Fläche `KvBackground`, Rand 2 px Mint, Glühen (zweiter Kreis 44 px, Mint 25 %); Marken oben 9,5 Mono SemiBold mit 1-px-Strich; Skala unten 10 Mono `KvQuiet`. Zusammen mit dem vorhandenen `GradeRingSliderStyle` aus `TargetPage.xaml` (zieht nach `KvertisControls.xaml`, Ring 92 px, Bogen 8 px, Zahl 26 Mono Bold, Wort 9,5 Mono) und dem Ringgriff `.ring-griff`/`.e5-rgriff` (28 bzw. 46 px, Ring als Griff mit Zahl).
4. `KvSegmentStyle` für `RadioButton` (`.segment`, Filter im Verlauf und „Alle gleich / Jede einzeln“ in Schritt 2): Behälter `KvDeep`, Rand `KvLine`, Radius 10, Polster 3; Knopf Polster 10,5, Radius 7, 12, `KvMuted`; gewählt `KvPanel2` + `KvInk` + innere Kante. Die WinUI-`RadioButton`-Vorlage hat einen Kreis links, der hier entfällt.
5. `KvOptionStyle` für `RadioButton` (`.wo`, `.w5-opt`: Speicherort-Auswahl und Ziel je Zeile): Zeile `16 | 1fr`, Polster 8,7, Radius 9–10, Rand 1 px transparent, Hover `KvHover`, gewählt `KvMintSurface` + Rand `KvMintFrame`; Kreis 16 px, Rand 1,5 `KvLineStrong` → Mint mit 10-px-Punkt; Titel 12,5 SemiBold, Pfad 10,5–11 Mono `KvMuted`; Variante „Ordner wählen…“ mit gestricheltem Quadrat (Radius 4).

**Bewusst Standard bleiben** (Barrierefreiheit, Hoher Kontrast, Tastatur, Bildschirmleser): `ComboBox`, `NumberBox` (mit Spin-Knöpfen), `TextBox`, `ToggleSwitch`, `CheckBox`, `Slider` außer den beiden oben, `ScrollViewer`/`ScrollBar` (Entwurf `scrollbar-width: thin` = WinUI-Standard mit schmalem Ruhezustand), `ListView` (nur Ressourcen), `InfoBar`, `ProgressRing`, `ToolTip`, `ContentDialog`, `MenuFlyout`, `DropDownButton` (Pfeil bleibt), `SplitView`, Titelleisten-Schaltflächen des Systems. Der `RadioButton` in seiner Standardform bleibt für Gruppen mit sichtbarem Kreis in Flyouts, wo keine der beiden Varianten passt.

## 3. Tabellen je Seite

Spalten: Element | Entwurf (CSS) | Heute in XAML | Änderung. „Standard-WinUI“ heißt: keine eigene Angabe, WinUI-Voreinstellung.

### 3.1 Rahmen und Schrittleiste (`MainWindow.xaml`, `Views/StepHeader.xaml`, `App.xaml`)

| Element | Entwurf | Heute in XAML | Änderung |
|---|---|---|---|
| Titelleiste `.tb` | Höhe 42; Polster links 14; Abstand 10; Rand unten 1 `line`; Hintergrund `panel` 55 % | Zeile 48; `AppTitleBar` Polster 12; kein Rand, Mica | Höhe bleibt 48 (Systemmaß der Titelleisten-Schaltflächen), Rand unten `KvLine` 1 px, Hintergrund `KvPanel55Brush` über Mica |
| Logo `.logo` | 20 × 20, Mint-Quadrat 13 Radius 4 + Kreis | `Image` 18 × 18 | 20 × 20 |
| Titel `.tb b` | 13 / SemiBold | `CaptionTextBlockStyle` (12 / Normal) | `KvHeadTextStyle` 13 / SemiBold |
| Icon-Knöpfe `.ikon` (Verlauf, Einstellungen) | 32 × 32, Radius 8, Rand 0, `muted`, Hover `hover` + `ink`, Symbol 16 | Höhe 32, Breite auto, transparent, Symbol 16 | `KvIconButtonStyle`; Abstand 4 bleibt |
| Pro-Schild `.pro` | 10,5 Mono SemiBold, Sperrung 80, Polster 7,2, Radius 6, Rand `amber-rahmen`, Text `amber` | Knopf mit Symbol + Caption-Text | Knopf bleibt (klickbar), Inhalt ist das Schild `KvProBadgeStyle` ohne Symbol; Text „PRO“ aus Ressource |
| Zurück-Knopf | kein Gegenstück (Entwurf navigiert über den Faden) | 40 × 32 links | bleibt, `KvIconButtonStyle` |
| Schrittleiste `.faden` | Höhe 62; Polster 0,22; Rand unten `line`; Hintergrund `panel` 35 %; Inhalt linksbündig, Segmente füllen die Breite | Rand unten `KvLine`; Polster 24,0,24,10; Inhalt zentriert, Verbinder fest 32 px | Höhe 62 fest, Polster 0,22, `KvPanel35Brush`; Knoten links, Segmente `*`-Spalten |
| Knoten `.knoten` | Raster `28px auto`, Abstand 0,10, Polster 7,12,7,8, Radius 10, Hover `hover`, deaktiviert Deckung 0,5 | `Button` Polster 10,6, Radius 6, Deckung 0,7 für kommende | Werte des Entwurfs; Deckung kommender Schritte 0,5 nur wenn deaktiviert, sonst 1 |
| Kennzeichen `.knoten i` | 28 × 28 **Quadrat** Radius 9, Zahl 12 Mono SemiBold, Rand 1 `line-stark`, Text `muted`; aktiv: Mint-Fläche, Text `auf-mint`, Sockel `0 3px 0` dunkles Mint; fertig: Rand `mint-rahmen`, Text Mint (Häkchen) | Kreis 24, Radius 12, Häkchen 12 / Punkt 8 | 28 × 28, Radius 9, Ziffer 1/2/3 statt Punkt; fertig: Häkchen in Mint auf `KvMintSurface`; aktiv: Sockel 3 px `KvMintDark45` (Hoher Kontrast: ohne Sockel) |
| Beschriftung `.knoten b` / `small` | Titel 13 / SemiBold; darunter Kleinzeile 11 Mono Medium `leise` (z. B. „3 Dateien“), fertig in Mint | ein `TextBlock` Body 14 | zwei Zeilen; die Kleinzeile kommt aus dem vorhandenen `AutomationProperties.ItemStatus`-Text (derselbe Text, jetzt sichtbar) |
| Segment `.seg` | `flex: 1`, Höhe 2, Rand 0,6, Radius 1, `line-stark`; Füllung Mint als `scaleX` 0→1 in 0,7 s `--sanft` | `Rectangle` 32 × 1 `KvLine` | Höhe 2, `KvLineStrong`, Breite `*`; Füll-`Rectangle` in Mint mit `ScaleTransform` (Composition, 0,7 s, aus bei ruhiger Ansicht) |
| Fenster `.win` | Radius 14, Rand `line-stark`, Schatten `0 40px 80px -40px` | Systemfenster | keine Änderung (Systemrahmen) |
| Drop-Overlay `.win.drag::after` | `inset 8px`, Radius 12, Rand 2 gestrichelt Mint, Fläche `mint-flaeche` 80 %, Text 18 / SemiBold Mint „Loslassen – Kvertis sortiert selbst“ | kein Overlay (Galaxie reagiert auf `dragOver`) | neu in `MainPage` über dem Inhalt (nicht in `MainWindow`, siehe Zusammenarbeit); Text aus Ressource `Main_DropOverlay_Text` |

### 3.2 Schritt 1 „Hineinwerfen“ (`Views/MainPage.xaml`, `GalaxyHost.xaml`, `TrayControl.xaml`, `TrayFileControl.xaml`)

| Element | Entwurf | Heute in XAML | Änderung |
|---|---|---|---|
| Seitenpolster | Fächer `left/right 16`, `bottom 12`; Zeichenfläche randlos | Inhalt Polster 24,8,24,0; Zeilenabstand 12 | Polster 16,0,16,0; Galaxie randlos über die ganze Breite; Fächer-Zeile Abstand 8 |
| Einwurf-Karte `.einwurf` | Zeile: Symbol, Text, Knöpfe; Abstand 12; Polster 9,12,9,14; Radius 14; Rand 1,5 **gestrichelt** `line-stark`; Hintergrund `bg` 70 % + Blur 4; Titel 13, Kleinzeile 11,5 `muted`; beim Ziehen Rand Mint + Ring 4 px `mint-flaeche`; Variante `.klar` ohne Rahmen, zentriert | `EntryCard` Polster 16,10, Radius 8, `KvGlas`, Rand `KvLine` durchgezogen; Stapel senkrecht | `KvDashedBoxStyle`, Radius 14, `KvBg78Brush`, Zeile waagerecht mit Abstand 12; Zieh-Zustand über `VisualState` (Rand Mint, Ring als äußerer `Border` 4 px `KvMintSurface`) |
| Einwurf-Zeile `EntryBar` (nach dem ersten Einwurf) | `.einwurf.klar`: ohne Rahmen, transparent, zentriert, Abstand 2 | Polster 10,4, Radius 6, `KvGlas`, Rand `KvLine` | ohne Rahmen und Hintergrund, Text 11,5 `KvMuted`, Knöpfe `KvSmallButtonStyle` |
| Nicht umwandelbar `.abweis` | rechts 16, oben 74 (im Zoom) / unten 12; max. 250; Polster 8,12; Radius 12; Rand 1 **gestrichelt** `coral` 45 % auf `line`; Hintergrund `bg` 75 %; Text 11,5 `muted`; Titel 12 / SemiBold `coral` | `MaxWidth` 300, Rand 0,8,8,0, Polster 12,8, Radius 8, Rand `KvError` durchgezogen, `KvGlas` | 250 breit, gestrichelt `KvErrorFrame`, `KvBg80Brush`, Radius 12, Titel `KvError` 12 / SemiBold; Entfernen-Knopf `KvIconButtonStyle` 24 sichtbar |
| Summe oben links `.summe` | links 16, oben 16; 12,5 `muted`; Zahl 15 / SemiBold `ink`; Fehler `coral` | kein Gegenstück (Zahl steht im Fach) | neu: „n Dateien · m nicht umwandelbar“ links oben in der Galaxie, aus `MainViewModel` (vorhandene Zähler) |
| Fächer-Zeile `.faecher` | Höhe 188 (Zoom 52), 5 gleiche Spalten, Abstand 10 | `TrayRow` `3*`, Spaltenabstand 8 | feste Höhe 188 bei Fensterhöhe ≥ 720, sonst `MinHeight` 150; Abstand 10; Galaxie bekommt den Rest (statt 2*/3*) |
| Fach `.fach` | Polster 9,10,8; Radius 14; Rand 1 `line`; **oberer Rand 3 px** in Artfarbe 70 % (leer 30 %); Hintergrund `bg` 78 % + Blur 8; Abstand 6; Hover Rand Art 55 %, oben Art 100 %, Ring 1 px Art 25 %; gewählt (Zoom) Hintergrund Art 12 %, Glühen; Zoom: Polster 8,10, nur Kopf | `Grid` Polster 8, Radius 8, Kartenpinsel, Rand 1, Abstand 6; Zoom `MinHeight` 52 | Radius 14, `KvBg86Brush`, `BorderThickness="1,3,1,1"` mit `BorderBrush` aus `Kv<Art>Frame60`… oberer Rand braucht eigene Farbe: äußerer `Border` (oben 3 px Art) um inneren `Border` (1 px `KvLine`); Hover/gewählt als `VisualState` |
| Fachkopf `.fach-kopf` | Abstand 7; Punkt 9 mit Glühen `0 0 8px` Art; Name 13,5 / SemiBold; Zahl 20 Mono Bold Art, leer 16 `leise` | `ToggleButton` 36, Polster 6,2; Punkt 10; Name BodyStrong 14; Zahl Subtitle 20 / Caption 12 | Punkt 9 + Glühkreis 17 (Art 25 %); Name `KvHeadTextStyle`; Zahl `KvCountTextStyle`, leer 16 Mono `KvQuiet`; `ToggleButton` ohne sichtbaren Rahmen (`KvIconButtonStyle`-Ableitung, Stretch) |
| Dateizeile `.fz` | Raster `22 | 1fr | auto | auto`, Abstand 7, Polster 3,6,3,3, Radius 8, Rand 1 transparent, Hintergrund `panel` 80 %, Schrift 12; Hover Rand Art 60 %, Hintergrund Art 12 %; Name SemiBold, Kleinzeile 10 Mono `muted` (Warnung `amber`); Chip 9,5 / Polster 5,1; Entfernen `.fz-weg` 18 × 18 nur bei Hover/Fokus, Hover `coral` 18 % | `Grid` Polster 4, Abstand 8; Miniatur 48 × 48 Radius 6; Name BodyStrong; Größe Caption; Schild Polster 6,1 Radius 4 Rand `KvCardStroke`; Entfernen 32 immer sichtbar | Zeile 28 hoch, Radius 8, `KvPanel80Brush`, Hover-Zustand; Miniatur/Symbol **24 × 24** Radius 4 (Entwurf 22; docs-Blatt Schritt 1 nannte 48, das ist im Entwurf nicht so); Name `KvRowTextStyle` 12 SemiBold; Kleinzeile `KvMonoTextStyle` 10; Chip `KvChipStyle` klein; Entfernen 24 sichtbar / 32 Klickfläche, Deckung 0 bis Hover oder Fokus, im Hohen Kontrast und bei Tastaturfokus immer sichtbar |
| Format-Chip `.fc` | 10,5 Mono Bold, Polster 7,2, Radius 6, Text Art, Fläche Art 12 % auf `panel`, Rand Art 42 %; `.m` Mint-Variante; `.da` (eingeworfen) Fläche Art voll, Text `#0b0f12`, Glühen 12 | `Border` Polster 6,1 Radius 4 Rand `KvCardStroke`, Caption | `KvChipStyle`/`KvChipTextStyle`, Varianten `Mint`, `Filled` |
| Leeres Fach `.fach-leer` | 11,5 `leise`, Abstand 5; Chips Deckung 0,75, 9,5, Polster 5,1 | zwei Caption-Zeilen `KvMuted` | 11,5 `KvQuiet`; Beispiel-Chips als `KvChipStyle` klein mit Deckung 0,75 (statt Text) |
| Fachfuß `.fach-fuss` | 10 Mono `muted`, Pfeil Mint, Zahlen `ink` SemiBold, einzeilig, abgeschnitten | Caption `KvMuted`, umbrechend | `KvMonoTextStyle` 10, `Run`s: Zahl `KvInk` SemiBold, „→“ Mint; `TextTrimming` |
| Fußleiste `.leiste` | Polster 10,16; Abstand 10; Rand oben `line`; Hintergrund `panel` 70 %; Tipp 12 `muted`; Vertrauen 12 `muted`, Symbol 14 Mint | Rand −24,0, Polster 24,12, `LayerFill`, `DividerStroke` | `KvBottomBarStyle`; Vertrauens-Symbol Mint 14; Text 12 |
| Weiter-Knopf | `.taste` (14 / SemiBold, Polster 20,11, Radius 12, Sockel) | `AccentButtonStyle` MinWidth 160, MinHeight 40 | `KvPrimaryButtonStyle`, MinWidth 160 |
| Zieh-Overlay | siehe 3.1 `.win.drag::after` | – | neu |

### 3.3 Zoom-Wege (`Views/PathsOverlay.xaml`, Fächer im Zoom)

| Element | Entwurf | Heute in XAML | Änderung |
|---|---|---|---|
| Zurück `.zurueck` | links 16, oben 14; Pille Radius 999; Rand `line-stark`; Hintergrund `bg` 80 %; Polster 6,12; 12,5 `ink`; Punkt 9 mit Mint-Rand 1,5 | Standard-Button oben links, Polster 12 | eigener Stil `KvPillButtonStyle` (Button, Radius 999, Polster 12,6, Rand `KvLineStrong`, `KvBg80Brush`); Symbol = Kreis 9 px mit Mint-Rand |
| Titel `.z-titel` | mittig oben 10; Titel 17 / SemiBold; Kleinzeile 11,5 Mono `muted`; Zusatz 11 Mono Mint | Subtitle 20 + Caption | `KvTitleTextStyle` 17; Kleinzeile `KvMonoTextStyle` 11,5 |
| Seitenlisten `.seite-l/.seite-r` | oben 74; Breite 250; Abstand 8; links 16 / rechts 16; Einblenden gestaffelt 35 ms je Zeile, Versatz ±14 px | Spalten 260, Polster 12, Abstand 4 | 250 / 250, Polster 16 seitlich, Abstand 8; Staffelung über `CardAnimations` nur bei Bewegung an |
| Zeile links `.knopf-l` (lesbare Formate) | Raster `56 | 1fr`, Polster 5,8, Radius 9, Rand `line`, Hintergrund `panel` 75 %, 12 `muted`; Hover/gewählt Rand Art 60 %, Hintergrund Art 10 %, Text `ink`; Chip links | `RadioButton` MinHeight 32 mit Punkt + Text | `RadioButton` mit `KvOptionStyle`-Variante `Chip` (Chip statt Kreis; Kreis bleibt nur für den Bildschirmleser-Zustand über `AutomationProperties`); Mindesthöhe 30 |
| Zeile rechts `.knopf-r` / `.kachel` (Ziele) | wie links, Chip rechts, Text rechtsbündig; „aus“ Deckung 0,3; gewählt/empfohlen Rand Mint, Fläche `mint-flaeche`, Titel Mint; Karte `.kachel` Radius 11, Polster 7,10, 12,5 / SemiBold; Marke `.empf-tag` Pille Radius 999, Polster 6,1, 9,5 Mono SemiBold, Mint-Fläche mit `auf-mint` | `Grid` Polster 8,4, Deckung über `DimIf`; „empfohlen“ als Mint-Umriss-Schild | `KvRowStyle`-Variante rechtsbündig, Radius 11; Chip `KvChipStyle`; empfohlen: Rand Mint + `KvMintSurface`, Marke `KvPillStyle` gefüllt (Mint auf `KvOnMint`, 9,5 Mono) |
| Überschriften „öffnet“/„macht“ | `.z-kopf b` 13, small 11 `muted`; Mint-Variante | BodyStrong 14 | 13 / SemiBold, Kleinzeile 11 |
| Fächerleiste im Zoom | Höhe 52; Fach Polster 8,10 nur Kopf | `MinHeight` 52 | Höhe 52 fest |

### 3.4 Schritt 2 „Ziel“ (`Views/TargetPage.xaml`)

| Element | Entwurf | Heute in XAML | Änderung |
|---|---|---|---|
| Spalten | links `.z-l` 236 (left 16, top 14); rechts `.z-r` 286 (right 16, bottom 14, rollt); Mitte Zeichenfläche | 320 / * / 360, Polster 24, Abstände 20 | 236 / * / 286, Polster 16,14; Abstand innerhalb der Spalten 6 |
| Arten links `.art-kopf` (gewählte Art) | Symbolkachel 40 × 40 Radius 12, Art auf Art 16 % mit Rand Art 40 %; Name 16; Kleinzeile 11 Mono `muted` | `ListView` mit Punkt 12, BodyStrong, Caption-Zähler | gewählte Art als Kopf mit Kachel 40; übrige Arten als Zeilen `KvOptionStyle` (Punkt 9, Name 13, Zähler Mono); `ListView` bleibt das Steuerelement (Ressourcen), `ItemTemplate` neu |
| Dateikarte `.z-datei` | Raster `1fr | auto`, Abstand 6, Polster 6,9, Radius 10, Rand `line`, Hintergrund `panel` 80 %; Name SemiBold, Kleinzeile 10 Mono `muted`; gewählt Rand Art, Fläche Art 12 % | `CardBorderStyle` Polster 12, Abstand 4, Rand unten 8 | Polster 9,6, Radius 10, `KvPanel80Brush`, Abstand zwischen Karten 6; Name 12,5 SemiBold; Größe `KvMonoTextStyle` 10; Warnung `KvVideo` |
| Alle gleich / Jede einzeln | `.segment` (Verlauf-Baustein) | zwei `RadioButton` waagerecht | `KvSegmentStyle` |
| Karte rechts `.karte-e` / `.einst-k` | Abstand 9 / 8; Polster 10 / 9,10; Radius 13 / 12; Rand `line`; Hintergrund `bg` 82 % + Blur 6 / 80 %; Überschrift 12,5 mit Größe rechts 11 | `Border`s mit Radius 8, Polster 12 | `KvGlassCardStyle` Radius 12, Polster 10, `KvBg90Brush` |
| Kleinüberschrift `.ueber` | 10,5 Mono SemiBold, Sperrung 100, Großbuchstaben, `leise`; Mint-Variante | `SectionHeaderTextStyle` (BodyStrong 14, Rand 0,16,0,8) | `KvLabelTextStyle`, Rand 0,10,0,4 |
| Zielformat-Reiter `.e5-reiter` | zwei Spalten, Abstand 5; Knopf Radius 11, Polster 7,10, Rand `line`, Hintergrund `panel` 85 %; Name 11,5 `ink`, Format 11 Mono SemiBold Mint; gewählt Rand Mint + `mint-flaeche`; ▾/▴ 10 rechts | `ComboBox` Stretch | `ComboBox` bleibt (Standard, Barrierefreiheit), mit den Ressourcen aus 2.1 (a); die Reiter-Optik ist eine spätere Option |
| Note `.e5-gross .ring.gross` | Ring 92, innen `inset 8` `bg`, Bogen `conic-gradient` Farbe nach Note; Zahl 26 Mono Bold, Kleinzeile 9,5 Mono `muted`; daneben Wort 17, Größe 12, Ersparnis 10,5 Mono | Ring 152, Strich 10, Zahl Title 28, zwei Caption-Zeilen | `GradeRingSliderStyle` → Ring 92, Bogen 8 (gefüllt bis zur Note über `ArcSegment` in der Vorlage, ohne Zeichenschicht, weil ein Bogen als `Path` genügt); Zahl 26 Mono Bold; Wort und Größe rechts daneben als Raster `auto | 1fr`, Abstand 14 |
| Ringgriff `.e5-rgriff` | Griff 46 (ohne Zahl 32), Hintergrund `bg`, Glühen Mint 45 % 16 px; Zahl 14 Mono Bold | kein Griff (Ring ist Slider ohne Daumen) | Daumen sichtbar: Kreis 32 auf dem Bogen, Mint-Rand 2, Glühen 25 %; Tastatur ±5 bleibt |
| Größenleiste `.gleiste` | Höhe 54 (mit Ringgriff 66, `margin-top` 36); Spur oben 16, Höhe 12, Radius 6, 24 Segmente; Griff 28, `#000`, Rand 2 Mint, Glühen 14; Marken oben 9,5 Mono SemiBold `muted` mit 1-px-Strich 18 hoch; Skala unten 10 Mono `leise` | Breite fest 320; Segmente 13,3 × 6; Standard-Slider darüber; Marken auf `Canvas` 18 | `KvSizeSliderStyle`; Breite `Stretch` (Segmente über `UniformGrid`-artiges `Grid` mit 24 `*`-Spalten statt fester Breite); rechte Spalte 286 |
| Zonen `.e5-z` / `.akk-z` | Kopf Raster `82 | 1fr | 12`, Polster 7,10 (Akkordeon 8,10), Radius 11, Rand `line`, Hintergrund `panel` 85 %; Name 12 `ink` (Akkordeon 12,5, Wert 11,5 Mono SemiBold Mint), Kleinzeile 10,5 `muted`; Balken `.bal` 6 (Kopf) / 8 (Inhalt), Radius 4, Spur `tief`, Füllung Artfarbe; Chevron ▾ 10–11 `muted`, dreht 0,2 s; offen Rand `mint-rahmen`; Inhalt Trennlinie oben, Polster oben 10 bzw. 0,10,10 | `Expander` Standard (48 hoch, Chevron 12), `ProgressBar` 3 px | `KvExpanderStyle`; `ProgressBar` 6 px Radius 4 Spur `KvDeep`; `Slider` im Inhalt Standard |
| Was sich ändert `.folgen li` | Zeile 12, Zeilenhöhe 1,35, Abstand 7; Symbolkreis 18 (10 Mono Bold): gut = `mint-flaeche`/Mint mit Rand `mint-rahmen`, Hinweis = `amber` 14 % auf `panel`, Warnung = `coral` 14 %; Kopf `.folgen-kopf` = `KvLabelTextStyle` | siehe Zeile „Was sich ändert `.zweck`“ | die Kreisvariante gilt für die Liste „Was sich ändert“; `.zweck`-Zeilen (unten) nur, wenn Einträge wählbar sind |
| Umschalter `.stufen` / `.umschalt` | Behälter `tief`, Rand `line`, Radius 9–10, Polster 2; Knopf Radius 7–8, Polster 4 bzw. 5,6, 11–12 `muted`; gedrückt `panel2` + `ink` + innerer Ring 1 px `mint-rahmen`; deaktiviert 0,4 | – | zweite Größe von `KvSegmentStyle` (`Compact`) |
| Zielgröße genau `.zielfeld` | Feld: Rand 1,5 `mint-rahmen`, Radius 10, Hintergrund `panel2`, Polster 5,10; Zahl 17 Mono Bold; Einheit 12 Mono SemiBold `muted`; Fokus Rand Mint + Ring 3 px `mint-flaeche` | `Expander` + `ToggleSwitch` + `NumberBox` 140 | `NumberBox` bleibt (Standard), Ressourcen: Rand `KvMintFrame` 1,5, Radius 10, Schrift 17 Mono Bold; Fokusring global |
| Schalter `.schalter` (Metadaten) | Zeile 11,5–12 `muted`, Knauf 30 × 17 | `ToggleSwitch` mit Beschriftung | `KvSwitchStyle`, Beschriftung links als `TextBlock` 12 `KvMuted`, Schalter rechts (Raster `1fr | auto`) |
| Was sich ändert `.zweck` | Zeilen Raster `26 | 1fr | auto`, Polster 7,9, Radius 11, Rand `line`, Hintergrund `panel` 85 %; Symbol 20 `muted`; Titel 12,5; Kleinzeile 10 Mono `muted`; gewählt Rand Mint + `mint-flaeche` | `ItemsRepeater` mit Symbol 14 + Caption | `KvRowStyle` Radius 11, Polster 9,7; Symbol 20 in Artfarbe; Titel 12,5 SemiBold; Kleinzeile Mono 10 |
| Damals-Karte `.damals-hinweis` | Raster `auto | 1fr | auto`, Polster 10,12, Abstand 4,12, Radius 12, Rand **gestrichelt** `line-stark`, Hintergrund `panel` 85 %; Symbol 18 Mint; Titel 12,5 SemiBold; Kleinzeile 11 Mono `muted` mit `ink`-Werten; Knopf `.btn` klein 11,5 / 6,10 | `PreviousCard` `KvPanel2`, Rand `KvLine`, Radius 8, Polster 12 | `KvDashedBoxStyle`; Knopf `KvSmallButtonStyle`; Marke `.damals-tag` (Pille 8,5 Mono SemiBold, gestrichelt, `↺`) als `KvPillStyle`-Variante an Ring und Leiste |
| Fußleiste | `.leiste` Polster 12,18; Summe 12 Mono `muted`, Zahl `ink` 13, Ersparnis Mint SemiBold 13 | Polster 24,12, `KvPanel`, Caption | `KvBottomBarStyle`; Summe mit `Run`s |
| Zurück / Weiter | `.btn` / `.taste` | Standard / Accent 160 × 40 | Standard mit Ressourcen / `KvPrimaryButtonStyle` |

### 3.5 Schritt 3 „Umwandeln“ (`Views/ConvertPage.xaml`, nur außerhalb von `SwirlHost`)

| Element | Entwurf | Heute in XAML | Änderung |
|---|---|---|---|
| Aufteilung `.w5` | Bühne 300 hoch (ganze Breite, Zeichenfläche), darunter Liste `1fr`; kein Spaltenraster | 300 / * / 340, Polster 24, Liste in Zeile 2 | Bühne = `SwirlHost` über die ganze Breite 300 hoch (Fenster < 720: 220); Eingang und Speicherort **liegen auf der Bühne** (links unten, rechts unten) als XAML über der Zeichenfläche. Das Spaltenraster entfällt in der breiten Ansicht; unter 900 px stapeln wie heute. **Abstimmung mit dem Wirbel-Entwickler nötig**, siehe Zusammenarbeit |
| Eingang `.w5-ein` | links 22, unten 16; Titel 13; Fehlerzeile 11 `coral`; max. 300 | Karte `CardBorderStyle` mit Zeilen (Punkt 10, Body, Caption) | ohne Karte: Titel 13 SemiBold, Fehler 11 `KvError`; die Zeilen bleiben (Punkt 10, 12,5, Zustand Mono 10,5) |
| Speicherort `.w5-gross` | rechts 22, unten 12; Breite `min(300px, 30 %)`; Abstand 6; Polster 6,8; Radius 12; Ordner-Ablage: `mint-flaeche` + Ring 2 px Mint; Label 10 Mono SemiBold Sperrung 100 `leise`; Ordnername 16 / SemiBold; Pfad 10,5 Mono `muted` | `LocationCard` Karte Polster 12 mit BodyStrong/Body/Caption | ohne Karte (transparent), Polster 8,6, Radius 12; Ablage-Zustand als `VisualState`; Texte wie Entwurf |
| Speicherort ändern `.w5-aendern` | Rand `line-stark`, Radius 8, Polster 10,4, Hintergrund `panel`, 12 / SemiBold; Hover Rand `mint-rahmen`; deaktiviert 0,5 | `DropDownButton` Standard | `DropDownButton` mit Ressourcen (Pfeil bleibt); Polster 10,4, 12 SemiBold |
| Tasche `.w5-tasche` | Rand gestrichelt `line-stark`, Radius 8, Polster 9,4, 11,5, Symbol 14 Mint; Hover Rand Mint; hüpft 0,4 s | `DropDownButton` Standard | `KvDashedBoxStyle` um einen `DropDownButton` mit `KvSmallButtonStyle`; Hüpfen über `CardAnimations.CountHop` |
| Ordner-Auswahl-Menü `.w5-menue`/`.w5-opt` | Breite 300, Polster 8, Radius 13, Rand `line-stark`, Hintergrund `panel`, Schatten; Titel 12,5 SemiBold + Pfad 10,5 Mono; Optionen `KvOptionStyle`; `hr` `line` Rand 4,2; Fuß 11,5 `muted` | `Flyout` Standard mit `RadioButton`s MinWidth 220/240 | `KvMenuFlyoutPresenterStyle`; `RadioButton`s mit `KvOptionStyle`; Breite 300; „Ordner wählen…“ als Option mit gestricheltem Quadrat |
| Bericht `.w5-bericht` | mittig, oben 128, Breite `min(440px, 90 %)`, zentriert; Titel 18 / SemiBold Sperrung −10; Zeile 11,5 Mono `muted`, Fehler `coral`; Knöpfe Abstand 8, oben 8 | Bericht in `SwirlHost` mit Subtitle 20 und Caption | Titel 18 SemiBold; Zeilen `KvMonoTextStyle` 11,5; Knöpfe `KvSmallButtonStyle` / `KvOutlineMintButtonStyle` (Ordner öffnen); Position in der Bühne über `Grid`-Ausrichtung (oben 128) |
| Liste `.w5-liste` | Polster 6,20,8; Abstand 4; Rand oben `line`; Hintergrund `panel` 30 % | `CardBorderStyle` Polster 16,0 um `ItemsControl` | ohne Karte: Rand oben `KvLine`, `KvPanel30Brush`, Polster 20,6,20,8; Zeilenabstand 4 |
| Listenkopf `.w5-kopf` | Zahl 15 Mono SemiBold, Rest 12 `muted`, fertige Mint; Spaltenköpfe `.w5-spalten` Polster 0,12 | `SectionHeaderTextStyle` + Caption | `Run`s: 15 Mono SemiBold; Spaltenköpfe `KvLabelTextStyle` |
| Zeile `.w5-zeile` | Raster `10 | 170 | 124 | 1fr | 214 | 58`, Abstand 12; Polster 4,12; Radius 11; Rand `line`, Hintergrund `panel`; Fortschritt als 2-px-Linie unten in Mint mit Glühen; läuft Rand `mint-rahmen`; Fehler Rand `coral-rahmen` | `Grid` Polster 0,10, Rand unten `KvLine`, Spalten `Auto | 2* | Auto | 3* | Auto | Auto` | `KvRowStyle`; feste Spalten wie Entwurf, Pfadspalte `*`; Fortschrittslinie als `Rectangle` 2 px unten (Breite über `ProgressValue`, `ScaleTransform`); Zustände als `VisualState` |
| Punkt `.w5-punkt` | 10, rund, Artfarbe | `Ellipse` 10 | bleibt |
| Name `.w5-nm` | Name 12,5 SemiBold; Größen 10,5 Mono `muted`, einzeilig | BodyStrong + Caption | `KvRowTextStyle` + `KvMonoTextStyle` |
| Format → Format `.w5-fa`/`.w5-chip`/`.w5-pfeil` | Raster `44 | 1fr | 44`, Polster 3, Radius 8, Hover `hover`; Chips 22 hoch, Radius 6, 11 Mono Bold: alt = Art auf Art 12 %, Rand Art 40 %; neu = Mint gestrichelt 1,5 `mint-rahmen`, fertig Mint gefüllt mit `auf-mint`; Pfeil 2 px `line-stark` mit Mint-Füllung nach Fortschritt, Spitze 7/5 | ein Schild „HEIC → JPG“ Polster 6,1 Radius 4 | zwei Chips `KvChipStyle` (Höhe 22) und eine Pfeil-Zeichnung (`Rectangle` 2 px + `Polygon`) als `UserControl` `FormatArrow` in `Views/`; `Button`-Hülle nur wenn `CanChangeTarget` |
| Ziel `.w5-ziel`/`.w5-tag`/`.w5-pfad` | Polster 8,4, Radius 8, Hover `hover`; Marke 10 Mono SemiBold `muted` („gemeinsam“/„eigen“ Mint); Pfad 11,5 Mono, Ordner `muted`, Datei SemiBold, fertig Mint | `StackPanel` mit Caption + BodyStrong | `KvMonoTextStyle` 11,5 mit `Run`s; Marke `KvLabelTextStyle` 10 ohne Großbuchstaben |
| Aktionen `.w5-akt` | Polster 9,5, Radius 8, Rand `line`, Hintergrund `panel2`, 11,5 / Medium, Symbol 13 `muted`; Hover Rand Mint; `.haupt` Mint-Umriss; deaktiviert 0,45 | Standard-Buttons, `DropDownButton` | `KvSmallButtonStyle`; „Öffnen“ `KvOutlineMintButtonStyle` |
| Zustand `.w5-st`/`.w5-grund` | 11 Mono Medium `muted`, Tabellenziffern, rechtsbündig; fertig Mint; Fehler `coral`; Grund 11,5 `coral` | Caption rechts, Fehlertitel BodyStrong + Caption | `KvMonoTextStyle` 11 mit `FontFeatures`/`Typography.NumeralAlignment="Tabular"`; Fehlertitel 12,5 SemiBold `KvError`, Grund 11,5 `KvError` |
| Fußleiste | `.leiste` 12,18; Gesamtfortschritt (Balken 6) | Polster 24,12 `KvPanel`, `ProgressBar` 3 | `KvBottomBarStyle`; `ProgressBar` 6 |
| Toast `.w5-toast` | unten 76, Polster 14,8, Radius 10, `ink` auf `bg` (invertiert), 12,5, Schatten | kein Toast (Ansage-Region) | nicht in dieser Etappe; bleibt Ansage |

### 3.6 Verlauf (`Views/HistoryPanel.xaml`, `MainPage` `SplitView`)

Der Entwurf zeigt den Verlauf als **ganzseitige Überlagerung** unter der Titelleiste (`inset: 42px 0 0 0`) mit Kopfzeile, Filter-Segment, Zeitleiste und Karten je Runde. Die App zeigt ein Seitenpanel (380 px, rechts). Der Wechsel zur Überlagerung ist ein Aufbau- und Ablaufthema (Suche, Filter, Runden-Gruppierung im ViewModel) und **kein Teil dieser Etappe**; hier werden Kopf, Karten, Knöpfe und Zeilen im Panel angeglichen und das Panel auf 440 px verbreitert.

| Element | Entwurf | Heute in XAML | Änderung |
|---|---|---|---|
| Fläche `.verlauf` | Hintergrund `bg` mit Mint-Radialverlauf 7 %; Einblenden 0,3 s / 0,5 s `--feder` | `SplitView` Overlay 380, Standardhintergrund | Breite 440; Hintergrund `KvBackground`; Einblenden bleibt Standard |
| Kopf `.v-kopf` | Polster 12,22; Abstand 10,14; Rand unten `line`; Hintergrund `panel` 45 %; Titel 17 / SemiBold; Unterzeile 12 `muted` mit Mint-Symbol 13 | Polster 16, Subtitle 20, Löschen-Knopf, Schließen | `KvTitleTextStyle` 17; Unterzeile („lokal, jederzeit löschbar“ aus vorhandener Ressource) 12; Rand unten `KvLine`, `KvPanel45Brush`; Schließen `KvIconButtonStyle`; „Leeren“ `KvSmallButtonStyle` |
| Filter `.segment` | Behälter `tief`, Rand `line`, Radius 10, Polster 3; Knöpfe 12 `muted`, Punkt 8 Radius 2 in Artfarbe; gewählt `panel2` `ink` mit innerer Kante | – | entfällt hier (kein Filter im ViewModel); `KvSegmentStyle` wird für Schritt 2 gebaut und steht dann bereit |
| Tag-Titel `.tag-titel` | 10,5 Mono Medium Sperrung 100 Großbuchstaben `leise`; Rand 18,0,8,76 | – | Datum-Gruppen: `KvLabelTextStyle`; gruppiert über `CollectionViewSource` nach Tag (`WhenText` liefert den Tag); Rand 18,0,8,0 (ohne Zeitleiste) |
| Zeitleiste `.tag-block::before`, `.runde .zeit` | Linie 2 px Mint-Rahmen-Verlauf bei 60 px; Zeit 12 Mono SemiBold rechtsbündig, Punkt 10 mit Mint-Rand 2 | – | nicht in dieser Etappe (kommt mit der Überlagerung) |
| Karte `.rk` | Radius 13; Rand `line`; Verlauf `panel2`→`panel`; innere Kante; Schatten; Hover −2 px, Rand `line-stark` | `ListViewItem` Polster 0,6, kein Rahmen | `ListViewItem` Polster 0,5; Inhalt `KvCardStyle` mit `ThemeShadow` 8; Hover Rand `KvLineStrong` |
| Kartenkopf `.rk-kopf` | Raster `36 | 1fr | auto | auto`, Polster 10,12, Abstand 12; Blätterstapel 32 × 28 mit Zähler-Pille (Artfarbe, 9,5 Mono Bold); Titel 13,5 SemiBold, Kleinzeile 11 Mono `muted`; Ersparnis-Pille Mint | Symbol 14 + BodyStrong + Caption-Zeilen | Zustandssymbol 16 in Artfarbe (kein Blätterstapel; der ist Zeichnung), Titel `KvHeadTextStyle`, Kleinzeile Mono 11 (Format, Einstellungen), Ersparnis `KvPillStyle` rechts |
| Knöpfe `.vk` | Polster 10,6, Radius 9, Rand `line`, Hintergrund `panel`, 12 / Medium, Symbol 14; `.haupt` Mint-Umriss; `.rund` 30 × 30; deaktiviert 0,4 | Standard-Buttons | „Nochmal“ `KvOutlineMintButtonStyle`, „Ordner öffnen“ `KvSmallButtonStyle`; Rundknopf 32 |
| Dateizeilen `.rk-dateien`/`.vd` | Liste mit 1-px-Haarlinien (`gap 1`, Hintergrund `line`), Radius 10, Rand 12 außen; Zeile Raster `50 | 14 | 58 | 1fr | auto | 104 | auto`, Polster 7,10, 12; Chip, Pfeil Mint, Papier-Etikett, Name SemiBold, Kleinzeile 10,5 Mono, Größe 11 Mono, Status 11 mit Symbol 13 (fertig Mint, fehlt `coral`, Name durchgestrichen) | eine Zeile je Eintrag | Eintrag = Karte mit **einer** Dateizeile (heutige Datenlage); Chip `KvChipStyle`, Etikett `KvPaperTagStyle`, Größe `KvMonoTextStyle` 11; „Original fehlt“: Name durchgestrichen `KvMuted` (vorhandene Prüfung in `AgainCommand.CanExecute`) |
| Leerzustand `.v-leer` | zentriert, Polster 70,20, Abstand 8, 15 `ink` + `muted` | Caption `KvMuted` | Titel 15 SemiBold + Zeile 12,5 `KvMuted`, zentriert |

### 3.7 Einstellungen (`Views/SettingsPage.xaml`)

Der Entwurf hat **keine Einstellungsseite**; Grundlage sind die Einstellungs-Bausteine aus Schritt 2 (`.einst-k`, `.schalter`, `.eingabe`, `select`, `.label`) und der Verlauf-Kopf.

| Element | Entwurf (abgeleitet) | Heute in XAML | Änderung |
|---|---|---|---|
| Seite | Inhalt max. 900; Polster 16,14 | `MaxWidth` 900, Polster 24,8,24,24 | Polster 22,14,22,24 |
| Titel | `.v-kopf h4` 17 / SemiBold | `TitleTextBlockStyle` 28 | `KvTitleTextStyle` 17 |
| Abschnitt | `.label`/`.ueber` 10,5 Mono Sperrung 100 Großbuchstaben `leise` | `SectionHeaderTextStyle` | `KvLabelTextStyle`, Rand 0,18,0,6 |
| Karte `SettingsCard` | `.einst-k` Polster 9,10, Radius 12, Rand `line`, Hintergrund `bg` 80 % | Standard (Toolkit) | Ressourcen aus 2.1 (a); Symbol 16 `KvMuted`; Karten-Abstand 4 |
| Auswahl (Sprache, Design, Speicherort) | `select`: Rand `line-stark`, Radius 8, Polster 8,6, 13 | `ComboBox` MinWidth 180/220 | Ressourcen; MinWidth bleibt |
| Schalter | `.toggle` 38 × 22 | `ToggleSwitch` mit Text | `KvSwitchStyle` (ohne On/Off-Text) |
| Regler parallele Jobs | `input[type=range]` + Wert 12 Mono SemiBold `ink` | `Slider` 200 + Text | Standard; Wert `KvMonoTextStyle` 12 SemiBold `KvInk` |
| Namensmuster | `.eingabe` + Vorschau 11,5 `muted` | `TextBox` + Caption | Ressourcen (Mono 12,5); Vorschau `KvSmallTextStyle` |
| FFmpeg-Ordner | Pfad 10,5 Mono `muted`; Knöpfe `.btn` | Caption + Standard-Buttons | `KvMonoTextStyle`; Standard-Buttons mit Ressourcen |
| Hinweis Neustart | – | `InfoBar` Standard | bleibt |
| Pro / Über / Lizenzen / Datenschutz | klickbare Karten `.rk`-artig | `SettingsCard IsClickEnabled` | Ressourcen; Pro-Zeile zeigt `KvProBadgeStyle` |

### 3.8 Pro, Über, Lizenzen (`Views/ProPage.xaml`, `AboutPage.xaml`, `LicensesPage.xaml`)

Kein Gegenstück im Entwurf; abgeleitet aus Karten, Schildern und Knöpfen.

| Element | Entwurf (abgeleitet) | Heute in XAML | Änderung |
|---|---|---|---|
| Titel | 17 / SemiBold | `TitleTextBlockStyle` | `KvTitleTextStyle`; Versionszeile `KvMonoTextStyle` 11 |
| Fließtext | 13 / Normal, Zeilenhöhe 1,45, max. 66 Zeichen (`.lead`-Regel) | `BodyTextBlockStyle` 14 | `KvBodyTextStyle`; `MaxWidth` 720 bleibt |
| Karte (Funktionen, Datenschutz) | `.rk`: Radius 13, Verlauf, Rand `line` | `CardBorderStyle` Radius 8 | `KvCardStyle`; Zeilen 13 mit Symbol 16 Mint |
| Pro-Status | `.pro`-Schild + Text | Symbol + BodyStrong | `KvProBadgeStyle` neben Text 13 SemiBold |
| Kaufen / Wiederherstellen | `.taste` / `.btn` | `AccentButtonStyle` / Standard | `KvPrimaryButtonStyle` / Standard mit Ressourcen |
| Lizenzliste | `ListView` Standard | `ListView` 240 | bleibt, Ressourcen (Radius 8, Mint-Balken) |
| Lizenztext | Mono 12 auf `panel` in Karte Radius 13 | `Consolas` in `CardBorderStyle` | `KvMonoFontFamily` 12, `KvCardStyle`, Polster 16 |
| App-Symbol | 64 × 64 | 64 × 64 | bleibt |

## 4. Teilaufgaben A–F

Reihenfolge nach Sichtbarkeit: erst der Rahmen, den jede Seite trägt, dann Schritt 1, das die App öffnet. Jede Teilaufgabe endet mit dem Vergleichsbild aus Abschnitt 5 und dem Abnahmekriterium: **Abweichung nur noch in Schrift (Segoe statt Geist) und in bewusst belassenen Standard-Elementen** (Liste in 2.1); jede weitere Abweichung steht mit Grund in `docs/06-design.md` unter „Umsetzungsstand Abgleich Mischentwurf“.

| Teil | Inhalt | Dateien | Abnahme |
|---|---|---|---|
| **A · Fundament, Rahmen, Schrittleiste** | neue Tokens und Mischpinsel (1.3), `Themes/KvertisControls.xaml` mit Textstilen (1.2), Überschreibungen (2.1 a), Stilen (2.1 b) und `KvPrimaryButtonStyle`; Fokus-Ressourcen (1.6); Schrittleiste nach 3.1; Titelleisten-Werte (Höhe, Rand, Titel, Icon-Knöpfe, Pro-Schild) | `Themes/KvertisColors.xaml`, neu `Themes/KvertisControls.xaml`, `App.xaml`, `Views/StepHeader.xaml(.cs)`, `MainWindow.xaml` **nur die Zeilen der Titelleiste** (siehe Zusammenarbeit) | Vergleichsbild Schritt 1 leer: Titelleiste und Faden decken sich in Höhe, Randlinien, Knoten (28 px Quadrate, Ziffern, Sockel), Segmenten; alle Knöpfe der App tragen `.btn`-Optik, Mint-Knöpfe den Sockel; Hoher Kontrast: keine Farbwerte außer Systemfarben, Sockel und Schatten aus |
| **B · Schritt 1 und Zoom-Wege** | 3.2 und 3.3 vollständig; Zieh-Overlay; Fächer mit oberem Artrand; Dateizeilen 28 px mit Hover-Entfernen; Chips; Summe oben links | `Views/MainPage.xaml(.cs)` (Layout, Overlay), `GalaxyHost.xaml`, `TrayControl.xaml`, `TrayFileControl.xaml`, `PathsOverlay.xaml`, `Strings/*/Resources.resw` (Overlay-Text, Summe) | Vergleichsbilder leer, mit 3 Dateien, im Zoom; Tastaturweg Fachkopf → Zeile → Entfernen (sichtbar bei Fokus) unverändert; Bildschirmleser liest Zahl, Name, Zustand wie vorher |
| **C · Schritt 2** | 3.4 vollständig; `KvExpanderStyle`, `KvSizeSliderStyle`, Ring 92 mit Bogen und Daumen, `KvSegmentStyle`, `KvOptionStyle` | `Views/TargetPage.xaml(.cs)`, `Themes/KvertisControls.xaml` (drei Vorlagen), `ViewModels/Target/*` nur, falls ein Text neu gebraucht wird | Vergleichsbild mit Bild- und Videodateien; Ring per Pfeiltasten ±5, Leiste per Pfeiltasten, Zonen per Leertaste; `AutomationProperties.Name` mit Wert bleiben |
| **D · Schritt 3** | 3.5 ohne die Bühne selbst: Liste, Zeilen, Chips mit Pfeil (`FormatArrow`), Menü und Optionen, Fußleiste, Bericht-Texte; Eingang und Speicherort auf die Bühne erst nach Abstimmung | `Views/ConvertPage.xaml(.cs)` **nur Liste, Fußleiste, Flyouts**, neu `Views/FormatArrow.xaml`, `Themes/KvertisControls.xaml` (`KvMenuFlyoutPresenterStyle`) | Vergleichsbild vor dem Start und nach dem Lauf; Fehlerzeile mit `KvErrorFrame`; „Öffnen“/„Im Ordner zeigen“ per Tastatur |
| **E · Verlauf** | 3.6 | `Views/HistoryPanel.xaml(.cs)`, `MainPage.xaml` (`OpenPaneLength` 440), `ViewModels/HistoryViewModel.cs` (Tag-Gruppierung, ohne neue Logik) | Vergleichsbild mit drei Einträgen an zwei Tagen; Leerzustand; Hoher Kontrast ohne Schatten |
| **F · Einstellungen, Pro, Über, Lizenzen** | 3.7 und 3.8 | `Views/SettingsPage.xaml`, `ProPage.xaml`, `AboutPage.xaml`, `LicensesPage.xaml` | Vergleichsbild gegen ein Bausteine-Bild (siehe 5): Karten, Labels, Schalter, Felder wie Schritt 2 |

Nach jeder Teilaufgabe: `dotnet build Kvertis.sln`, `dotnet test tests/Kvertis.App.Tests -p:Platform=x64`, `bash tools/compliance/check.sh`, Commit je Teilaufgabe (Regel „Commit nach jeder Etappe“), Eintrag in `docs/06-design.md` (Abweichungsliste) und `docs/CHANGELOG.md` durch den Doku-Pfleger. Keine neue Bibliothek in dieser Etappe; `CommunityToolkit.WinUI.Controls.SettingsControls` bleibt, wie eingetragen.

## 5. Prüfmethode: Vergleichsbild je Seite

1. Entwurf: `node tools/design-server.js` (Port 8765), Browser auf `http://localhost:8765/`, Fensterbreite so, dass `.win` genau **1280 px** breit ist (Seite hat 20 px Polster je Seite und `max-width 1320`: Browserfenster innen 1320 px → `.win` 1280 px; bei 100 % Zoom prüfen). Dunkles Farbschema des Systems, denn Dunkel ist die Vorgabe; Hell als zweites Bild. Im Entwurf den passenden Schritt und Zustand wählen (Demo-Knöpfe über dem Fenster). Bildschirmfoto nur des `.win`-Rechtecks (Werkzeug: Ausschneiden mit fester Größe 1280 × 900).
2. App: `powershell -File tools/dev/run-app.ps1 -Width 1280 -Height 900 -Monitor 1` (Nebenbildschirm, Regel „Testläufe auf Nebenbildschirm“), gleicher Zustand (Debug: `-StageFiles` für Schritt 2/3), Bildschirmfoto des Client-Bereichs 1280 × 900 (`Alt+Druck` liefert den Rahmen mit; stattdessen `Win+Umschalt+S` mit festem Rechteck oder ein Skript, das `MoveWindow` so setzt, dass der Client-Bereich 1280 × 900 misst).
3. Nebeneinander (Entwurf links, App rechts) in `docs/entwuerfe/bilder/abgleich-<seite>-<zustand>.png` ablegen (nicht ins Repository, nur lokal; der Ordner steht in `.gitignore`), zusätzlich eine Überlagerung mit 50 % Deckung, um Höhen von Leisten, Zeilen und Karten zu vergleichen.
4. Prüfliste je Bild: Höhen der Leisten (42/48, 62, Fußleiste), Spaltenbreiten, Radien, Randfarben, Schriftgrößen und -gewichte (per Lineal-Werkzeug auf x-Höhe), Chips, Abstände zwischen Karten, Zustände (Hover, gewählt, Fokus, deaktiviert), Hell und Dunkel, Hoher Kontrast (ein Kontrastthema mit dunklem Hintergrund) und ruhige Ansicht (`KVERTIS_REDUCED_MOTION=1`).
5. Für Einstellungen, Pro, Über, Lizenzen (kein Entwurf) dient das Bausteine-Bild: Schritt 2 rechte Spalte (Karten, Labels, Schalter, Felder) und Verlauf-Kopf; Kriterium ist Gleichheit der Bausteine, nicht des Aufbaus.

## 6. Reihenfolge und Zusammenarbeit mit den laufenden Arbeiten

- **Übergänge** (ADR-023) bauen `Rendering/TransitionOverlay` in `MainWindow.xaml` ein und ändern `MainPage`, `TargetPage`, `ConvertPage` um `ITransitionAnchors`. Teil A ändert in `MainWindow.xaml` nur das `TitleBarGrid` (Werte, Stile) und in `StepHeader.xaml` alles; Teil B wartet mit `MainPage.xaml`, bis der Anker-Commit der Übergänge auf `main` liegt, und setzt dann auf. Das Zieh-Overlay liegt in `MainPage`, nicht in `MainWindow`.
- **Wirbel** (ADR-023) ersetzt `SwirlHost` in `ConvertPage.xaml` und die Mitte der Seite. Teil D fasst in `ConvertPage.xaml` nur Liste, Flyouts und Fußleiste an; die Verlegung von Eingang und Speicherort **auf die Bühne** (3.5, erste Zeile) wird mit dem Wirbel-Entwickler abgestimmt und erst nach dessen Commit umgesetzt (eigenes Teilstück D2).
- Gemeinsame Datei ist `Themes/KvertisControls.xaml`: Teil A legt sie an; die beiden anderen Entwickler fügen dort nichts hinzu (Zeichenschicht braucht keine XAML-Stile).
- `CardBorderStyle` und `SectionHeaderTextStyle` bleiben als Schlüssel bestehen (Alias), damit Seiten, die parallel geändert werden, nicht brechen; ihre Werte werden in Teil A auf die neuen gesetzt.

## 7. Offene Punkte

- Verlauf als ganzseitige Überlagerung mit Zeitleiste, Suche und Filter (Aufbau, eigenes Blatt).
- Eingang und Speicherort auf der Bühne von Schritt 3 (D2, nach dem Wirbel).
- Zielformat als Reiter (`.e5-reiter`) statt `ComboBox`: erst, wenn der Reiter mit Bildschirmleser und Tastatur mindestens so gut bedienbar ist wie die `ComboBox`.
- Toast in Schritt 3 (`.w5-toast`): Ansage-Region reicht; offen, ob ein sichtbarer Hinweis dazukommt.
- Mica gegenüber `--bg` mit Verläufen: bewusste Abweichung; falls das Vergleichsbild in Hell zu stark abweicht, Option „Hintergrund `KvBackground` ohne Mica“ als Einstellung prüfen.
- Schriftmetrik: Segoe UI Variable läuft etwa 3–5 % breiter als Geist; Spaltenbreiten (170/124/214 in Schritt 3) sind daraufhin zu prüfen, bevor sie fest übernommen werden.
