# Arbeitsblatt Schritt 3 „Umwandeln“ (Pixelwirbel, ohne Zeichenschicht) für den UI-Entwickler

Stand 2026-09-25. Verbindliche Grundlage: `docs/03-architektur.md`, Abschnitt „Schritt 3 „Umwandeln“: Schnittstellen und Datenfluss“ sowie ADR-020 und ADR-021. Dieses Blatt ist die Kurzfassung zum Abarbeiten; bei Widerspruch gilt `03-architektur.md`. Entwurf: `design/ENTSCHEIDUNGEN.md` (Zeilen „Schritt 3“, „Ziel in Schritt 3“, „Speicherort“, „Übergänge und Abschluss“), `design/oberflaeche-mischentwurf.html` (Abschnitt „Schritt 3“, Funktionen `renderGross`, `zeileW5`, `renderLauf`, `renderLeiste`).

Reines XAML. Pixelwirbel, weißes Loch, Blätter und Abschluss-Sequenz kommen später mit Win2D (ADR-018); in diesem Blatt gibt es dafür nur eine leere Platzhalter-Fläche, in die die Zeichenschicht später eingehängt wird. Keine Animationen außer den Fluent-Standards. Alle Farben nur aus `Themes/KvertisColors.xaml`.

## Was gebaut wird

Die Seite `Views/ConvertPage.xaml` ersetzt den Platzhalter aus Etappe 0. Oben drei Spalten, darunter die Liste, ganz unten die Leiste.

| Bereich | Jetzt (reines XAML) | Später (Win2D, ADR-018) |
|---|---|---|
| Links „Eingang“ | Karte mit Überschrift, Zahl „7 Dateien · 42 MB“ und einer schlichten Liste der **wartenden** Dateien (Name, Farbpunkt der Art, Größe). Nach dem Lauf steht hier, was fehlgeschlagen ist („1 Datei nicht umgewandelt“) mit Namen. Dateien aus `TargetPlan.Skipped` stehen darunter klein mit Grund (`Convert_Skipped_*`, siehe Freemium) | Eingangsstapel als Blätter |
| Mitte „Wirbel“ | `Grid x:Name="SwirlHost"` mit `KvDeepBrush`-Hintergrund; darin ein `ItemsRepeater` mit höchstens **zwei** hervorgehobenen laufenden Jobs: Name, „HEIC → JPG“, `ProgressBar` (bestimmt, nie `IsIndeterminate`), Prozent, Restzeit. Laufen mehr als zwei (`MaxParallel` > 2), zeigt eine Zeile „und 2 weitere laufen“; alle laufen sichtbar in der Liste unten mit. Vor dem Start steht hier „Bereit · Umwandeln startet die 7 Dateien“, nach dem Lauf der Bericht (siehe Abschluss) | Pixelwirbel; der Bericht wandert in den Ring |
| Rechts „Ergebnis“ | Kopf „fertig 3 von 7“ (`AutomationProperties.LiveSetting=Polite`) und darunter die Namen der fertigen Dateien; darunter die **Speicherort-Karte**: Titel („Neben dem Original“, „Unterordner „Kvertis““ oder Ordnername), zweite Zeile mit Erklärung bzw. vollem Pfad, Knopf „Speicherort ändern“ (Flyout mit drei `RadioButton` und „Ordner wählen…“), die Karte ist Ablageziel für einen Ordner aus dem Explorer; daneben der kleine Knopf „Tasche“ („2 Dateien woanders hin“ bzw. „Eine Datei woanders hin? In der Liste auf „Ändern““) | weißes Loch mit Bahnen und Planeten |
| Liste | Kopf „Dateien in diesem Auftrag · 42 MB → ≈ 12 MB · bereit/läuft/fertig“. Eine Zeile je Datei aus `Plan.Items`: Farbpunkt, Name mit „8,4 MB → ≈ 1,1 MB“, Chips „HEIC → JPG“, Zielpfad (Ordner gekürzt, Dateiname fett, Marke „wie alle“ / „eigenes Ziel“), Aktion, Status. Aktion vor dem Start: „Ändern“ (Ziel je Datei). Laufend: Prozent. Fertig: „Öffnen“ und „Im Ordner zeigen“. Fehler: Titel aus `Error_<Code>_Title`, Lösung aus `Error_<Code>_Body`, dazu `AutomationProperties.LiveSetting=Assertive` | unverändert |
| Untere Leiste | Vertrauenszeile (`Convert_Trust_Text`), Gesamtfortschritt als `ProgressBar` mit Text „3 von 7 · noch etwa 40 s“, links „Zurück“ (nur vor dem Start), rechts vor dem Start der einzige Mint-Knopf „Umwandeln · 7 Dateien“; während des Laufs „Pause“/„Weiter“ und „Abbrechen“; nach dem Lauf „Neue Runde“ (Mint) | unverändert |

