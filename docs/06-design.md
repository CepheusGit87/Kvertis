# 06 – Design

## Leitidee

Eine Fläche, ein Weg: Datei rein → Format bestätigen → Start. Alles andere ist erreichbar, aber nicht im Weg. Windows-11-Look mit Mica, Karten mit Tiefe, ruhige Animationen. Premium, nicht überladen.

## Screens

### 1. Hauptansicht (einziger Vollbild-Screen)

Aufbau von oben nach unten:

1. **Titelzeile** mit Mica-Hintergrund, App-Name, rechts: Verlauf, Einstellungen, Pro-Status.
2. **Ablagefläche** („Dateien hierher ziehen oder auswählen“). Große Karte, gestrichelter Rand, Icon. Unterhalb ein Button „Dateien wählen“ und ein Link „Ordner wählen“. Strg+V fügt Bilder aus der Zwischenablage ein. Beim Hovern mit Dateien hebt sich die Karte leicht an (Schatten, Skalierung 1.02).
3. **Vertrauenszeile** direkt unter der Ablagefläche, immer sichtbar: Schloss-Icon + „Ihre Dateien verlassen nie diesen PC.“
4. **Job-Liste**: Eine Karte pro Datei. Sobald Dateien da sind, schrumpft die Ablagefläche zu einer schmalen Leiste am oberen Rand („Weitere Dateien hinzufügen“).
5. **Aktionsleiste** unten, fixiert: Zielordner (Dropdown: „Gleicher Ordner“, „Unterordner ‚Kvertis‘“, „Wählen…“), Gesamt-Zeitschätzung, Button **Start** (Akzentfarbe, groß). Während der Konvertierung: Gesamtfortschritt, „Alle pausieren“, „Alle abbrechen“.

### 2. Job-Karte

- Links: Miniatur (Bild/Video-Frame) oder Typ-Icon, darunter Format-Badge („HEIC“).
- Mitte: Dateiname, Größe, Dauer/Auflösung/Seiten. Darunter das **Ausgabeformat als Chip** mit dem Standardvorschlag („→ JPG“), Klick öffnet die Formatliste. Daneben der Preset-Chip („Für E-Mail“).
- Rechts: Größenschätzung („≈ 1,2 MB“), Zeitschätzung, Buttons Vorschau / Mehr / Entfernen.
- Zustand laufend: Fortschrittsbalken in der Karte, Restzeit, Pause/Abbrechen. Die Karte bekommt eine dezente 3D-Kippanimation beim Start (Composition API, 300 ms) und ein leichtes Leuchten am Rand, das mit dem Fortschritt wandert.
- Zustand fertig: grüner Haken, „Vorher 8,4 MB → Nachher 1,1 MB“, Buttons „Ordner öffnen“, „Nochmal“.
- Zustand Fehler: rotes Icon, kurze Ursache, Button „Was tun?“ öffnet die Lösung.

Mehrfachauswahl: Ein Format-Chip in der Aktionsleiste („Alle: → JPG“) setzt das Format für alle kompatiblen Jobs.

### 3. „Mehr“-Panel (Flyout rechts)

Nur für erfahrene Nutzer, standardmäßig geschlossen:

- Regler **Qualität ↔ Größe** (0–100) mit Live-Größenschätzung.
- **Zielgröße** (Eingabe + Einheit), aktiviert die automatische Berechnung.
- Auflösung / Abtastrate / Bitrate je nach Kategorie, mit „Automatisch“.
- **Metadaten**: Schalter „EXIF/GPS entfernen“ (Standard an) mit einem Satz Erklärung.
- Dateinamen-Muster mit Vorschau.
- „Auf alle anwenden“.

### 4. Vorschau-Dialog

Zwei Bilder nebeneinander (oder Slider zum Überblenden), darunter „Vorher 8,4 MB · Nachher ≈ 1,1 MB (−87 %)“. Für Audio: Wellenform vorher/nachher und 10-Sekunden-Ausschnitt abspielbar. Für Video in Phase 1: ein Standbild vorher/nachher.

### 5. Verlauf (ganzseitige Überlagerung)

Ebene über dem ganzen Fenster unter der Titelleiste: links die Zeitleiste der letzten Konvertierungen nach Tagen, rechts das Detail des gewählten Eintrags (Formate, Einstellungen, Größe vorher/nachher, Dauer, Speicherort), oben Suche nach Dateinamen und Filter nach Art oder Fehlern. Knöpfe „Nochmal mit denselben Einstellungen“ (öffnet den Dateidialog und danach Schritt 2 mit den alten Werten), „Ordner öffnen“ und „Im Ordner zeigen“. Esc oder „Schließen“ schließt. Verlauf ist lokal, löschbar (Nachtrag Teil E).

### 6. Einstellungen

- Sprache (System / Deutsch / English).
- Design (System / Hell / Dunkel).
- Parallele Jobs (Standard nach Kernen, Regler 1–16).
- Standard-Zielordner, Standard-Dateinamen-Muster.
- Metadaten-Standard.
- Benutzerdefinierter FFmpeg-Pfad (nur für LGPL-Austausch, mit Erklärung).
- Über Kvertis, Third-Party Licenses, Datenschutz (Text: keine Daten, kein Netzwerk).
- Kvertis Pro: Status, Kaufen (Store), Wiederherstellen.

### 7. Pro-Hinweis

Erscheint nur, wenn ein Limit greift (Video-Datei abgelegt, Batch zu groß): kleine Karte in der Job-Liste, keine Modal-Unterbrechung. „Video-Konvertierung ist Teil von Kvertis Pro. Einmal kaufen, für immer nutzen.“ Button „Mehr erfahren“.

## Farben und Material

- Hintergrund: Mica (Fenster), Acrylic für Flyouts. Fallback auf Windows 10: einfarbig aus den eigenen Tokens.
- Akzent: **eigene Farbtokens statt Systemakzent** (ADR-017 in `03-architektur.md`). Mint ist die einzige Aktionsfarbe; der Start-Button ist das einzige große Element in Mint. Die Dateiart-Farben sind Bedeutungsträger und dürfen nicht mit dem Systemakzent kollidieren, deshalb wird `SystemAccentColor` nirgends verwendet. Hell/Dunkel folgt dem System; im Hohen Kontrast gelten ausschließlich die Systemfarben.
- Karten: eigener Kartenhintergrund (`panel`) und Linie (`line`) aus den Tokens, Radius 8 px, Schatten `ThemeShadow` mit Tiefe 8–16 je Zustand.
- Status: Erfolg Mint, Fehler Koralle, Warnung Bernstein. Nie nur Farbe als Informationsträger, immer Icon + Text.

Farbtokens (1:1 aus dem Entwurf `design/oberflaeche-mischentwurf.html`; Ressourcenschlüssel in
`Kvertis.App/Themes/KvertisColors.xaml`, Schlüsselmuster `Kv<Token>Color` und `Kv<Token>Brush`):

| Token | Bedeutung | Dunkel | Hell |
|---|---|---|---|
| `bg` | Fensterhintergrund | `#0c0f11` | `#f4f5f6` |
| `panel` | Karten, Flächen | `#14181b` | `#ffffff` |
| `panel2` | zweite Flächenstufe | `#1a1f23` | `#eef0f2` |
| `tief` | vertiefte Flächen (Eingabefelder, Bahnen) | `#0f1416` | `#e7eaec` |
| `line` | Linien, Ränder | `#262d32` | `#dde1e4` |
| `line-stark` | betonte Linien | `#3a454b` | `#b6bec4` |
| `ink` | Text | `#e7ecee` | `#171c1f` |
| `muted` | Zweittext | `#8b959c` | `#5a656c` |
| `leise` | Dritttext, Beschriftungen | `#7f8a91` | `#5b646a` |
| `mint` | Aktion, Erfolg, Fokus | `#6fe0bf` | `#0a6851` |
| `auf-mint` | Text auf Mint | `#08110e` | `#ffffff` |
| `mint-rahmen` | Rahmen von Mint-Flächen | `#2b5a4d` | `#93cfbd` |
| `mint-flaeche` | Mint-Hintergrund (schwach) | `#12201c` | `#e6f5f0` |
| `blue` | Bilder | `#6ba7ff` | `#1a53a8` |
| `violet` | Audio | `#b28cff` | `#5b39a3` |
| `amber` | Video, Warnung | `#f2b45a` | `#7a5104` |
| `amber-rahmen` | Rahmen von Warnflächen | `#5e4722` | `#e0c692` |
| `cyan` | Dokumente | `#4fc3e8` | `#095c78` |
| `model` | 3D-Modelle | `#f08fd0` | `#9a2f7d` |
| `coral` | Fehler | `#f07a6a` | `#9e3123` |
| `coral-rahmen` | Rahmen von Fehlerflächen | `#5e2f29` | `#e8b5ac` |
| `paper` | Blätter (Dateien im Flug) | `#f2f0ea` | `#ffffff` |
| `paper-linie` | Linien auf Blättern | `#cfcbc1` | `#d8d5cd` |
| `glas` | halbdurchsichtige Überlagerung | `#14181bcc` | `#ffffffcc` |
| `schatten` | Schattenfarbe | `#000000a0` | `#1a232a40` |

Die Textfarben auf den jeweiligen Flächen sind im Entwurf gegen 4,5:1 geprüft; die Artfarben in Hell sind bewusst dunkler, damit sie auch als Text tragen.

Noch nicht als XAML-Ressourcen angelegt sind `leise`, `amber-rahmen`, `coral-rahmen`, `paper`, `paper-linie`, `glas` und `schatten`. Sie kommen mit den Etappen, die sie brauchen (Blätter und Überlagerung im Übergang, Warn- und Fehlerflächen, Schatten der Karten); bis dahin bleibt die Tabelle die verbindliche Quelle der Werte.
- Typografie: Segoe UI Variable, Fluent-Typografie-Stufen (`TitleTextBlockStyle`, `BodyTextBlockStyle`, `CaptionTextBlockStyle`).
- Icons: Segoe Fluent Icons (System), Ergänzungen aus Fluent UI System Icons (MIT).

## Animationen (Composition API)

| Element | Animation | Dauer | Hinweis |
|---|---|---|---|
| Ablagefläche bei Drag-over | Skalierung 1.02, Schatten wächst | 150 ms | |
| Neue Job-Karte | Einblenden von unten, 12 px Versatz | 250 ms | Gestaffelt 40 ms pro Karte |
| Start der Konvertierung | Karte kippt 6° um die X-Achse und zurück, Rand leuchtet | 300 ms | Dezent, einmalig |
| Fortschritt | Balken plus wandernder Lichtschimmer am Kartenrand | fortlaufend | Nur bei laufenden Jobs |
| Fertig | Haken skaliert 0 → 1 mit leichtem Überschwingen | 200 ms | |
| Flyouts | Fluent-Standard (Slide + Fade) | System | |

Alle Animationen respektieren die Systemeinstellung „Animationen reduzieren“ (`UISettings.AnimationsEnabled`): dann nur Ein-/Ausblenden.

Die partikelreichen Flächen des Entwurfs (Galaxie, Pixelwirbel, weißes Loch, Übergangs-Überlagerung) laufen nicht über die Composition API, sondern über eine Win2D-Zeichenschicht; bei „Animationen reduzieren“ und im Hohen Kontrast wird sie nicht erzeugt (ADR-018 in `03-architektur.md`).

## Barrierefreiheit

