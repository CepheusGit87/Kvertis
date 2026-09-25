# Arbeitsblatt Schritt 2 „Ziel“ (Zoom-Wege) für den UI-Entwickler

Stand 2026-09-25. Verbindliche Grundlage: `docs/03-architektur.md`, Abschnitt „Schritt 2 „Ziel“: Schnittstellen und Datenfluss“ sowie ADR-019 und ADR-020. Dieses Blatt ist die Kurzfassung zum Abarbeiten; bei Widerspruch gilt `03-architektur.md`.

## Was gebaut wird

Die Seite `Views/TargetPage.xaml` ersetzt den Platzhalter aus Etappe 0. Drei Spalten:

| Spalte | Etappe C (jetzt, reines XAML) | Später (Win2D, ADR-018) |
|---|---|---|
| Links | Artenliste als `ListView` (Farbpunkt aus `Kv<Art>Brush`, Name, Anzahl); darunter Dateikarten der gewählten Art mit Miniatur, Name, Größe, Warnhinweis; Umschalter „Alle gleich / Jede einzeln“ ab zwei Dateien | Universum-Symbole mit Ring und Bahn |
| Mitte | schlichte Liste „Quelle-Format → Ziel-Format · ≈ Größe“, eine Zeile je Datei; in „Jede einzeln“ ein `ComboBox` je Zeile | Bahn, Loch, Wege |
| Rechts | Zielformate (Empfehlung markiert), Ring mit Note und Wort, Größenleiste mit Marken, Zonen als `Expander`, „Zielgröße genau“, „Weiteres“, „Was sich ändert“ | unverändert |

Keine Animationen in dieser Etappe außer den Fluent-Standards. Alle Farben nur aus `Themes/KvertisColors.xaml`.

## Reihenfolge der Teilaufgaben

### A · Engine-Tuning (engine-entwickler, mit tester) – Voraussetzung für alles Weitere

Namensraum `Kvertis.Engine.Tuning`: `QualityGrade`, `GradeBand`, `TuningAspect`, `AspectLevel`, `AspectDetail`, `GradeMapper`, `EffectCode`, `EffectSeverity`, `ConversionEffect`, `EffectAnalyzer`, `GradeSizeTable`, `GradeSizeTableBuilder`, `SizeMarks`. Dazu `Estimation.SizeModel` und die Anpassung von `Estimator`. Signaturen und Abbildungstabelle stehen in `03-architektur.md`. Tests: Rundreise `GradeOf(Apply(g)) == g`, Tabelle monoton, kein Wurf für alle Kombinationen der `FormatRegistry`. Ohne diese Etappe bindet die Ziel-Seite an nichts.

### B · Sitzung und ViewModels (ui-entwickler, mit tester)

- `Services/WorkflowSession.cs`: `IWorkflowSession` mit `Staged`, `Plan`, `Location`, `Previous`, `Changed`, `Reset()`. Registrierung als Singleton in `ServiceRegistration`.
- `ViewModels/Target/`: `TargetPageViewModel`, `KindGroupViewModel`, `TargetFileViewModel`, `TuningPanelViewModel`, `SizeBarViewModel`, `ZoneViewModel`, `EffectViewModel`, `PreviousSettingsViewModel`, `PathRowViewModel`. Aufbau wie in `03-architektur.md`.
- Regeln:
  - Formate je Gruppe = Schnittmenge von `IConverterResolver.CanConvert` über alle Dateien der Art; Empfehlung = `FormatRegistry.SuggestOutputs(...).Default` der ersten Datei, sofern in der Schnittmenge, sonst das erste Element.
  - Note, Zonen, Zielgröße genau, Metadaten und Dateiname gelten je Art, nie je Datei. „Jede einzeln“ ändert nur das Format je Datei.
  - Tabellen (`GradeSizeTable`) je Datei in `Task.Run` bauen, mit `CancellationTokenSource`, die bei jeder neuen Änderung die alte abbricht. Beim Ziehen nur `BytesAt` / `GradeForBytes` synchron; `Effects` und Leistenfarben alle 50 ms über einen `DispatcherQueueTimer` bzw. im Test über `TimeProvider`.
  - Leiste: logarithmische Skala von `MinBytes` (kleinste Tabelle) bis zur größten Quelldatei; Position → Bytes → `GradeForBytes` der größten Datei. Marken aus `SizeMarks.For(kind)`, Beschriftung `Preset_Messenger`, `Preset_Email` aus den Ressourcen; Marken außerhalb der Skala nicht zeichnen.
  - `Zielgröße genau` setzt `TargetSizeBytes`; `bytes < table.MinBytes` zeigt den Hinweis `Effect_TargetSizeMayBeUnreachable_Text`. Die Leiste zeigt dann „≈“.
  - Freemium: `FreemiumPolicy.IsKindLocked(MediaKind.Video)` → Gruppe zeigt Pro-Karte (Texte `Pro_Card_Video_*`), Dateien in `TargetPlan.Skipped` mit `FreemiumPolicy.ReasonVideo`. Über `BatchLimit` → Karte `Pro_Card_Batch_*`, die ersten fünf in den Plan.
  - `ContinueCommand` baut je Datei `PlannedConversion(Input, settings, NamePattern)`, wobei `settings = GradeMapper.Apply(grade, baseline, input)` und danach die Zonen-Abweichungen per `WithAspect`, `Metadata`, `TargetSizeBytes` gesetzt sind; schreibt `session.Plan`; `IStepNavigationService.GoTo(WorkflowStep.Convert)`.