Fenster unter 900 px: die drei Spalten stapeln sich (Eingang, Wirbel, Ergebnis) über der Liste (`AdaptiveTrigger`). Mindestbreite 640 px bleibt bedienbar.

### Abschluss („Fertig“-Bericht)

Sobald kein Job mehr wartet oder läuft, zeigt die Mitte den Bericht: Häkchen-Symbol (Mint) oder Ausrufezeichen (Bernstein, wenn etwas fehlte), Titel „Alle 7 Dateien umgewandelt“ bzw. „6 von 7 umgewandelt“, Zeile „42 MB → 12 MB · 71 % gespart · 1:20 min“, Liste der Fehler mit Titel je Datei, Knöpfe „Ordner öffnen“ (bei „Neben dem Original“: Ordner der ersten fertigen Datei) und „Neue Runde“. Eingang und Speicherort-Karte bleiben stehen (die Ausblendung ist Teil der späteren Übergangs-Überlagerung). Der Bericht wird mit `LiveSetting=Assertive` einmal angesagt. Bei „Animationen reduzieren“ ändert sich nichts, weil es hier nichts zu reduzieren gibt; das ist die statische Ansicht, die ADR-018 verlangt.

## Datenfluss

Alles Verbindliche steht in `03-architektur.md`; hier die Reihenfolge, wie es passiert.