- Jedes Bedienelement hat `AutomationProperties.Name`; Job-Karten haben einen zusammengesetzten Namen („foto.heic, HEIC nach JPG, wartend“).
- Fortschritt wird als `ProgressBar` mit `AutomationProperties.LiveSetting=Polite` gemeldet; Abschluss und Fehler als `Assertive`.
- Vollständige Tastaturbedienung: Tab-Reihenfolge Ablagefläche → Job-Liste → Aktionsleiste. Enter auf Start, Entf entfernt die markierte Karte, Strg+O öffnet den Dialog, Strg+V fügt ein, Leertaste pausiert.
- Hoher Kontrast: Karten bekommen sichtbare Ränder (`SystemColorWindowTextColor`), Mica wird deaktiviert, Schatten entfallen.
- Mindestgröße Klickziele 32 × 32 px, Textkontrast ≥ 4,5:1.
- Fensterskalierung 100–300 % getestet, Mindestfenster 640 × 480 px mit umbrechendem Layout.

## Sprache

- Kurze, freundliche Sätze. Keine Fachbegriffe im Hauptweg („Für E-Mail verkleinern“ statt „Bitrate reduzieren“).
- Fehlermeldungen: Erst was passiert ist, dann was der Nutzer tun kann. Beispiel: „Diese Datei ist beschädigt und kann nicht gelesen werden. Versuchen Sie, sie erneut von der Quelle zu kopieren.“
- Alle Texte in `Strings/de-DE/Resources.resw` und `Strings/en-US/Resources.resw`. Schlüssel nach Muster `Screen_Element_Zweck` (`Main_Start_Button`, `Error_CorruptFile_Body`).

## Logo und Icon

Platzhalter: ein abstraktes Symbol aus zwei ineinander übergehenden Formen (Wandlung), einfarbig auf Mint (ADR-017). Kein Bezug zu bestehenden Marken. Wird später ersetzt; alle Größen liegen unter `Assets/` und werden aus einer SVG-Quelle erzeugt.

## Umsetzungsstand und Abweichungen (UI, 2026-09-23)

Erste Umsetzung in `src/Kvertis.App` (noch nicht unter Windows gebaut). Abweichungen vom Entwurf oben, bewusst klein gehalten:

- **Mehrfachauswahl:** Statt eines eigenen Format-Chips „Alle: → JPG“ in der Aktionsleiste gibt es im „Mehr“-Panel jeder Karte „Auf alle anwenden“ (Format, Qualität, Zielgröße, Metadaten, Dateiname für alle bereiten Karten derselben Kategorie). Grund: ein Bedienelement weniger in der Aktionsleiste; nachrüstbar.
- **„Mehr“-Panel:** Auflösung, Abtastrate und Bitrate sind noch nicht einzeln einstellbar; sie kommen aus der gewählten Voreinstellung (`PresetCatalog`). Qualität, Zielgröße, Metadaten und Dateiname sind umgesetzt.
- **Vorschau Audio:** Zwei Abspielelemente (Original und 10-Sekunden-Ausschnitt), noch ohne Wellenform.
- **Karten-Schatten:** Karten haben Rand und Kartenhintergrund aus den Theme-Ressourcen, aber noch keinen `ThemeShadow` (Tiefe je Zustand). Grund: Schatten brauchen unter Windows gezielte Tests (Hoher Kontrast, Performance bei vielen Karten).
- **Schmale Fenster:** Unter 900 px Breite rutschen die Buttons der Aktionsleiste in eine zweite Zeile (`AdaptiveTrigger`).
- **Tastatur:** Strg+O, Strg+V (nicht in Textfeldern), Entf und Leertaste auf der markierten Karte, Enter auf einer Karte startet. Enter auf dem Start-Button wirkt wie immer.

## Entwurfsstand Oberfläche (2026-09-24)

Die HTML-Entwürfe unter `design/` gehen über die Screens oben hinaus. Verbindlich für die weitere Umsetzung ist `design/ENTSCHEIDUNGEN.md`, zusammengeführt in `design/oberflaeche-mischentwurf.html`. Kurzfassung:

- **Ablauf in drei Schritten** mit rotem Faden oben: Dateien (Fächer-Galaxie) → Ziel → Umwandeln (Pixelwirbel, weißes Loch als Ziel).
- **Ziel in Schritt 3:** rechts ein weißes Loch mit gebündelten Bahnen statt eines Zielordners. Die Pixel fliegen in der Farbe ihrer Dateiart aus dem Wirbel, jede fertige Datei wird ein Planet auf ihrer Bahn; bis 10 Dateien eine Bahn je Datei, darüber höchstens 10 Bahnen mit mehreren Plätzen. Darunter „3 von 7“, der Speicherort und „Speicherort ändern“. Dateien mit eigenem Ziel fliegen in den kleinen Knopf daneben.
- **Schritt 2 „Ziel“:** links je Dateiart ein kleines Universum aus flachem Linien-Symbol und Ring, farbig nur die gewählte Art, Bewegung nur beim Wechsel. Daneben die Dateien mit „Alle gleich / Jede einzeln“, in der Mitte die Wege durchs Loch, rechts die Zielformate mit Größe.
- **Ausgabe-Einstellungen rechts:** oben eine Note von 0 bis 100 für die Qualität, darunter eine Größenleiste von Rot bis Grün (Zielgröße je Datei, Marken wie „E-Mail“). Darunter zugeklappte Zonen, jede mit eigenem Balken (z. B. Schärfe, Details, Klang, Bewegung), dazu „Zielgröße genau“ und „Was sich ändert“. Das ersetzt das „Mehr“-Panel aus Abschnitt 3.
- **Übergänge:** Wurmloch. Das schwarze Loch bleibt über alle Schritte dasselbe Objekt: Es saugt die Fächer aus Schritt 1 ein und gibt sie links als Artwahl von Schritt 2 wieder frei, danach gibt es die Dateien als Blätter an den Eingangsstapel von Schritt 3 und geht im Pixelwirbel auf. Die Seiten selbst ändern sich dabei nicht.
- **Abschluss:** Eingang und Speicherort blenden aus. Die Planeten drehen am weißen Loch hoch, das Loch zehrt an ihnen (Staub spiralt in ihrer Laufrichtung hinein). Dann kreisen schwarzes und weißes Loch im selben Drehsinn um einen gemeinsamen Mittelpunkt, der in die Mitte wandert, immer enger und schneller, bis sie verschmelzen. Kurze Stille, Supernova, Fenster, Kopfleiste und Zeilen beben mit. Danach legen sich die Planeten als Ring um die Mitte, darin Häkchen und Bericht („Alle 7 Dateien umgewandelt“, gesparte Größe, „Ordner öffnen“, „Neue Runde“). Fehlgeschlagene Dateien liegen wieder im Eingang, ihr Platz im Ring ist ein korallener Kreis, der Bericht sagt „6 von 7“. Bei „Animationen reduzieren“ stehen sofort Ring, Häkchen und Bericht.
- **Barrierefreiheit:** Bei „Animationen reduzieren“ stehen alle Bewegungen still; Note und Balken haben `role="meter"` bzw. in WinUI `AutomationProperties.Name` mit Wert.


## Umsetzungsstand Oberfläche, Etappe 0 (2026-09-25)

Fundament für Fächer-Galaxie, Zoom-Wege und Pixelwirbel. Umgesetzt in `src/Kvertis.App`:

- **Farbtokens** in `Themes/KvertisColors.xaml` mit den Theme-Wörterbüchern `Default` (dunkel), `Light` und
  `HighContrast`. Schlüssel `Kv<Token>Color` und `Kv<Token>Brush`, Werte 1:1 aus der Tabelle oben. Im Hohen
  Kontrast verweist jeder Token auf eine Systemfarbe (`SystemColorWindowColor`, `SystemColorWindowTextColor`,
  `SystemColorHighlightColor`, `SystemColorHighlightTextColor`, `SystemColorButtonFaceColor`,
  `SystemColorGrayTextColor`); alle fünf Artfarben fallen dort auf die Textfarbe zusammen.
- **Akzent der Standard-Bedienelemente:** Dieselbe Datei überschreibt in `Default` und `Light` die
  Akzent-Ressourcen von WinUI (`AccentFillColor*Brush`, `AccentTextFillColor*Brush`,
  `TextOnAccentFillColor*Brush`, `AccentControlElevationBorderBrush`, `AccentButton*`, `SliderTrackValueFill*`,
  `SliderThumbBackground*`, `ToggleSwitch*On*`, `CheckBoxCheck*Checked*`, `RadioButton*Checked*`,
  `ProgressBarForeground`, `ProgressRingForegroundThemeBrush`, `Hyperlink*Foreground`,
  `ListViewItemSelectionIndicator*`, `InfoBadge*`) auf Mint. Die Blattschlüssel werden einzeln überschrieben,
  weil die `StaticResource`-Verweise in `generic.xaml` schon beim Parsen aufgelöst werden und ein Überschreiben
  der Basis-Pinsel sie nicht erreicht. Im Hohen Kontrast tragen dieselben Schlüssel wieder Systemfarben; sie
  stehen dort nur, damit kein Schlüssel auf das `Default`-Wörterbuch zurückfallen kann.
- **Dreistufiger Rahmen** `Views/StepHeader.xaml`: „Hineinwerfen“, „Ziel“, „Umwandeln“ unter der Titelleiste,
  über dem `ContentFrame`. Erledigte Schritte tragen ein Häkchen, der aktuelle einen gefüllten Mint-Punkt,
  spätere einen leeren Kreis; Zustand zusätzlich als `AutomationProperties.ItemStatus`. Der Rahmen zeigt sich
  nur auf den drei Schritt-Seiten.
- **Schritt-Navigation** `Services/StepNavigationService.cs` mit `WorkflowStep { Drop, Target, Convert }` über
  dem bestehenden `INavigationService` (neu: `AppPage.Target`, `AppPage.Convert`, `INavigationService.CurrentPage`).
- **Platzhalterseiten** `Views/TargetPage.xaml` und `Views/ConvertPage.xaml` mit Überschrift und Hinweistext.
  Der vollständige Ablauf (Dateien, Format, Start, Fortschritt) liegt unverändert auf `MainPage` = Schritt 1.
- **Ruhige Ansicht** `Services/MotionSettings.cs` (`IMotionSettings`): liest `UISettings.AnimationsEnabled` und
  `AccessibilitySettings.HighContrast`, meldet Änderungen auf dem UI-Thread. `CardAnimations` und `StepHeader`
  fragen nur noch diesen Dienst.

**Abweichung:** Der Dateiname lautet `Themes/KvertisColors.xaml` statt des in ADR-017 genannten
`Themes/Colors.xaml`, damit er in der Zusammenführung mehrerer Wörterbücher eindeutig bleibt und nicht mit
`Colors.xaml` anderer Bibliotheken verwechselt wird. ADR-017 ist entsprechend nachgezogen.

**Offen:** Die drei Schritte sind alle anklickbar; das Vorwärtsgehen wird später an Bedingungen geknüpft
(Dateien vorhanden, Ziel gewählt). Win2D ist nach ADR-018 vorgesehen, in dieser Etappe aber bewusst nicht
eingebunden. Die Farbtokens sind angelegt, aber außerhalb von `StepHeader` noch nicht auf die bestehenden
Ansichten angewandt; Karten und Job-Liste nutzen weiter die WinUI-Fluent-Pinsel.