- Tests ohne WinUI: Modus-Wechsel, Schnittmenge, Sperre, Plan-Inhalt, Entprellung, „damals“-Übernahme.

### C · XAML der Ziel-Seite (ui-entwickler)

- Ring: `Grid` mit `Ellipse` (Umfangsfarbe je `GradeBand`: Mint ≥ 65, Bernstein ≥ 45, Koralle darunter) und Textblöcken; `AutomationProperties.Name="Qualität"`, Wert über `AutomationProperties.HelpText` oder ein unsichtbares `Slider`-Steuerelement für die Tastatur (Pfeiltasten ±5). Empfehlung: den Ring selbst als `Slider` mit eigenem `ControlTemplate` bauen, damit Fokus, Tastatur und Erzähler von allein stimmen.
- Größenleiste: `Slider` (0–1000, logarithmische Umrechnung im ViewModel), darunter `Canvas` mit den Marken als `TextBlock` an berechneter Position; Farbzonen als `Rectangle`-Reihe hinter der Spur (24 Segmente aus `GradeSizePoint.Worst`).
- Zonen: `Expander` je `ZoneViewModel`; im Kopf Name, `ProgressBar` (0–100, `IsIndeterminate=False`) als Balken mit Farbe nach Wert, Detailtext; im Inhalt ein `Slider` (deaktiviert bei `Adjustable = false`, mit Erklärung „hängt am Format“).
- „Zielgröße genau“: `Expander` mit `NumberBox` und Einheit; „Weiteres“: `ToggleSwitch` Metadaten, `TextBox` Namensmuster mit Vorschau; „Was sich ändert“: `ItemsRepeater` mit Symbol (Häkchen, Ausrufezeichen, Kreuz) und Text.
- „damals“: `InfoBar`-ähnliche Karte oben rechts („Aus dem Verlauf · {Datum}“, „Alte Werte übernehmen“) und kleine gestrichelte Marken an Ring, Leiste und Zonen (`TextBlock` „damals“ an berechneter Position).
- Untere Leiste: Vertrauenszeile, Summe („7 Dateien · ≈ 12 MB“), „Zurück“, „Weiter: Umwandeln“ (Mint, einziger Akzentknopf).
- Barrierefreiheit: jede Bedienung mit `AutomationProperties.Name`; Ring und Balken zusätzlich mit Wert; Hoher Kontrast ohne Farbzonen (nur Text). Fenster ab 640 px: rechte Spalte rutscht unter die Liste (`AdaptiveTrigger`).
- Ressourcen: alle Texte in `Strings/de-DE` und `en-US`; Schlüsselmuster `Target_*`, `Grade_Band_*`, `Effect_<Code>_Text`, `Zone_<Aspect>_Name`.

### D · Verdrahtung (ui-entwickler, reviewer)

- Schritt 1 (`MainPage`): Dateien landen zusätzlich in `IWorkflowSession.Staged` (Miniatur, `InputInfo`, Warnungen). Knopf „Weiter: Ziel“ statt Start; aktiv nur mit mindestens einer erkannten Datei.
- Schritt 2: liest `Staged`, schreibt `Plan`.
- Schritt 3 (`ConvertPage`, eigene Etappe): baut aus `Plan` und `Location` die `ConversionJob`s und ruft `EnqueueRange`.
- Verlauf: `HistoryViewModel.AgainAsync` setzt `session.Previous`, öffnet den Dateidialog, fügt die Dateien zu `Staged`, springt nach Schritt 2.
- Abbau: `MorePanel`, `FormatPickerFlyout`, Preset-Chip und Start in `MainViewModel` erst entfernen, wenn Schritt 3 die Jobs startet; bis dahin bleibt der alte Weg funktionsfähig.

## Nicht in diesem Blatt

Zeichenschicht (Bahn, Loch, Universen, Übergänge), „Ändern“ je Datei in Schritt 3, Vorschau-Dialog aus Schritt 2, Video-Zielgröße exakt (Phase 2).

## Offene Fragen an den Projektinhaber

Keine, die die Umsetzung blockieren. Die Zahlen der Abbildungstabelle Note → Parameter werden nach der ersten Sichtprüfung unter Windows angepasst.