1. **Betreten der Seite:** `ConvertPageViewModel` liest `session.Plan`; ohne Plan zeigt die Seite nur den Hinweis `Convert_NoPlan_Text` (Schrittleiste lässt das ohnehin nicht zu). Ist `session.Location` noch null, setzt das ViewModel es aus den Einstellungen (`AppSettings.OutputLocation`, `CustomOutputFolderPath` über `IFilePickerService.GetRememberedOutputFolderAsync`). Danach `TargetPathPlanner.PreviewAll(...)` für alle Zeilen.
2. **Zielpfad-Vorschau:** je Zeile `OutputDirectoryResolver.Resolve(input, wirksamer Ort)` + `OutputNamePattern.Render(pattern, input, registry.ExtensionFor(output), now, batchIndex)` + `EnsureUnique` gegen Datenträger **und** die anderen Vorschauen derselben Runde. Der wirksame Ort ist `session.OwnLocations[input.Path]`, sonst `session.Location`. Das ist dieselbe Rechnung, die `JobRunner` beim Start macht; Abweichungen (jemand legt zwischendurch eine gleichnamige Datei an) meldet die Queue mit `JobChangeKind.Details`, die Zeile zieht dann nach. Nie überschreiben (ADR-007): die Nummerierung `_1`, `_2` ist Standard, es gibt keinen Schalter „Überschreiben“.
3. **Zwischenablage-Bilder** liegen unter `AppPaths.ClipboardFolder`; „Neben dem Original“ oder „Unterordner“ wäre dort sinnlos. `TargetPathPlanner` markiert solche Zeilen mit `NeedsFolder = true`, die Zeile zeigt „Ordner wählen“ statt eines Pfads, und `Umwandeln` bleibt gesperrt, bis ein eigener Ordner (für alle oder für diese Datei) gewählt ist.
4. **Speicherort ändern / Ordner ziehen:** setzt `session.Location`, speichert die Wahl in den Einstellungen (wie heute `MainViewModel.ApplyOutputLocationAsync`, mit `RememberOutputFolder`-Token bei eigenem Ordner) und rechnet alle Vorschauen neu. **„Ändern“ je Datei** setzt `session.SetOwnLocation(path, location)` (Flyout mit denselben drei Optionen plus „wie alle“). Vor dem Start ändert ein Wechsel alle Zeilen ohne eigenes Ziel; nach dem Start nur noch **wartende** Jobs (`IConversionCoordinator.RelocateWaiting`: `IJobQueue.Remove` + neuer `ConversionJob` mit neuem Ordner, gleicher `BatchIndex`, `EnqueueRange`); laufende und fertige Jobs bleiben, wo sie sind, ihre Zeilen zeigen den endgültigen Pfad. Die Knöpfe „Ändern“ einer laufenden oder fertigen Zeile sind ausgeblendet.
5. **Start:** `IConversionCoordinator.Start(plan, location, ownLocations)` baut je `PlannedConversion` einen `ConversionJob(item.Input, item.Settings, directory, item.NamePattern, batchIndex)` (Index ab 1 nur bei mehr als einer Datei) und ruft `IJobQueue.EnqueueRange`. `JobAdmissionException` (zweites Netz der Freemium-Regel) → nichts ist eingereiht, das ViewModel zeigt die Pro-Karte mit `ex.Result.Reason`.
6. **Ereignisse:** der Koordinator hängt an `IJobQueue.JobChanged`, filtert auf die Job-Ids **dieser Runde** und meldet über `IUiDispatcher.Post` auf dem UI-Thread `Changed`. Das ViewModel aktualisiert Zeile, Wirbel-Karten, „fertig n von N“, Gesamtfortschritt (`OverallProgress.Compute(coordinator.Jobs)`) und Restzeit (`Overall.Remaining`). Fremde Jobs in der Queue (es gibt nach Teil C keine mehr) würden ignoriert.
7. **Pause/Weiter, Abbrechen:** `PauseAll`/`ResumeAll`/`CancelAll` nur über die Ids der Runde (`IJobQueue.Pause(id)` je Job), nicht über `IJobQueue.PauseAll`, damit der Koordinator auch neben einer anderen Runde sauber bliebe. „Abbrechen“ fragt nach (`Convert_CancelAll_*`), weil laufende Dateien verloren gehen.
8. **Verlauf:** schreibt weiterhin **die Queue** (`JobQueueOptions.History` → `JobHistory.RecordAsync` bei Completed und Failed, nie bei Cancelled). Die App schreibt nichts. Der „damals“-Eintrag braucht `Settings` (vollständige `ConversionSettings`), `InputFormat`, `OutputPath`, `BytesIn/Out` und `When`; das steht alles in `HistoryEntry.FromJob`. Nichts Neues nötig. Da die Note keine Einstellung ist (ADR-019), rechnet Schritt 2 sie aus `Settings` zurück.
9. **Neue Runde:** `coordinator.Reset()` (entfernt die fertigen Jobs dieser Runde per `IJobQueue.Remove`, vergisst die Ids), `session.Reset()` (löscht `Staged`, `Plan`, `OwnLocations`, `Previous`, `Location` auf null), `IStepNavigationService.GoTo(WorkflowStep.Drop)`. Schritt 1 sieht eine leere Sitzung und leert seine Karten.

### Was aus `MainViewModel` wandert

| Heute in `MainViewModel` | Wohin |
|---|---|
| `StartAsync`, `ResolveOutputLocationAsync`, Bau der `ConversionJob`s, `_byJobId`, `EnqueueRange`, `JobAdmissionException` → `ShowPro` | `ConversionCoordinator` (Start, Ids der Runde) und `ConvertPageViewModel` (Pro-Karte) |
| `OnJobChanged`/`HandleJobChanged`, `UpdateOverall` (Fortschritt, Restzeit, `IsPausedAll`, Fertig-Text), `Announce` | `ConversionCoordinator` (Filter, Marshalling, `Overall`, `Report`) und `ConvertPageViewModel` (Texte, Ansagen) |
| `OutputLocationIndex`, `ApplyOutputLocationAsync`, `CustomFolderPath`, `HasCustomFolder` | `ConvertPageViewModel` (Speicherort-Karte) mit `IFilePickerService` |
| `PauseAll`, `CancelAll`, `PauseOrResume`, `Cancel`, `Retry`, `EnqueueAgain` | `ConversionCoordinator` (nur `PauseAll`/`ResumeAll`/`CancelAll` je Runde; Einzel-Pause und Retry sind nicht Teil dieses Blatts) |
| `OpenFolderAsync` | `IShellLauncher` (`OpenFileAsync`, `ShowInFolderAsync`), genutzt von Zeile und Bericht |
| `PresetOptions`, `ApplyToAll`, `OnItemSettingsChanged`, `BuildSettings` in `JobItemViewModel` | entfällt (Schritt 2 hat das) |