## Umsetzungsstand Schritt 2 „Ziel“, Etappen B–D (2026-09-25)

Umgesetzt nach `docs/entwuerfe/schritt-2-ziel.md` und dem Abschnitt „Schritt 2 „Ziel““ in `03-architektur.md`
(ADR-019, ADR-020). Reines XAML, keine Zeichenschicht.

- **Sitzung** `Services/WorkflowSession.cs`: `IWorkflowSession` mit `Staged`, `Plan`, `Location`, `Previous`,
  `Changed`, `SetStaged` und `Reset`; Singleton in `ServiceRegistration`. Dazu `StagedFile`, `TargetPlan`,
  `PlannedConversion`, `SkippedFile`.
- **Reine Logik** `Services/TargetPlanner.cs` (ohne WinUI): Reihenfolge der Arten, Schnittmenge der Formate,
  Farbtoken je Art, logarithmische Größenskala, Plan mit Freemium-Grenzen. `Services/Debouncer.cs` entprellt
  über `TimeProvider` (50 ms). `FreemiumPolicy` hat jetzt `IsKindLocked`, `BatchLimit` und `Limits`.
- **ViewModels** unter `ViewModels/Target/`: `TargetPageViewModel`, `KindGroupViewModel`,
  `TargetFileViewModel`, `TuningPanelViewModel`, `SizeBarViewModel`, `ZoneViewModel`, `EffectViewModel`,
  `PreviousSettingsViewModel`, `PathRowViewModel`.
- **Seite** `Views/TargetPage.xaml`: links Artenliste mit Farbpunkt, Modus-Umschalter und Dateikarten; Mitte
  die Wege je Datei; rechts Zielformat, Ring (ein `Slider` mit eigenem `ControlTemplate`, Pfeiltasten ±5),
  Größenleiste mit 24 Farbsegmenten und Marken, Zonen als `Expander` mit `ProgressBar` und `Slider`,
  „Zielgröße genau“, „Weiteres“, „Was sich ändert“. Untere Leiste mit Vertrauenszeile, Summe, „Zurück“ und
  „Weiter: Umwandeln“ als einzigem Mint-Knopf. `AdaptiveTrigger` ab 640 px; im Hohen Kontrast werden die
  Farbsegmente der Leiste ausgeblendet (nur Text).
- **Verdrahtung:** `MainPage` hat den Knopf „Weiter: Ziel“ (`MainViewModel.GoToTargetCommand`) und spiegelt
  die fertigen Karten laufend in `IWorkflowSession.Staged`. Schritt 2 schreibt den Plan, `ConvertPage` zeigt
  Anzahl und Summe. `HistoryViewModel.AgainAsync` setzt `Previous` und springt nach Schritt 2. Die
  Schrittleiste macht Schritt 2 anklickbar, sobald mindestens eine Datei abgelegt ist, Schritt 3 erst mit
  Plan. Der alte Start-Weg auf `MainPage` bleibt unverändert bestehen.

**Abweichungen vom Arbeitsblatt:**

- Die Miniatur auf den Dateikarten fehlt noch; die Karte zeigt Name, Größe, Maße und Warnung. Grund: Der
  Pfad zur Vorschau liegt in `StagedFile.ThumbnailPath`, das Dekodieren gehört zu einer eigenen kleinen
  Bildquelle, die zusammen mit der Zeichenschicht kommt.
- Die Größenleiste hat eine feste Breite von 320 px, weil Marken und Segmente ohne Zeichenschicht an einer
  festen Breite ausgerichtet werden. Die rechte Spalte ist entsprechend 360 px breit.
- Der Ring zeigt die Note als Zahl und Wort in einem farbigen Kreis, nicht als gefüllten Bogen; ein Bogen
  bräuchte eine gezeichnete Fläche (ADR-018).
- Die Pro-Karte „Batch zu groß“ steht in der rechten Spalte, die Karte „Video braucht Pro“ in der Gruppe.
- Für Bindungen mit möglicherweise leerem Datenkontext (gewählte Art) werden zwei Konverter benutzt
  (`ShowConverter`, `BrushKeyConverter` in `Helpers/Converters.cs`); `x:Bind` bleibt überall sonst.
- Entwicklungshilfe: In Debug-Builds legt die Umgebungsvariable `KVERTIS_STAGE_FILES` (Pfade mit `;`)
  Dateien ab und öffnet Schritt 2. Im ausgelieferten Build existiert der Zweig nicht.

## Umsetzungsstand Schritt 3 „Umwandeln“, Teilaufgaben A–C (2026-09-25)

Umgesetzt nach `docs/entwuerfe/schritt-3-umwandeln.md` und dem Abschnitt „Schritt 3 „Umwandeln““ in
`03-architektur.md` (ADR-020, ADR-021). Reines XAML, keine Zeichenschicht.

- **Zielpfad-Vorschau** `Services/TargetPathPlanner.cs` (ohne WinUI): `EffectiveLocation`, `NeedsFolder`,
  `PreviewAll` mit `TargetPathPreview`. Dieselbe Rechnung wie der `JobRunner` beim Start, mit Nummerierung
  gegen Datenträger und gegen die übrigen Zeilen der Runde (ADR-007).
- **Koordinator** `Services/ConversionCoordinator.cs` (ohne WinUI): `IConversionCoordinator` mit `Start`,
  `RelocateWaiting`, `PauseAll`, `ResumeAll`, `CancelAll`, `Reset`, `Jobs`, `State`, `Overall`, `Report`,
  `Changed`. Einzige Stelle der App, die für Umwandlungen mit der Queue spricht; Singleton.
- **Öffnen und Zeigen** `Services/ShellLauncher.cs` (`IShellLauncher`), genutzt von Zeile, Bericht und Verlauf.
- **Sitzung** `IWorkflowSession.Location` ist `OutputLocation?`, neu `OwnLocations` und `SetOwnLocation`.
- **ViewModels** unter `ViewModels/Convert/`: `ConvertPageViewModel`, `ConvertRowViewModel`,
  `LocationCardViewModel`, `RoundReportViewModel`.
- **Seite** `Views/ConvertPage.xaml`: links Eingang mit Farbpunkten, übersprungenen Dateien und Pro-Karte;
  Mitte `SwirlHost` (`KvDeepBrush`, Radius 8, Mindesthöhe 220) mit höchstens zwei laufenden Karten, Zeile
  „und n weitere laufen“ und dem Abschlussbericht; rechts „fertig n von N“ und die Speicherort-Karte mit
  Flyout (drei `RadioButton` plus „Ordner wählen…“), Ordner-Ablage aus dem Explorer und Tasche-Knopf; darunter
  die Liste Format → Format mit Zielpfad, „Ändern“, „Öffnen“, „Im Ordner zeigen“ und Fehlertext aus
  `Error_*`. Untere Leiste mit Vertrauenszeile, Gesamtfortschritt, „Zurück“, „Pause“/„Weiter“, „Abbrechen“
  (mit Rückfrage), „Umwandeln · n Dateien“ als einzigem Mint-Knopf und „Neue Runde“ nach dem Lauf.
  `AdaptiveTrigger` ab 900 px stapelt die drei Spalten.
- **Abbau:** `MainPage` gibt Start, Zielordner-Dropdown, Gesamtfortschritt, „Alle pausieren“, „Alle
  abbrechen“, „Fertige entfernen“, Pro-Karte und Ansage-Region ab; der einzige Mint-Knopf ist „Weiter: Ziel“.
  Die Job-Karte zeigt nur noch Miniatur oder Typ-Symbol, Formatschild, Name, Größe, Maße, Warnung, die
  Zustände Erkennen/Bereit/Abgelehnt und „Entfernen“. `Views/MorePanel.xaml(.cs)` und
  `Views/FormatPickerFlyout.xaml(.cs)` sind gelöscht, die frei gewordenen Ressourcen-Schlüssel ebenfalls.
  `MainViewModel` kennt die Queue nicht mehr. Die Schrittleiste sperrt Schritt 1 und 2, solange die Runde
  läuft oder pausiert (`Steps_Status_Locked`).

**Abweichungen vom Arbeitsblatt:**

- `TargetPathPreview` trägt zusätzlich `BatchIndex`, damit der Koordinator die Jobs allein aus den Vorschauen
  bauen kann und die Nummerierung des `{n}`-Platzhalters nur an einer Stelle entsteht.
- `RoundReport.Failures` ist eine Liste von `RoundFailure(Job, Error)` statt einer Tupel-Liste; ein Record ist
  in Tests und in XAML besser lesbar.
- Die Ansage des Gesamtfortschritts wird direkt über `TimeProvider` zurückgehalten (Zählerwechsel oder
  5 Sekunden) statt über `Debouncer`; der `Debouncer` würde hier nur eine zweite Zeitquelle einführen.
- `JobItemState` hat nur noch `Detecting`, `Ready`, `Rejected`; damit entfallen auch die Schlüssel
  `Card_State_Queued/Running/Paused/Completed/Failed/Cancelled`, `Card_PresetList.*` und `Card_Progress.*`,
  die das Arbeitsblatt nicht einzeln aufzählt.
- `Main_Output_Same/Sub/Custom` werden weiter von der Einstellungsseite gebraucht und heißen dort jetzt
  `Settings_Output_Same/Sub/Custom`; gelöscht statt umbenannt wäre die Einstellungsseite kaputt.
- `Convert_NoPlan_Text` ist zu `Convert_NoPlan_Text.Text` geworden, weil der Hinweis jetzt fest in der Seite
  steht und nicht mehr aus dem Code kommt.
- Der Bericht zeigt die Ersparnis als vorzeichenbehaftete Änderung (`Format_Change_Percent`), nicht als
  „71 % gespart“; derselbe Text wird schon im Verlauf und auf der Karte benutzt.
- `SettingsViewModel` setzt weiterhin `IJobQueue.MaxParallel`; das ist eine Einstellung, keine Umwandlung,
  und `App.OnWindowClosed` ruft weiterhin `JobQueue.StopAsync`.

## Abweichungen Schritt 1 „Fächer-Galaxie“ (Teil B/C/D, 2026-09-25)

Grundlage: `docs/entwuerfe/schritt-1-galaxie.md`. Abweichungen und ihr Grund:

- **`RemoveFromVisualTree()` wird nicht gerufen, die Fläche bleibt bei der Navigation stehen.** Der Aufruf
  stürzt mit Win2D 1.4.0 und diesem Windows App SDK reproduzierbar ab (Zugriffsverletzung in
  `Microsoft.Graphics.Canvas.dll`, auch mit leerem `Draw`). Deshalb behält jede `GalaxyCanvas` ein
  einziges `CanvasAnimatedControl` über die Lebensdauer der gecachten Seite; `Suspend()` pausiert nur.
  Abgebaut (Renderer auf dem Spielschleifen-Thread entsorgt, pausiert, aus dem Eltern-Grid genommen) wird
  nur bei einem Wechsel der Bewegungs-Einstellung oder nach einem Zeichenfehler. Einzelheiten im Nachtrag
  zu ADR-022 in `03-architektur.md`.