`ConversionCoordinator` und `TargetPathPlanner` sind frei von WinUI und werden wie `TargetPlanner` per `<Compile Include>` in `tests/Kvertis.App.Tests` eingebunden.

## Schrittleiste und Navigation

- Schritt 3 ist erst anklickbar, wenn `session.Plan` mindestens einen Eintrag hat (heute schon so in `StepHeader.IsReachable`).
- Während `coordinator.State == Running` oder `Paused` sind die Knöpfe für Schritt 1 und 2 **gesperrt** (`IsEnabled = false`, `AutomationProperties.ItemStatus` „gesperrt, Umwandlung läuft“), ebenso „Zurück“ in der Leiste. Keine Rückfrage: Wer abbrechen will, drückt „Abbrechen“ und geht dann zurück. Nach dem Lauf sind 1 und 2 wieder erreichbar; der Plan bleibt (ADR-020), die Zeilen der letzten Runde bleiben in Schritt 3 stehen, bis „Neue Runde“ oder ein neuer Plan kommt (ein geänderter Plan setzt den Koordinator zurück).
- `StepHeader` bekommt dafür `IConversionCoordinator` per DI und hört `Changed`.
- Beim Schließen des Fensters während eines Laufs gilt das heutige Verhalten (`JobQueue.StopAsync` bricht ab); eine Rückfrage ist nicht Teil dieses Blatts.

## Freemium

- Die Gratis-Grenze ist im Plan schon berücksichtigt (Schritt 2, `TargetPlanner.BuildPlan`): Video ohne Pro und Dateien über dem Batch-Limit stehen in `Plan.Skipped` mit `ReasonVideo`/`ReasonBatchSize`.
- Schritt 3 zeigt eine Pro-Karte **nur**, wenn `Plan.Skipped` nicht leer ist: unterhalb des Eingangs, Text je Grund (`Pro_Card_Video_*`, `Pro_Card_Batch_*` wiederverwenden), Knopf „Mehr erfahren“ (`AppPage.Pro`). Kein Modal, kein Hinweis ohne Grund.
- Die Queue-Admission (`FreemiumPolicy.Check`) bleibt als zweites Netz. Wirft `EnqueueRange`, wurde nichts eingereiht; das ViewModel zeigt dieselbe Pro-Karte mit dem Grund aus `JobAdmissionException.Result.Reason` und bleibt im Zustand „Bereit“.
- Wird der Nutzer während der Runde Pro (`ILicenseService.Changed`), ändert sich am laufenden Auftrag nichts; die Pro-Karte verschwindet.

## Barrierefreiheit und „Animationen reduzieren“

- Jeder Knopf und jede Zeile hat `AutomationProperties.Name`; Zeilenname zusammengesetzt: „foto.heic, HEIC nach JPG, wird gespeichert als …, wartend“ (`Convert_Row_AutomationName`).
- Gesamtfortschritt: `ProgressBar` mit `AutomationProperties.Name` = Text der Leiste; der Text „3 von 7 · noch etwa 40 s“ als `TextBlock` mit `LiveSetting=Polite`; der Erzähler bekommt Änderungen höchstens alle 5 Sekunden oder bei jedem Zählerwechsel (das ViewModel hält den Ansage-Text absichtlich zurück, `Debouncer` mit `TimeProvider`).
- Fertig und Fehler je Datei: ein unsichtbarer `TextBlock` `LiveSetting=Assertive` wie heute `AnnouncementText` (`Convert_Announce_Completed`, `Convert_Announce_Failed`, `Convert_Announce_Cancelled`). Der Bericht am Ende ebenso einmal.
- Keine Dauerbewegung: keine unbestimmten Fortschrittsanzeigen, kein wanderndes Leuchten, kein Kippen. Die Zeichenschicht kommt später und wird bei „Animationen reduzieren“ und im Hohen Kontrast nicht erzeugt (ADR-018); die XAML-Ansicht dieses Blatts ist genau diese statische Ansicht und bleibt danach bestehen.
- Tastatur: Enter auf „Umwandeln“, Leertaste auf „Pause/Weiter“, Esc schließt Flyouts. Reihenfolge: Eingang → Wirbel → Ergebnis/Speicherort → Liste → Leiste.
- Hoher Kontrast: Farbpunkte fallen auf die Textfarbe zusammen (Tokens), Zustände stehen immer als Text.
- Zielpfade sind lang: `TextTrimming=CharacterEllipsis` am Ordnerteil, voller Pfad als `ToolTip` und im Automation-Namen.

## Reihenfolge der Teilaufgaben

### A · Koordinator und Zielpfad-Logik (ui-entwickler, mit tester)

- `Services/TargetPathPlanner.cs` (statisch, rein): `EffectiveLocation`, `Preview`, `PreviewAll`, `NeedsFolder`. Eingaben: `TargetPlan`, `OutputLocation`, `IReadOnlyDictionary<string, OutputLocation>`, `FormatRegistry`, `DateTimeOffset`, `Func<string, bool> exists`, `string temporaryRoot`. Signaturen in `03-architektur.md`.
- `Services/ConversionCoordinator.cs`: `IConversionCoordinator` mit `Start`, `RelocateWaiting`, `PauseAll`, `ResumeAll`, `CancelAll`, `Reset`, `Jobs`, `State`, `Overall`, `Report`, `Changed`. Abhängigkeiten nur `IJobQueue`, `IUiDispatcher`, `FormatRegistry`, `TimeProvider`. Singleton in `ServiceRegistration`.
- `IWorkflowSession`: `Location` wird `OutputLocation?` (null = noch nicht gewählt), neu `OwnLocations` und `SetOwnLocation(string inputPath, OutputLocation? location)`; `Reset` löscht beides.
- `Services/ShellLauncher.cs`: `IShellLauncher` (`OpenFileAsync(path)`, `ShowInFolderAsync(path)`, `OpenFolderAsync(directory)`) über `Windows.System.Launcher` mit `FolderLauncherOptions.ItemsToSelect`; WinRT, deshalb eigene Datei, nicht im Test-Projekt.
- Tests (ohne WinUI, `tests/Kvertis.App.Tests`): Vorschau nummeriert gleiche Namen innerhalb der Runde (`a.png` und `a.jpg` → `a.jpg`, `a_1.jpg`); `EnsureUnique` gegen `exists`; eigenes Ziel schlägt gemeinsames; Zwischenablage-Datei mit `SameFolder` → `NeedsFolder`; `Start` reiht genau `Plan.Items.Count` Jobs mit fortlaufendem `BatchIndex` ein (Fake-`IJobQueue`); `JobChanged` fremder Ids wird ignoriert; `RelocateWaiting` ersetzt nur wartende Jobs und behält `BatchIndex`; `Report` zählt Completed/Failed/Cancelled und Bytes; `Changed` kommt über den Dispatcher (Fake mit synchronem `Post`); `Reset` entfernt nur eigene Jobs.

### B · XAML der ConvertPage (ui-entwickler)