- **`x:Uid` in `DataTemplate`s nur für feste Texte.** Ein `x:Uid` in einer Vorlage funktioniert (z. B.
  `Card_Remove_Button` in der Karte „Nicht umwandelbar“). Der Abbruch des XAML-Compilers (nicht
  formatierbarer interner Fehler) trat in der rechten Liste des Zooms auf, zusammen mit zwei Vorlagen
  desselben `x:DataType` in einer Datei (siehe nächster Punkt); welcher der beiden Umstände ihn allein
  auslöst, ist nicht getrennt geprüft. Der Text „empfohlen“ kommt ohnehin aus dem ViewModel
  (`Galaxy_Recommended_Text`), weil derselbe Text auch als `HelpText` für Bildschirmleser dient; der
  Schlüssel `Galaxy_Recommended_Tag.Text` entfällt.
- **Vorzugsformat aus dem Zoom.** Wer aus dem Zoom mit „Weiter: Ziel“ weitergeht, gibt Schritt 2 neben der
  Art (`IWorkflowSession.FocusKind`) auch das empfohlene Ziel mit (`IWorkflowSession.PreferredOutput`),
  sofern das links gewählte Format wirklich eingeworfen oder vom Nutzer angeklickt wurde; der automatische
  Wechsel allein drückt keinen Wunsch aus. Schritt 2 wählt es in der Gruppe dieser Art vor, wenn es in der
  Schnittmenge aller Dateien liegt, sonst bleibt der Vorschlag der Engine.
- **Kein schwebendes Tastenschild.** Die Tastenkürzel der Seite zeigen kein eigenes Schild
  (`KeyboardAcceleratorPlacementMode="Hidden"`), weil es dem Zeiger folgend über die Galaxie und im Zoom über
  die Schrittleiste wanderte. Strg+O steht im Tooltip von „Dateien wählen“, Strg+V in der Einwurf-Zeile.
- **Einwurf-Karte nur im leeren Zustand.** Die Karte „Dateien hierher ziehen“ steht nur, solange keine Datei
  eingeworfen ist, kleiner und mittig; danach ersetzt sie eine dezente Zeile am Fuß der Galaxie, damit keine
  Bahn verdeckt wird.
- **Zwei `DataTemplate`s mit demselben `x:DataType` in einer Datei** brechen denselben Compiler ebenfalls;
  deshalb hat die rechte Liste des Zooms einen eigenen Typ `PathTargetViewModel` neben
  `PathFormatViewModel` (gemeinsame Basis `PathRowBase`).
- **`VisualState`-Setter auf `RowDefinition`/`ColumnDefinition`** brechen den Compiler ebenso. Der schmale
  Aufbau unter 900 px (fünf Spalten à 200 px, seitliches Rollen) und die Höhe der Galaxie stehen deshalb im
  Code-behind von `MainPage` statt in einem `AdaptiveTrigger`.
- **Bezugslinien im Zoom** zeichnet der Renderer nicht getrennt: die Szene gibt die Ankerpunkte der Zeilen
  nicht einzeln heraus, und der gezeichnete Weg läuft ohnehin vom Rand der linken Zeile durch das Loch bis
  zum Rand der rechten Zeile. Das Glühen von Bahnen und Loch ist ein zweiter, breiter und blasser Strich
  statt eines Weichzeichners (ADR-022 verbietet Blur-Effekte je Bild).
- **Bilddrossel bei Akkubetrieb** (1/30 s) ist nicht umgesetzt; die Fläche läuft immer mit 1/60 s.
- **Fächer-Zeilen** erscheinen erst, wenn die Erkennung die Art einer Datei kennt; eine Datei ohne Art hätte
  kein Fach. Das Erscheinen ist die Einblende-Animation aus `CardAnimations`, die Zahl hüpft über
  `CardAnimations.CountHop`.
- **Entwicklungshilfe:** `KVERTIS_REDUCED_MOTION=1` erzwingt in Debug-Builds die ruhige Ansicht
  (`SystemMotionSettings`), damit sie ohne Änderung einer Windows-Einstellung geprüft werden kann.

## Umsetzungsstand Übergänge und Abschluss, Teile B–E (2026-09-25)

Grundlage: `docs/entwuerfe/uebergaenge.md` und ADR-023. Bewusste Abweichungen vom Arbeitsblatt, jeweils mit Grund:

**Wirbel und Abschluss (Teile D und E):**

- **Zeilen beben einzeln.** Das Blatt strich das Beben je Zeile; umgesetzt ist es wie im Mischentwurf (`beben`): Kopfleiste und die Zeilen der Liste schwingen nacheinander aus (40 ms + 70 ms je Zeile, wechselndes Vorzeichen, Deckel 12 Zeilen), der Fensterinhalt ruckt einmal. Grund: größtmögliche Nähe zum Entwurf, die Zeilen sind `ItemsControl`-Container und lassen sich ohne eigene Vorlage bewegen.
- **Zähler „n von N“ auch in der Zeichenfläche.** Das Blatt wollte nur den XAML-`DoneText`; der Renderer zeichnet den Zähler zusätzlich unter dem weißen Loch (Text aus `Swirl_Counter_Of`, Zahl 15 pt, Zusatz 11 pt). Grund: der Entwurf zeigt ihn dort; die Information bleibt im XAML-Text mit `LiveSetting=Polite`.
- **Fortschrittskarten als Streifen am unteren Rand.** Die zwei Karten der laufenden Dateien liegen kompakt und waagerecht unten in der Fläche statt in der Mitte, damit sie Stapel, Wirbel und weißes Loch nicht verdecken; ohne Zeichenfläche (Reduced Motion) stehen sie senkrecht wie bisher. *Überholt durch Abgleich Teil D:* mit Zeichenfläche entfallen die Karten ganz (laufende Dateien nur im Wirbel und in der Liste), ohne Zeichenfläche stehen sie oben in der Bühnenmitte.
- **„Neue Runde“ auch in der Berichtskarte.** Neben „Ordner öffnen“ steht ein zweiter „Neue Runde“-Knopf in der Karte (wie `w5-bericht` im Entwurf); der Knopf der Fußleiste bleibt.
- **Blattschrift Consolas.** Die Blatt-Attrappen (Reiter, Formatkürzel, Dateiname) und der Zähler nutzen Consolas statt der Mono-Schrift des Entwurfs, weil Consolas auf jedem Windows vorhanden ist und keine Schrift mitgeliefert wird.
- **Bag-Anker außerhalb der Fläche.** Die Tasche liegt in der rechten Spalte; die Pixel einer Datei mit eigenem Ziel fliegen zum gemessenen Knopf und verlassen dabei die Zeichenfläche. Der Entwurf hatte die Tasche innerhalb der Bühne. *Überholt durch Abgleich Teil D:* die Tasche liegt jetzt wie im Entwurf auf der Bühne unter dem weißen Loch.
- **Reduced Motion ohne Finale-Timer.** `SwirlFeed.RoundFinished(…, animated:false)` startet keinen Timer, der Bericht erscheint sofort; Eingang und Speicherort werden nach dem Ausblenden auch für Tastatur und Erzähler entfernt (`Visibility=Collapsed` nach 300 ms) und vor dem Einblenden zurückgeholt.

**Überlagerung (Teile B und C):**

- **Overlay `IsHitTestVisible=false`, Sperre über die Stage.** Die Überlagerung schluckt keine Zeiger selbst; `ITransitionStage.SetInputLocked` sperrt Schrittleiste und Frame. Grund: ein durchsichtiges Steuerelement über allem würde auch nach dem Flug Klicks fangen, wenn ein Aufräumen ausbleibt.
- **Overlay sichtbar mit Deckkraft 0 statt `Collapsed`.** Die Swapchain von `CanvasAnimatedControl` wird bei `Collapsed` verworfen und neu aufgebaut; mit Deckkraft 0 bleibt sie erhalten, der erste Flug startet ohne Verzögerung.
- **Haltephase mit erstem Bild vor dem Seitenwechsel.** `HoldAsync` zeichnet das erste Bild der Szene und hält es, erst dann verlässt die alte Seite den Frame. Grund: sonst ein leeres Bild zwischen alter Seite und Overlay.
- **Zielelemente ab `FlightsEnded` sichtbar.** Die Seite zeigt ihre echten Elemente, sobald die Flüge enden, nicht erst nach dem Ausblenden des Lochs; so gibt es keinen Sprung zwischen Geist und Element.
- **3→2 als Überblenden (240 ms).** Statt eines schlichten Seitenwechsels blendet `NavigateFaded` die Zielseite ein; kein Geisterflug, aber kein harter Schnitt.
- **Galaxiepause über `ITransitionService.Changed`.** `MainPage` und `ConvertPage` pausieren ihre Fläche selbst, wenn `IsTransitioning` wechselt; der Dienst kennt die Hosts nicht. Ergebnis wie im Blatt: höchstens eine Zeichenschleife zur Zeit.

## Umsetzungsstand Abgleich Mischentwurf

Grundlage: `docs/entwuerfe/abgleich-mischentwurf.md`. Vergleichsbilder nach Abschnitt 5 liegen nur lokal (`docs/entwuerfe/bilder/`, in `.gitignore`).

### Teil A · Fundament, Rahmen, Schrittleiste (2026-09-25)

Umgesetzt: neue Tokens und Mischpinsel (1.3) in `Themes/KvertisColors.xaml` (dunkel, hell, Hoher Kontrast nur Systemfarben), neue Datei `Themes/KvertisControls.xaml` (Textstile 1.2, Standard-Elemente 2.1 a, Stile 2.1 b, `KvPrimaryButtonStyle`), Fokusring 1.6, Titelleiste und Schrittleiste nach 3.1. Bewusste Abweichungen, jeweils mit Grund:

- **Titelleiste 48 statt 42 px hoch.** Wie im Blatt: Die Zeile gehört zum Raster von `MainWindow` (nicht zum `TitleBarGrid`, das Teil A allein ändern darf), und die System-Schaltflächen brauchen die Höhe. Im Vergleichsbild liegt deshalb alles unter der Titelleiste 6 px tiefer; Rand, Hintergrund (`KvPanel55`), Titel 13 / SemiBold, Logo 20, Icon-Knöpfe 32 und Schrittleiste 62 decken sich.
- **Farb-Überschreibungen der Standard-Elemente in `KvertisColors.xaml`.** Das Blatt sah sie in `KvertisControls.xaml` vor. Die Pinsel (`ButtonBackground`, `TextControl*`, `ComboBox*`, `Expander*`, `SliderTrackFill*`, `ListViewItemBackgroundPointerOver`) stehen jetzt neben den Akzent-Überschreibungen, weil sie Farben sind und `{ThemeResource Kv*Color}` innerhalb eines Theme-Wörterbuchs nur im selben Wörterbuch verlässlich auflöst. `KvertisControls.xaml` enthält nur Größen, Stile und Vorlagen. Im Hohen Kontrast tragen die Schlüssel die Systemfarben, die WinUI dort selbst nutzt; einzige Ausnahme ist der sichtbare 1-px-Rand des Sekundärknopfs (`SystemColorButtonTextColor` statt transparent).
- **Fokusring über `SystemControlFocusVisualPrimaryBrush`/`…SecondaryBrush`.** Das sind die Schlüssel, die der System-Fokusrahmen tatsächlich liest (Mint außen, `KvBackground` als Spalt); Abstand wie WinUI 1 px Spalt + 2 px Ring (`FocusVisualMargin` −3) statt 2 px Spalt im Entwurf. Hoher Kontrast: Systemwerte.
- **Drei kleine Knopf-Vorlagen statt „Stil ohne Vorlage“.** `KvGhostButtonStyle` (Basis von `KvIconButtonStyle` und den Knoten der Schrittleiste) und `KvOutlineMintButtonStyle` haben eine eigene, schlanke `ControlTemplate`, weil die Hover-Farbe (`KvHover` bzw. Rand Mint) sich per Stil nicht vom globalen `ButtonBackgroundPointerOver` trennen lässt. Dazu `KvPrimaryButtonStyle` (Sockel) sowie `KvDashedBoxStyle` und `KvPaperTagStyle` als `ContentControl`-Vorlagen (gestrichelter Rand bzw. eigene Drehung je Etikett; ein geteilter `RotateTransform` im Stil ginge nicht).
- **`AccentButtonStyle` trägt die Primär-Optik.** Der Schlüssel ist in `KvertisControls.xaml` neu definiert (auf `KvPrimaryButtonStyle`, ohne `MinWidth` 190), damit alle Mint-Knöpfe schon jetzt Sockel und Maße haben, ohne dass Teil A die Seiten anfasst. Die Seiten stellen in B bis F auf `KvPrimaryButtonStyle` um.
- **Primärknopf ohne Glühen und Glanzkante.** `0 12px 24px -8px` und `inset 0 1px 0 #fff5` entfallen (1.5: nur der Sockel); Hover −1 px, gedrückt +3 px, deaktiviert 0,4 ohne Sockel, ohne Übergangsanimation.
- **Kleinzeile der Knoten ist der Zustandstext.** „Aktueller Schritt“, „Erledigt“, „Noch offen“, „Gesperrt …“ (dieselben Wörter wie `AutomationProperties.ItemStatus`) statt der Ergebniszeile des Entwurfs („7 erkannt“, „JPG · MP3“). Die Schrittnamen bleiben die der App („Hineinwerfen“ statt „Dateien“). Titel und Kleinzeile haben die Zeilenhöhen des Entwurfs (20 und 17).
- **Erledigter Knoten auf `KvMintSurface`.** Wie im Blatt; im Entwurf ist das Quadrat dort durchsichtig.
- **Pro-Schild ohne Statustext.** Der Knopf zeigt nur das Schild „PRO“; „Gratis-Version“ bzw. „Kvertis Pro ist aktiv“ steht jetzt im Tooltip (vorher sichtbarer Text, der alte Tooltip-Eintrag „Kvertis Pro“ entfällt). Der Name für den Bildschirmleser bleibt „Kvertis Pro“.
- **Logo** bleibt das App-Symbol aus `Assets/` (20 × 20), nicht die gezeichnete Mint-Form des Entwurfs.
- **`SectionHeaderTextStyle` und `CardBorderStyle`** sind Aliase auf `KvLabelTextStyle` (Rand 0,10,0,4) bzw. `KvCardStyle` (Radius 13, Verlauf, Polster 12). Die Überschriften der Seiten sind noch nicht in Großbuchstaben; das kommt mit den Texten der Teile C bis F.
- **Textfelder global in Mono 12,5** (`.eingabe`), Rand beim Fokus 1 px Mint; im Hohen Kontrast bleiben 2 px.
- **Nicht geprüft im Bild:** Hell und Hoher Kontrast (das Kontrastthema des Systems wird für den Test nicht umgestellt). Die Vorlagen nutzen nur `{ThemeResource Kv*}` und das `HighContrast`-Wörterbuch nur Systemfarben (plus `Transparent` für Kante und Ghost-Rand); Sockel sind dort Fensterfarbe, `KvShadowDepth` ist 0.

### Teil B · Schritt 1 und Zoom-Wege (2026-09-25)

Umgesetzt: 3.2 und 3.3 in `Views/MainPage.xaml(.cs)`, `GalaxyHost.xaml(.cs)`, `TrayControl.xaml(.cs)`, `TrayFileControl.xaml(.cs)`, `PathsOverlay.xaml(.cs)`; neu `Views/ChipWrapPanel.cs` (Chips mit Umbruch, `.chips`) und `Views/FormatChip.cs` (Chip in Artfarbe aus Code). Summe oben links („Noch leer“ / „n Dateien · m umwandelbar in k Arten · r nicht“) aus `MainViewModel`, Ablage-Karte oben mittig, Hinweiszeile „Bahn oder Fach anklicken zum Heranzoomen“, Fächer 188 hoch mit oberem 3-px-Artrand, Beispiel-Chips, Dateizeilen mit Hover-Entfernen, Fußzeile in Mono mit fetten Zahlen, Fußleiste mit `KvPrimaryButtonStyle` „Weiter: Ziel wählen“; Zoom mit Pille „Übersicht“, Titel 17, Listen 250 breit ab oben 58, Zeilen `.knopf-l`/`.knopf-r`, Marke „empfohlen“ als gefüllte Pille, Staffelung 35 ms seitlich. Bewusste Abweichungen, jeweils mit Grund:

- **Kein Fenster-Overlay „Loslassen – Kvertis sortiert selbst“ in Schritt 1.** Der Entwurf zeigt `.win.drag::after` nur in Schritt 2 und 3 (`schritt !== 0`); in Schritt 1 wechselt nur die Ablage-Karte auf Mint mit „Loslassen / Kvertis prüft jede Datei am Inhalt“ (`.welt.zieht .einwurf`). So umgesetzt; der Schlüssel `Main_DropOverlay_Text` aus dem Blatt entfällt.
- **Ablage-Karte bleibt in der Übersicht auch mit Dateien** (wie im Entwurf); die Einwurf-Zeile `EntryBar` am Fuß der Galaxie entfällt. Die Karte behält „Ordner wählen“ als Link neben „Dateien wählen“ (der Entwurf hat nur den Knopf), weil das Hinzufügen ganzer Ordner sonst nur über das Ziehen erreichbar wäre. Der Hinweis „Strg+V fügt ein Bild ein“ steht jetzt im Tooltip der Karte statt als eigene Zeile.
- **Ablage-Karte und Galaxie liegen nicht übereinander wie im Entwurf.** Im Entwurf füllt die Zeichenfläche das ganze Feld und die Fächer liegen unten darüber; in der App bleibt die Galaxie eine eigene Zeile über den Fächern (Zeile `*`, Fächer 188 + 12). Größe und Mittelpunkt der Bahnen legt die Szene fest (Zeichenschicht bleibt unverändert), deshalb sind die Bahnen in der App größer.
- **Fächer-Rahmen aus zwei Rändern.** Ein `Border` kann oben keine andere Farbe tragen als an den Seiten: unten liegt der Rahmen 1/3/1/1 in `KvLine`, darüber ein zweiter Rand nur oben in der Artfarbe mit Deckung 0,7 (leer 0,3, Hover und gewählt 1). Hover-Ring 1 px Art 25 %; Hover-Rand `Kv<Art>Frame60` statt Art 55 % auf `line`; das farbige Glühen (`0 12px 30px -18px`) entfällt (1.5).
- **Beispiel-Chips im `.fc`-Stil der Art, nicht als Papier-Etikett.** Der Entwurf nutzt dort `.fc` (Deckung 0,75, 9,5 px); die Namen sind die `DisplayName` der Formate („OGG (Vorbis)“, „GLB (glTF)“, „Text“) statt der Kurzcodes des Entwurfs, deshalb bricht die Chipzeile bei Audio um.
- **Dateizeile etwa 32 statt 28 px.** Das Maß ergibt sich im Entwurf selbst aus Symbol 22, zwei Zeilen (12 und 10 px bei Zeilenhöhe 1,2) und Polster 3; die App hat dieselben Werte (Zeilenhöhen 14/12). Symbol 22 × 22, Radius 6, Fläche `Kv<Art>Tint12` (Entwurf Art 16 % auf `panel2`).
- **Entfernen-Kreuz 24 sichtbar, 32 Klickfläche** (1.6) mit eigener kleiner Vorlage in `TrayFileControl` (Hover `KvError` 18 % und Text `KvError`); sichtbar bei Hover über der Zeile, bei Fokus auf Zeile oder Kreuz und immer im Hohen Kontrast. Tastaturweg Fachkopf → Zeile → Entfernen und die Namen für den Bildschirmleser sind unverändert.
- **Fachkopf 20 px hoch, Klickfläche 32** (Polster 6 gegen Rand −6); eigene schlanke `ToggleButton`-Vorlage ohne Checked-Fläche, weil die Standardvorlage im Zoom die Akzentfläche zeigt.
- **Eingeworfene Formate im Zoom:** gefüllter Chip (`.fc.da`) mit Text `KvPaperInk` statt `#0b0f12`, ohne Glühen. Hover der linken Zeilen als `KvHover`-Schicht über der Zeile statt Rahmen Art 60 % (die Zeile trägt Art 60 %/`Tint12` nur, wenn gewählt).
- **Zoom-Titel:** Unterzeile 12,5 `KvMuted` (Entwurf `.titel small`), nicht Mono 11,5 wie im Blatt; die Kleinzeile rechts („PNG: 7 Ziele“) kommt neu aus `PathsViewModel.ReachText`.
- **Stile lokal statt in `KvertisControls.xaml`.** `PillBackButtonStyle`, `PathInputRowStyle`, `TrayHeadButtonStyle`, `RowRemoveButtonStyle` liegen in den `Resources` ihrer Views, weil `KvertisControls.xaml` in dieser Etappe parallel von Teil C geändert wird; ein späteres Aufräumen kann sie dorthin ziehen (`KvPillButtonStyle`, `KvOptionStyle`-Variante).
- **Nicht geprüft im Bild:** Hell und Hoher Kontrast. Alle neuen Flächen nutzen `{ThemeResource Kv*}` bzw. über `Ui.KindVariant` die Mischpinsel aus `KvertisColors.xaml`, die auch im `HighContrast`-Wörterbuch definiert sind (Systemfarben). Einschränkung: Pinsel, die im Code-behind gesetzt werden (Fachrahmen, Dateizeile, Chips), werden beim Themenwechsel über `ActualThemeChanged` neu gesetzt.

### Teil E · Verlauf (2026-09-25)

Umgesetzt nach 3.6 in `Views/HistoryPanel.xaml(.cs)`, `Views/MainPage.xaml` (`SplitView` 440 breit, Fläche `KvBackground`) und `ViewModels/HistoryViewModel.cs` (Gruppierung nach Kalendertag, `HistoryDayGroup`, keine neue Ablauflogik): Kopf `.v-kopf` mit Polster 12,22, Rand unten `KvLine`, Fläche `KvPanel45` (neuer Mischpinsel in `KvertisColors.xaml`), Titel 17 / SemiBold, Unterzeile 12 mit Mint-Schloss („Nur auf diesem PC gespeichert“), „Leeren“ als `KvSmallButtonStyle`, Schließen als `KvIconButtonStyle`; Tag-Titel `.tag-titel` im Label-Stil (HEUTE, GESTERN, sonst Wochentag und Datum), bündig mit den Karten und beim Rollen oben fest; Karte `.rk` mit `KvCardStyle`, innerer Oberkante `KvEdge`, `ThemeShadow` Tiefe 8 und Hover-Rand `KvLineStrong`; Kartenkopf mit Zustandssymbol 16 in Artfarbe, Titel „Datei → Ziel“ 13,5 / SemiBold, Kleinzeile Mono 11 (Qualität bzw. Voreinstellung · Größen), Ersparnis-Pille; Knöpfe „Nochmal“ (Mint-Umriss) und „Ordner öffnen“ mit Symbol 14, Polster 10,6, Radius 9, 12 px; eine Dateizeile `.vd` (Radius 10, Rand `KvLine`, Fläche `KvPanel`, Polster 7,10) mit Format-Chip der Art, Mint-Pfeil, Papier-Etikett und Größen (bei Fehlern der Fehlertitel in `KvError`); Leerzustand `.v-leer` zentriert mit 70,20, Titel 15 / SemiBold und Zeile 12,5 `KvMuted`. Neue Ressourcen-Schlüssel (DE/EN): `History_Local_Text.Text`, `History_Day_Today`, `History_Day_Yesterday`, `History_Saving_Text`, `History_Detail_Text`, `History_Empty_Title.Text`, `History_Again_Text.Text`, `History_OpenFolder_Text.Text`; `History_Empty_Text.Text` hat den Text des Entwurfs; `History_Again_Button.Content` und `History_OpenFolder_Button.Content` entfallen (die Knöpfe tragen Symbol und Text, der Name für den Bildschirmleser bleibt). Bewusste Abweichungen, jeweils mit Grund:

- **Zeitleiste schon in dieser Etappe, im Seitenpanel.** Das Blatt hatte sie für die Überlagerung zurückgestellt; der Auftrag nannte die Zeitleiste aus „Zusammengeführt“ (`design/verlauf-konzepte.html`) als Ziel. Sie braucht keine Logik (Uhrzeit aus `HistoryEntry.When`) und passt mit den Maßen des Entwurfs (Zeit 50, Abstand 26, Linie 2 px bei 60, Punkt 10 mit Mint-Ring 2 und Hof 3 in `KvBackground`) in 440 px; die Karten sind damit 320 breit. Die Linie ist einfarbig `KvMintFrame` statt Verlauf nach 30 %, weil sie je Eintrag gezeichnet wird; der gefüllte Punkt für „gerade eben“ (`.runde.neu`) entfällt.
- **Weiter offen (Abschnitt 7):** ganzseitige Überlagerung, Detailbereich rechts, Suche nach Dateinamen und Filter-Knöpfe aus „Zusammengeführt“. Sie brauchen neue Logik im ViewModel (Filter, Suche, Auswahl) und für die Filter das `KvSegmentStyle` aus Teil C, das noch nicht committet ist.
- **Knöpfe unter dem Titel statt rechts im Kartenkopf.** 320 px reichen nicht für Titel, Pille und zwei Knöpfe in einer Zeile. „Anpassen“ und der Aufklapp-Rundknopf fehlen, weil ein Eintrag genau eine Datei ist (heutige Datenlage) und „Nochmal“ schon in Schritt 2 mit der „damals“-Karte landet.
- **Zustandssymbol statt Blätterstapel** (wie im Blatt; der Stapel ist Zeichnung). Die Dateizeile hat keine Spalte „Original da/fehlt“ und keinen durchgestrichenen Namen: Eine Prüfung, ob das Original noch existiert, gibt es im ViewModel nicht (das Blatt nahm sie in `AgainCommand.CanExecute` an), und neue Dateisystem-Logik war nicht Teil dieser Etappe.
- **Linker Rand 1 px `KvLine` am Panel.** Das Panel liegt über der Galaxie; der Entwurf deckt das ganze Fenster ab und braucht keine Trennlinie.
- **Kein Hover-Anheben der Karte (−2 px)**, nur der Randwechsel; der Schatten wird im Code gesetzt und im Hohen Kontrast (`IMotionSettings.IsHighContrast`) weggelassen. Auf dunklem Grund ist er im Vergleichsbild kaum sichtbar, wie im Entwurf.
- **Chip im `KvChipStyle`** (Radius 6, fett) statt `.chip-k` (Radius 4, 500), wie im Blatt.
- **Nicht geprüft im Bild:** Hell und Hoher Kontrast. Alle Flächen nutzen `{ThemeResource Kv*}` bzw. `Ui.KindVariant`; `KvPanel45Brush` ist im `HighContrast`-Wörterbuch auf `KvPanelColor` ohne Deckung gesetzt.

#### Nachtrag Teil E · Verlauf als ganzseitige Überlagerung (2026-09-25)

Umgesetzt nach `design/ENTSCHEIDUNGEN.md` („Verlauf“: Zusammengeführt; „Anpassen aus dem Verlauf“) mit `design/verlauf-konzepte.html` (erstes Fenster) und dem Verlauf-Teil von `design/oberflaeche-mischentwurf.html`. Das `SplitView`-Panel in `MainPage` und `Views/HistoryPanel.xaml(.cs)` entfallen. Neu: `Views/HistoryOverlay.xaml(.cs)` liegt in `MainWindow` über Schrittleiste und `ContentFrame` (Zeilen 1–2, unter der Titelleiste wie `.verlauf` mit `inset: 42px 0 0 0`) und unter der `TransitionOverlay`-Ebene; der Titelleisten-Knopf „Verlauf“ schaltet sie um und trägt offen die `.ikon.an`-Farben (Mint auf `KvMintSurface`). Aufbau: Kopf `.v-kopf` (Schließen, Titel 17, „Nur auf diesem PC gespeichert“, Filter-Segment Alle/Bilder/Audio/Video/Dokumente/3D/Fehler mit 8-px-Quadrat in der Artfarbe über `KvSegmentBarStyle`/`KvSegmentStyle`, „Verlauf leeren“), Suchzeile `.k6-such` (Beschriftung 11,5, Feld 372 breit, Radius 10, Lupe links, Trefferzeile Mono 11,5 mit Mint-Zahl), links die Zeitleiste 410 breit (`.k6-liste`: Tag-Titel 13,5 mit Wochentag und Datum, Tagesmarke 16 mit Mint-Ring, Linie 2 px, Zeit 58 | Knoten 28 | Karte), rechts das Detail (`.k4-d`: Blätterstapel mit Zähler, Zeitstempel, Dateiname 18, Ersparnis-Pille 14; Kasten Format → Format mit Sockel in der Artfarbe; Vorher/Nachher/Gespart mit Balken; Zweck, Qualität mit Messbalken 90 × 6, Dauer, Metadaten, Speicherort; Abschnitt DATEIEN mit einer `.vd`-Zeile; bei Fehlern Fehlertitel und Lösungstext aus `Error_*`) und die Knopfleiste `.k4-akt` („Nochmal mit denselben Einstellungen“ als `KvPrimaryButtonStyle`, „Ordner öffnen“, „Im Ordner zeigen“). Ablauf: Überblenden 240 ms über die Composition-Deckung, bei `ReducedMotion` sofort; beim Öffnen bekommt das Suchfeld den Fokus (ohne Einträge der Schließen-Knopf), Tab bleibt in der Ebene (`TabFocusNavigation="Cycle"`), Schrittleiste und `ContentFrame` sind solange deaktiviert (keine Tastenkürzel der Seiten), Esc leert zuerst eine Suche und schließt dann, der Fokus geht an das vorherige Element bzw. an den Titelleisten-Knopf zurück; die Galaxie pausiert, solange die Ebene offen ist. „Nochmal“ wählt Dateien, übergibt den Eintrag als `IWorkflowSession.Previous`, schließt die Ebene und öffnet erst dann Schritt 2 (dort „damals“-Marken und „Alte Werte übernehmen“, unverändert), damit der Schrittübergang laufen darf.

Logik WinUI-frei in `ViewModels/HistoryViewModel.cs` (`HistoryFilter`, `HistoryFilterOption`, Suche mit 150 ms Entprellung über `TimeProvider`, Enter sofort, Auswahl bleibt erhalten, solange der Eintrag sichtbar ist, sonst der erste Treffer; Trefferzeile als höfliche Live-Region); die Seiteneffekte (Dateiauswahl, Übergabe, Schritt 2, Explorer, Rückfrage beim Leeren) liegen hinter `IHistoryActions` (`Services/HistoryActions.cs`). Tests: `tests/Kvertis.App.Tests/HistoryViewModelTests.cs`. Neue Ressourcen-Schlüssel (DE/EN): `History_Overlay`, `History_Close_Text`, `History_Filters`, `History_Filter_{All,Image,Audio,Video,Document,Model,Failed}`, `History_Search_Label`, `History_Search_Box.PlaceholderText`, `History_Match_{Of,One,Many}`, `History_NoMatch_{Title,Text}`, `History_ResetFilters_Button`, `History_Detail`, `History_Detail_{Before,After,Saved,Purpose,Quality,Duration,Metadata,Location,Files}`, `History_Stamp_Text`, `History_Value_None`, `History_Metadata_{Strip,Keep}`, `History_ShowInFolder_{Button,Text}`; geänderte Texte: `History_Again_Text` („Nochmal mit denselben Einstellungen“), `History_Clear_Button` („Verlauf leeren“). Bewusste Abweichungen, jeweils mit Grund:

- **Karten der Zeitleiste mit Zustandssymbol statt Blätterstapel** (wie Teil E): ein Eintrag ist eine Datei, der Zähler wäre immer 1. Im Detailkopf steht der Stapel (in XAML aus drei Blättern), weil er dort die Stelle des Entwurfs einnimmt.
- **Kein „Anpassen“-Knopf.** „Nochmal mit denselben Einstellungen“ führt bereits nach Schritt 2 mit den alten Werten und der „damals“-Karte, das ist der Weg „Anpassen aus dem Verlauf“. Dritter Knopf ist „Im Ordner zeigen“ (Auftrag).
- **Kein Fußbalken `.v-fuss`** und keine Spalte „Original da/fehlt“: der Mischentwurf hat in der Überlagerung keinen Fuß; eine Prüfung der Originale braucht neue Dateisystemlogik.
- **Trefferzeile immer sichtbar** („31 Umwandlungen“, gefiltert „1 von 31 Umwandlungen“), im Entwurf nur beim Suchen; so hat die Live-Region auch beim Filtern etwas anzusagen. Keine Hervorhebung des Suchworts im Namen (`<mark>`).
- **„Verlauf leeren“ fragt per Dialog** (vorhandener `ConfirmAsync`) statt der eingeklappten Frage-Zeile `.v-frage`.
- **Verschiebung der gewählten Karte über den Rand statt `translateX`:** das Listenelement beschneidet Transformationen; Karten reservieren rechts 12 px, die gewählte rückt 5 px nach rechts und zeigt dort die Spitze. Ohne Federanimation.
- **Überblenden ohne Anheben/Skalieren** (`translateY(24px) scale(.985)` des Entwurfs), nur Deckung 240 ms (Auftrag).
- **Schmale Fenster (< 1060 px):** Filter rücken in eine zweite Kopfzeile, der Hinweis „Nur auf diesem PC“ entfällt, die Zeitleiste wird 340 breit (Code-behind, wie die anderen Seiten).
- **Tag-Titel mit fester Fläche statt Verlauf nach unten:** `KvBackground` für 36 px, darunter offen; ein Farbverlauf nach `Transparent` zeichnete einen grauen Streifen.
- **Nicht geprüft im Bild:** Hell und Hoher Kontrast. Alle Flächen nutzen `{ThemeResource Kv*}` oder `Ui.KindVariant`; Schatten und Glühen entfallen im Hohen Kontrast (`KvGlowOpacity` 0, `ThemeShadow` nur ohne Hohen Kontrast).

### Teil F · Einstellungen, Pro, Über, Lizenzen (2026-09-25)