- `ViewModels/Convert/`: `ConvertPageViewModel`, `ConvertRowViewModel`, `LocationCardViewModel`, `RoundReportViewModel`. Aufbau in `03-architektur.md`. Views binden nur an ViewModels; kein `ConversionJob` in XAML.
- `Views/ConvertPage.xaml` nach der Tabelle oben. `SwirlHost` ist ein leeres `Grid` mit `MinHeight=220`, `KvDeepBrush`, Radius 8; die zwei Wirbel-Karten liegen darin als normale XAML-Kinder, damit die spätere Zeichenschicht **hinter** sie gelegt werden kann (`Canvas.ZIndex`).
- Speicherort-Karte: `AllowDrop=True`, `DragOver` akzeptiert nur `StorageItems`, die genau einen Ordner enthalten (`DataPackageOperation.Link`, Beschriftung `Convert_Location_DropCaption`); `Drop` setzt `OutputLocation.Custom(folder.Path)` und merkt den Ordner über `RememberOutputFolder`. Flyout „Speicherort ändern“: drei `RadioButton` (`Convert_Location_Same`, `_Sub`, `_Custom`) und Knopf „Ordner wählen…“ (`IFilePickerService.PickFolderAsync`).
- Zeilen-Flyout „Ändern“: dieselben drei Optionen plus „wie alle“ (`Convert_Row_Location_Shared`). Zeile mit eigenem Ziel trägt die Marke `Convert_Row_OwnTarget`.
- Fehlerzeile: `Error_<Code>_Title` fett, `Error_<Code>_Body` als Zweittext (`ErrorMessageMapper.Map`), Koralle nur als Symbol und Rand, nie allein als Träger.
- Ressourcen: Schlüsselmuster `Convert_*` (`Convert_Inbox_*`, `Convert_Swirl_*`, `Convert_Result_*`, `Convert_Location_*`, `Convert_Bag_*`, `Convert_List_*`, `Convert_Row_*`, `Convert_Overall_*`, `Convert_Start_Button`, `Convert_Pause_Label`, `Convert_Resume_Label`, `Convert_CancelAll_*`, `Convert_NewRound_Button`, `Convert_Report_*`, `Convert_Announce_*`, `Convert_Trust_Text`, `Convert_Skipped_*`), alle in `de-DE` und `en-US`. Bestehende `Convert_Title`, `Convert_NoPlan_Text` bleiben; `Convert_Placeholder_Text` und `Convert_Plan_Text` entfallen.

### C · Verdrahtung und Abbau des alten Weges (ui-entwickler, reviewer)

Erst wenn A und B laufen (Durchlauf Bild, Audio, Dokument, 3D unter Windows), wird der alte Start-Weg entfernt. In einem Zug, damit nie zwei Wege Jobs einreihen:

- **`MainPage.xaml`:** entfernen `StartButton`, `Main_Output_ComboBox` mit `Main_Output_Change`, `OverallPanel` (Text und `ProgressBar`), `PauseAllButton`, `Main_CancelAll_Button`, `Main_ClearFinished_Button`, Pro-Karte (`ProCardText`, `Main_ProCard_*`), `AnnouncementText`. Bleiben: Ablagefläche mit Dialogen und Zwischenablage, `Main_Adding_Progress`, Vertrauenszeile, Karten-Liste, `GoTargetButton` „Weiter: Ziel“, Verlaufs-`SplitView`.
- **`JobCardControl.xaml`:** entfernen `FormatChip`, `PresetChip`, `MoreButton` mit `MorePanel`, Vorschau-Knopf, `PauseButton`, Cancel/OpenFolder/Again/Retry, Fortschrittsbalken, Haken, Fehlerbereich mit `Card_WhatToDo_Link`, Größen- und Zeitschätzung. Bleiben: Miniatur oder Typ-Symbol, Formatschild, Name, Größe, Maße/Dauer/Seiten, Warnhinweis (falsche Endung, VFR, Transparenz), Zustände Erkennen/Bereit/Abgelehnt, Entfernen (Knopf und Entf-Taste).
- **`MainViewModel`:** entfernen alles aus der Tabelle „Was aus `MainViewModel` wandert“ sowie `PresetOptions`, `IJobItemHost.OnItemSettingsChanged/PauseOrResume/Cancel/Retry/OpenFolderAsync/ShowErrorHelpAsync/RequestPreview/ApplyToAll`, `PreviewRequested`, `_queue`-Abonnement, `HasFinishedJobs`, `IsPausedAll`, `OverallProgress`, `OverallText`, `Announcement`. `AddPathsWithSettingsAsync` verliert den Parameter `settings` (der Verlauf geht über `session.Previous` nach Schritt 2). `HasStagedJobs` steuert nur noch `GoToTargetCommand`.
- **Dateien löschen:** `Views/MorePanel.xaml(.cs)`, `Views/FormatPickerFlyout.xaml(.cs)`. `Views/PreviewDialog` und `PreviewViewModel` bleiben im Baum (Vorschau aus Schritt 2 ist ein späteres Blatt), werden aber nirgends mehr aufgerufen; `ViewModels/Options.cs` behält `PresetOption` nur, wenn Schritt 2 es noch nutzt, sonst weg.
- **Verlauf:** `HistoryViewModel.AgainAsync` bleibt (setzt `Previous`, springt nach Schritt 2); „Ordner öffnen“ im Verlauf nutzt `IShellLauncher`.
- **Ressourcen-Schlüssel, die frei werden** (in `de-DE` und `en-US` löschen, nicht umwidmen; `tools/compliance/check-resw.py` meldet sonst Waisen): `Main_Start_Button`, `Main_Output_ComboBox`, `Main_Output_Same`, `Main_Output_Sub`, `Main_Output_Custom`, `Main_Output_Change`, `Main_Overall_Progress`, `Main_Overall_AutomationName`, `Main_Overall_Count`, `Main_Overall_Done`, `Main_Overall_Estimate`, `Main_Overall_ProgressText`, `Main_Overall_Remaining`, `Main_PauseAll_Label`, `Main_ResumeAll_Label`, `Main_CancelAll_Button`, `Main_ClearFinished_Button`, `Main_ProCard_LearnMore`, `Main_ProCard_Dismiss`, `Main_Announce_Completed`, `Main_Announce_Failed`, `Main_Announce_Cancelled`, alle `More_*`, alle `FormatPicker_*`, `Card_OutputChip_Text`, `Card_OutputChip_AutomationName`, `Card_PresetChip_AutomationName`, `Card_Pause_Label`, `Card_Resume_Label`, `Card_Remaining_Text`, `Card_Result_Text`, `Card_EstimateSize_Text`, `Card_EstimateTime_Text`, `Card_WhatToDo_Link`, `Card_Preview_Button`, `Card_More_Button`, `Card_Cancel_Button`, `Card_OpenFolder_Button`, `Card_Again_Button`, `Card_Retry_Button`, `Card_State_Queued`, `Card_State_Running`, `Card_State_Paused`, `Card_State_Completed`, `Card_State_Failed`, `Card_State_Cancelled`, `Card_AutomationName` (die Variante `Card_AutomationName_NoFormat` wird zum einzigen Kartennamen und darf zu `Card_AutomationName` umbenannt werden). `Preset_*` bleiben (Marken der Größenleiste, Verlaufstexte). `Error_*` bleiben und werden von Schritt 3 gebraucht.
- **Schrittleiste:** Sperre während des Laufs (siehe oben).
- **Abnahme (reviewer):** kein `IJobQueue`-Aufruf mehr außerhalb von `ConversionCoordinator` und `HistoryViewModel`; kein `SystemAccentColor`; jeder neue Text in beiden Sprachen; `bash tools/compliance/check.sh` grün; Durchlauf mit Zwischenablage-Bild (erzwingt Ordnerwahl), mit zwei gleichnamigen Quellen (Nummerierung), mit Abbruch während des Laufs (`.kvertis-tmp` weg, Zeile „Abgebrochen“), mit Speicherort-Wechsel nach dem Start (nur wartende Zeilen ändern den Pfad).

## Nicht in diesem Blatt

Zeichenschicht (Wirbel, weißes Loch, Blätter, Übergänge, Supernova), Einzel-Pause und „Erneut versuchen“ je Zeile, „Direkt umwandeln“ von Schritt 1, Nachher-Aktionen (Original verschieben, PC herunterfahren; Phase 2 laut `01-anforderungen.md`), Rückfrage beim Schließen des Fensters während eines Laufs, Staubringe ab 150 Dateien, Vorschau-Dialog aus Schritt 2.

## Offene Fragen an den Projektinhaber

Keine, die die Umsetzung blockieren.