Umgesetzt nach 3.7 und 3.8 in `Views/SettingsPage.xaml`, `ProPage.xaml`, `AboutPage.xaml`, `LicensesPage.xaml`: Seitenpolster 22,14,22,24, Titel `KvTitleTextStyle` (17 / SemiBold, Überschrift Ebene 1), Abschnitte im Label-Stil (Mono 10,5, Sperrung 100, Großbuchstaben aus der Ressource, Rand 0,18,0,6), `SettingsCard` mit den Ressourcen aus Teil A und Symbolen 16 px in `KvMuted`, Schalter `KvSwitchStyle`, Wert der parallelen Jobs Mono 12 / SemiBold `KvInk`, Namensvorschau `KvSmallTextStyle`, FFmpeg-Pfad `KvMonoTextStyle`; Pro-Zeile mit `.pro`-Schild. Pro und Über: Karte `KvCardStyle` mit innerer Oberkante `KvEdge`, Zeilen 13 mit Symbol 16 in Mint, Pro-Status als Schild plus Text 13 / SemiBold, Kaufen `KvPrimaryButtonStyle`, Wiederherstellen und „Lizenzen anzeigen“ als `.btn` (Standard mit Ressourcen), Versionszeile Mono 11. Lizenzen: Liste Standard mit Ressourcen, Lizenztext Mono 12 in `KvInk` auf `KvPanel` in einer Karte mit Radius 13, Polster 16. Neue Ressourcen-Schlüssel (DE/EN): `Pro_Badge.Text`, `Settings_Pro_Badge.Text`, `Settings_{About,Advanced,Appearance,Conversion}_Header.AutomationProperties.Name`; die vier `…_Header.Text` stehen jetzt in Großbuchstaben. Bewusste Abweichungen, jeweils mit Grund:

- **Bausteine-Bild statt Seitenvergleich.** Der Entwurf hat keine dieser Seiten; verglichen wurde gegen die rechte Spalte von Schritt 2 (dunkel, 1280 px). Labels, Karten (Radius, Rand, Polster), `.btn` und Schalterfarben decken sich; Unterschiede nur in Schrift (Segoe statt Geist) und in den bewusst belassenen Standard-Elementen (`ToggleSwitch` 40 × 20 statt 38 × 22, `ComboBox`, `Slider`, `ListView`).
- **Abschnittslabels mit eigenem Namen für den Bildschirmleser.** Der sichtbare Text steht in Großbuchstaben (`.label`), `AutomationProperties.Name` trägt die normale Schreibweise, damit Sprachausgaben das Wort nicht buchstabieren.
- **Pro-Schild immer sichtbar**, auch in der Gratis-Version (wie in der Titelleiste); es benennt das Produkt, der Zustand steht im Text daneben. Das Häkchen in Mint erscheint nur bei aktivem Pro. Der Schildtext ist für die Automation ausgeblendet (`AccessibilityView="Raw"`), weil Karte bzw. Statuszeile den Namen schon tragen.
- **Primärknopf im Prüfbild nicht sichtbar.** Der Debug-Build läuft mit aktivem Pro, dort ist „Kvertis Pro kaufen“ ausgeblendet; der Knopf trägt `KvPrimaryButtonStyle`, die Zeile hat 4 px Polster unten für den Sockel.
- **Karten ohne `ThemeShadow`.** Nach 1.5 bekommen nur Verlauf-Karten und das Speicherort-Menü einen Schatten; die Karten hier haben Verlauf und Oberkante, aber keinen Schatten.
- **Lizenzliste bleibt 240 px breit und ohne Kartenrahmen**, wie in 3.8 („bleibt“); der Einleitungstext ist auf 720 px begrenzt (`.lead`-Regel), die Textkarte nimmt die volle Breite.
- **Nicht geprüft im Bild:** Hell und Hoher Kontrast. Die Seiten setzen nur `{ThemeResource Kv*}`-Pinsel und Stile aus Teil A.

### Teil C · Schritt 2 „Ziel“ (2026-09-25)

Umgesetzt: 3.4 in `Views/TargetPage.xaml(.cs)`; neue Vorlagen in `Themes/KvertisControls.xaml`: `KvGradeRingSliderStyle` (ersetzt `GradeRingSliderStyle` der Seite), `KvSizeSliderStyle`, `KvExpanderStyle` mit `KvExpanderHeaderStyle`, `KvSegmentBarStyle`/`KvSegmentStyle`/`KvSegmentCompactStyle`, `KvOptionStyle`/`KvOptionActionStyle` (für Teil D), dazu `KvOverlineTextStyle` und `KvGlowOpacity` (Hoher Kontrast 0). Helfer `Helpers/TargetUi.cs` (x:Bind-Funktionen) und `Helpers/RingConverters.cs` (Bogen und Daumen des Rings in der Vorlage). Vergleich gegen den gerenderten Entwurf (Schritt 2 über „Beispieldateien einlegen“ und „Weiter“). Bewusste Abweichungen:

- **Spalten nach dem gerenderten Entwurf, nicht nach der Tabelle in 3.4:** Artleiste 124 (links 10), linke Spalte 250 ab x = 146, rechte Spalte 300 (rechts 16). Der Entwurf rendert `.v-l`/`.v-r` in diesen Breiten; 236/286 aus der Tabelle stammen aus einer älteren Variante. 900–1059 px: 85 %; unter 900 px stapeln die Spalten und der Körper rollt (Code-behind).
- **Artwahl als kleine Universen statt `KvOptionStyle`-Zeilen** (Auftrag): flaches Symbol im Kern, Bahn davor und dahinter; grau, bis die Art gewählt ist. Beim Wählen öffnet sich die farbige Bahn einmal (0,9 s von 55 %), danach bleibt nur ein kaum sichtbarer, flacher Hof (8 %) statt des radialen „Atmens“; kein Dauerlauf. Symbole aus Segoe Fluent Icons statt der gezeichneten Linien-Symbole.
- **Mitte bleibt die Liste „Format → Ziel“** (Zeichenschicht folgt), jetzt als Karten (Radius 12, `panel` 85 %, Chips je Art) unter dem Titel „2 Bilder“ mit „70 KB → ≈ 70 KB“.
- **Zielformat bleibt `ComboBox`** unter „WIRD ZU“ (3.4, Barrierefreiheit), nicht die Liste `.zr` des Entwurfs. Dadurch beginnt die Einstellungskarte rund 150 px höher als im Entwurf.
- **Ring 92 mit Bogen 8 und Daumen 22 px** (Blatt: 32). Ein 32-px-Kreis ragte 12 px aus dem 92-px-Ring und verdeckte die Zahl bei 3 und 9 Uhr; 22 px sitzen auf dem Bogen. Glühen als zweiter Kreis 34 px mit 25 %. Der Entwurf zeigt an dieser Stelle einen 64-px-Ring ohne Daumen; es gilt das Blatt (92).
- **Notenwort klein** („gut“ statt „Sehr gut“): der Text kommt unverändert aus `Grade_Band_*`, der auch im Satz für den Bildschirmleser steht.
- **Größenleiste:** Griff in `KvBackground` statt `#000` (Hell sonst schwarzer Knopf), Glühen als flacher Kreis 44 px statt Blur. Die 24 Segmente sind ein Verlauf mit harten Stufen auf einem gerundeten `Border` (Code-behind), weil ein `Border` seine Kinder nicht auf den Radius beschneidet. Marken und „damals“ setzt der Code-behind nach Breite; Marken außerhalb der Skala entfallen wie bisher (bei kleinen Testdateien also keine).
- **Offener Ausklapper:** Der Rahmen in `mint-rahmen` ist ein eigener Rand über dem normalen; ein Setter des Zustands auf den per `TemplateBinding` gesetzten Rand griff nicht. Chevron Fluent `E70D` 10 px statt „▾“, dreht ohne Animation.
- **„Weiteres“ bleibt als eigener Ausklapper** (Metadaten, Dateiname); im Entwurf sind Metadaten die Zone „Privatsphäre“. „Was sich ändert“ ist wie im Entwurf zugeklappt, mit der Summe („alles gut“, „1 Hinweis“) im Kopf; Hinweise in Bernstein wie bisher (`EffectViewModel`), Kreise 18 px.
- **Neue Texte** (DE/EN): Überschriften je Art mit Einzahl („1 Bild“, „2 Bilder“), Quellformate und Größe, Größenfluss je Datei, Ersparnis, Werte in den Ausklapper-Köpfen, Kleinüberschriften in Großbuchstaben. Entfallen: `Target_Title`, `Target_Kinds_Header`, `Target_Mode_Header`, `Target_Paths_Header` und die `Header` von Metadaten-Schalter, Zielgrößen-Schalter und Zahlenfeld (Beschriftung steht links als Text).
- **Nicht geprüft im Bild:** Hell und Hoher Kontrast. Die Vorlagen nutzen nur `{ThemeResource Kv*}`; im Hohen Kontrast entfallen Glühen (`KvGlowOpacity` 0) und die Farbsegmente (Spur `KvDeep`).

**Nachtrag Teil C · Mitte als Zeichenschicht und Zielliste (2026-09-25):** Die beiden verbliebenen Lücken zum Mischentwurf sind geschlossen. Die Mitte ist jetzt eine Win2D-Fläche nach ADR-018/022: Szene-Modell `Scenes/TargetPathsScene` (mit `TargetPathsLayout`, `TargetPathsCommand`; WinUI-frei, Tests in `Kvertis.App.Tests/Scenes/TargetPathsSceneTests`), Zeichner `Rendering/TargetPathsRenderer`, Hülle `Rendering/TargetPathsCanvas` (Muster `GalaxyCanvas`, ohne Zeigereingabe). Die Szene gehört `TargetPageViewModel.PathsScene`; der Code-behind der Seite ist der Host: er baut die Fläche nur ohne „Animationen reduzieren“ und ohne Hohen Kontrast, sonst bleibt die bisherige Liste „Format → Ziel“ (`PathsStaticList`); er pausiert bei verborgenem Fenster, auf anderen Schritten und während eines Übergangs (`ITransitionService.IsTransitioning`), meldet nach jedem Layout die rechte Kante der Dateikarten und die linke Kante der Zielzeilen (nur bei Änderung) und liefert als Loch-Anker die Loch-Mitte der Fläche aus dem reinen Layout (`TargetPathsLayout.Hole`, Radius 12 = `PageHoleRadius`), sodass das Loch der Überlagerung am Ende des Wurmlochs 1→2 exakt dort landet (geprüft in der Bildfolge). Die Zielliste „WIRD ZU“ ist eine `ListView` mit Einfachauswahl im `.zr`-Kartenstil (Raster 58 | * | auto, Radius 12, `panel` 85 %, gewählt Mint-Rand auf Mint-Fläche; Formatname 15 Mono Bold, Kurzbeschreibung aus `Format_<id>_Hint`, Pille „EMPFOHLEN“, Radiopunkt und Größe rechts); Zeilen sind `TargetFormatViewModel`, die Wahl spiegelt `KindGroupViewModel.SharedFormat` in beide Richtungen, Pfeiltasten wechseln die Wahl (`SingleSelectionFollowsFocus`), jede Zeile meldet sich als „Optionsfeld“ (`AutomationProperties.ItemType`) mit Name, Beschreibung, Größe und „empfohlen“. Die Größen je Format schätzt `KindGroupViewModel` in `Task.Run` mit Abbruch (Schätzer mit `GradeMapper.Apply` je Datei und Format, ausgelöst über das neue `TuningPanelViewModel.Settled` nach dem entprellten Teil); die gewählte Zeile zeigt die Summe des Panels, damit Liste und Ring nicht auseinanderlaufen. Die `ComboBox` und ihr Schlüssel `Target_Format_ComboBox` entfallen; neu `Target_Formats_List`, `Target_Format_Recommended`, `Target_Format_ItemType`, `Target_Format_AutomationName(_Recommended)` (DE/EN). Der Punkt „Zielformat als Reiter“ in `abgleich-mischentwurf.md` 7 ist damit durch die Liste erledigt. Bewusste Abweichungen vom Entwurf:

- **Kein Dauerlauf auf den Wegen:** Die wandernden Punkte des Entwurfs laufen nur 2,5 s nach einer Änderung (neues Ziel, neue Art), danach stehen die Wege; nur Planeten und Staub drehen langsam (0,12 rad/s). Beim Zielwechsel biegt der Weg weich um (Glättung 9/s) statt zu springen.
- **Bahn und Loch nur in der Mitte:** Die Fläche deckt allein die mittlere Spalte; die Wege beginnen 3 px rechts der Dateikarte und enden 3 px links der Zielzeile, wie `zoomZeichnen()` im Entwurf, aber ohne Fläche unter den Spalten. Loch bei 42 % der Höhe, Bahn höchstens 200 breit (Entwurf `geo()`).
- **Eigene Wahl in „Jede einzeln“:** Die zweite Hälfte des Wegs wird in Bernstein gestrichelt, wenn die Datei ein eigenes Ziel hat (Entwurf `gelb`); unbenutzte Zielzeilen bekommen die blasse Linie aus dem Loch.
- **Kurzbeschreibung darf umbrechen** (zwei Zeilen), weil die deutschen Hinweise („Scharfe Grafik, transparent“) breiter sind als die Kurzworte des Entwurfs; die Größe steht Mono 11 rechts neben dem Radiopunkt.
- **Beim Wählen per Tastatur rollt die rechte Spalte** die gewählte Zeile ins Bild (Standard der `ListView`); die Kleinüberschrift kann dabei nach oben verschwinden.
- Gestapelte Spalten unter 900 px: die Mitte behält 260 px Höhe für die Fläche.

### Teil D · Schritt 3 „Umwandeln“ inklusive D2 (2026-09-25)

Umgesetzt: 3.5 in `Views/ConvertPage.xaml(.cs)`, neu `Views/FormatArrow.xaml(.cs)` (zwei Chips 44 × 22 mit Pfeil 2 px, Füllung nach Fortschritt, Ziel-Chip gestrichelt Mint bzw. gefüllt) und `Helpers/ConvertUi.cs` (x:Bind-Funktionen für Rahmen und Textfarben der Zeile); in `Themes/KvertisControls.xaml` `KvMenuFlyoutPresenterStyle` auf 300 px festgelegt, dazu `KvMenuTitleTextStyle`, `KvMenuSubTextStyle`, `KvMenuSeparatorStyle` und `KvSmallOutlineMintButtonStyle` (`.w5-akt.haupt`). **D2:** `SwirlHost` liegt über die ganze Breite 300 px hoch ohne eigene Fläche und Rundung (Zeichnung unverändert, sie legt Stapel, Loch und weißes Loch schon nach der Breite an); „EINGANG“ mit Zeile 13 / Bold und roten Kleinzeilen links unten (22/16), „HIERHIN SPEICHERN“ mit Ordnername 16, Pfad Mono 10,5, „Speicherort ändern“ und Tasche rechts unten unter dem weißen Loch (Inhalt min(300, 30 %) breit, 14 px vom rechten und 16 px vom unteren Rand wie im gerenderten Entwurf); Ordner-Ablage färbt den Block Mint mit 2-px-Ring. Liste `.w5-liste` mit Kopf (Label, Größen „vorher → ≈ nachher“ 15 Mono, Stand rechts mit Mint-Teil), Spaltenköpfen und Zeilen mit festen Spalten 10 | 170 | 124 | * | 214 | 58 (im Vergleichsbild pixelgleich mit dem Entwurf, auch mit Segoe; die Mono-Spalten tragen die Breite), Fortschrittslinie 2 px mit Glühen, Rahmen Mint beim Laufen und `KvErrorFrame` bei Fehlern; Aktionen „Ändern“ (Menü), „Öffnen“ (Mint-Umriss), „Im Ordner zeigen“ mit Symbolen 13; Menüs `.w5-menue` mit Kopf, `KvOptionStyle`-Zeilen, Trennlinien, „Ordner wählen …“ als `KvOptionActionStyle` und Fußzeile; Bericht Titel 18 / SemiBold, Zeilen Mono 11,5, Knöpfe klein. Fußleiste `.leiste` 12/18. Neue Ressourcen-Schlüssel (DE/EN): `Convert_Inbox_Header.AutomationProperties.Name`, `Convert_Inbox_Summary_One`, `Convert_Inbox_Running`, `Convert_Inbox_Running_One`, `Convert_Inbox_InSwirl`, `Convert_Inbox_AllDone`, `Convert_Inbox_NotConverted`, `Convert_Location_Header.AutomationProperties.Name`, `Convert_Menu_{Shared_Header,Shared_Sub,Same_Title,Same_Detail,Sub_Title,Sub_Detail,Custom_Title,Pick_Title,Pick_Detail,Footer,RowShared_Title,RowShared_Detail}.Text`, `Convert_Bag_None`, `Convert_Bag_Hint.Text`, `Convert_List_Header.AutomationProperties.Name`, `Convert_Column_{File,Format,Target,Action,Status}.Text`, `Convert_List_Estimate`, `Convert_ListState_{Ready,Progress,Paused,Change,Finished,Done}`, `Convert_Row_Status_{Ready,Waiting,Paused,Done,Failed,Cancelled}`, `Convert_Row_{Change,Open,ShowInFolder}_Text.Text`; geändert: Überschriften in Großbuchstaben, `Convert_Row_Location_Header`, `Convert_Location_{Same,Sub}_Detail` (Texte des Entwurfs); entfallen: `Convert_Title`, `Convert_Swirl_Header`, `Convert_Result_Header`, `Convert_Skipped_Header`, `Convert_List_Summary`, `Convert_List_State_*` und die `.Content`-Schlüssel der Menüzeilen und Zeilenknöpfe (Inhalt ist jetzt Titel + Pfad bzw. Symbol + Text; die Namen für den Bildschirmleser bleiben). Bewusste Abweichungen, jeweils mit Grund:

- **Eingang ohne Dateizeilen.** Das Blatt wollte die Zeilen behalten; der Entwurf zeigt unter dem gezeichneten Stapel nur Label, Zahl und Kleinzeile, und in 300 px Höhe liefen die Zeilen in den Stapel. Jede Datei steht mit Zustand in der Liste darunter.
- **„n von N“ nur gezeichnet.** Mit Zeichenfläche steht der Zähler nur unter dem weißen Loch (Renderer); ohne Zeichenfläche (ruhige Ansicht, Hoher Kontrast) steht „fertig n von N“ als Text über „HIERHIN SPEICHERN“. Laufende Dateien werden über die Ansage-Region und die Gesamtzeile angesagt.
- **Laufende Karten nur ohne Zeichenfläche.** Wie im Entwurf zeigen Wirbel und Liste die laufenden Dateien; der Hinweis „Bereit · Umwandeln startet n Dateien“ und die Karten erscheinen nur in der ruhigen Ansicht und im Hohen Kontrast (oben mittig in der Bühne).
- **Bühne bleibt bei Fensterhöhe < 720 px 300 hoch** (Blatt 1.1: 220). Das weiße Loch liegt in der Zeichnung fest bei y = 100; bei 220 px läge der Speicherort darüber. Die Liste rollt.
- **Unter 900 px** stehen Eingang und Speicherort unter der Bühne, die Liste behält die festen Spalten (Mindestbreite 822) und rollt seitlich, statt die Spalten umzubauen.
- **Chips zeigen die Dateiendung** (`PrimaryExtension`, „MD“, „OGG“), nicht den Anzeigenamen der Formatliste („Markdown“, „OGG (Vorbis)“), weil 44 px nur vier Zeichen fassen; der Anzeigename bleibt im Namen für den Bildschirmleser.
- **Format-Chips und Zielpfad sind keine Knöpfe.** Im Entwurf öffnen `.w5-fa` (Format ändern) und `.w5-ziel` (dasselbe Menü wie „Ändern“) eigene Aktionen; in der App gibt es je Zeile genau einen Tab-Stopp für das Ziel („Ändern“), der Zielpfad trägt den vollen Pfad als Tooltip. Das Format ändert sich weiter in Schritt 2.
- **Fehlerzeile mit Lösungsvorschlag.** Neben dem Grund in der Aktionsspalte (11,5 `KvError`, wie `.w5-grund`) steht unter der Zeile der Vorschlag aus `Error_*` (Leitlinie: Fehler mit Lösungsvorschlag). Rahmen `KvErrorFrame`, Status „✕ Fehler“. Nicht im Bild geprüft (Testdateien sind fehlerfrei).
- **Kurzer Status** in der Spalte 58 („bereit“, „42 %“, „✓ fertig“, „✕ Fehler“, „✕ Abbruch“) aus `Convert_Row_Status_*`; der ausführliche Zustand bleibt im Namen der Zeile.
- **Zeilenmenü:** „Wie alle anderen“ mit „folgt „Hierhin speichern““ statt der Regel des Entwurfs; zuletzt benutzte Ordner (`ZULETZT`) und „↺ damals“ gibt es nicht, weil die App keine Liste früherer Ordner führt. Eigene Ziele aufheben im Fuß des Hauptmenüs fehlt aus demselben Grund (je Zeile über „Wie alle anderen“).
- **Tasche immer sichtbar** wie im Entwurf („Eine Datei woanders hin?“); ohne eigene Ziele zeigt ihr Menü den Hinweis „In der Liste bei einer Datei auf „Ändern“ klicken.“ statt eines Toasts (Toast weiter offen, 7). Hüpfen über `CardAnimations.CountHop`, Rand bei Hover Mint.
- **Hover der kleinen Knöpfe** (`.w5-akt`): Rand `KvLineStrong` aus den globalen Knopf-Ressourcen statt Mint; eine lokale Überschreibung hätte eigene Theme-Wörterbücher gebraucht.
- **„Zurück“ nach dem Lauf fehlt** (Entwurf zeigt ihn); der Befehl gilt nur vor dem Start, danach führt „Neue Runde“ weiter.
- **Pause, Abbrechen und Gesamtbalken** (6 px, Mono 11) stehen während des Laufs in der Fußleiste; der Entwurf hat dort nur „Zurück“. Sie sind die einzige Stelle für Pause und Abbruch.
- **„Speicherort ändern“ bleibt `DropDownButton`** mit dem Chevron von WinUI statt „▾“; Höhe 26 statt 28,6.
- **Nicht geprüft im Bild:** Hell, Hoher Kontrast und die ruhige Ansicht. Alle Flächen nutzen `{ThemeResource Kv*}`; die im Code gesetzten Pinsel (`FormatArrow`, Ablage-Zustand, Taschenrand) kommen über `Ui.Brush` aus dem Theme und werden bei `ActualThemeChanged` neu gesetzt (`FormatArrow`).
