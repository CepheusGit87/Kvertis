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

### 5. Verlauf (Seitenpanel)

Liste der letzten Konvertierungen: Datum, Datei, Format, Einstellungen. Button „Nochmal mit denselben Einstellungen“ (öffnet den Dateidialog mit vorbelegten Einstellungen) und „Ordner öffnen“. Verlauf ist lokal, löschbar.

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

- Hintergrund: Mica (Fenster), Acrylic für Flyouts. Fallback auf Windows 10: einfarbig aus den Systemfarben.
- Akzent: Systemakzentfarbe (`SystemAccentColor`), nicht überschreiben. Der Start-Button ist das einzige große Element in Akzentfarbe.
- Karten: `CardBackgroundFillColorDefault`, Radius 8 px, Schatten `ThemeShadow` mit Tiefe 8–16 je Zustand.
- Status: Erfolg `SystemFillColorSuccess`, Fehler `SystemFillColorCritical`, Warnung `SystemFillColorCaution`. Nie nur Farbe als Informationsträger, immer Icon + Text.
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

Platzhalter: ein abstraktes Symbol aus zwei ineinander übergehenden Formen (Wandlung), einfarbig auf Akzentfarbe. Kein Bezug zu bestehenden Marken. Wird später ersetzt; alle Größen liegen unter `Assets/` und werden aus einer SVG-Quelle erzeugt.

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

- **Ablauf in drei Schritten** mit rotem Faden oben: Dateien (Fächer-Galaxie) → Ziel → Umwandeln (Pixelwirbel, Zielordner).
- **Schritt 2 „Ziel“:** links je Dateiart ein kleines Universum aus flachem Linien-Symbol und Ring, farbig nur die gewählte Art, Bewegung nur beim Wechsel. Daneben die Dateien mit „Alle gleich / Jede einzeln“, in der Mitte die Wege durchs Loch, rechts die Zielformate mit Größe.
- **Ausgabe-Einstellungen rechts:** oben eine Note von 0 bis 100 für die Qualität, darunter eine Größenleiste von Rot bis Grün (Zielgröße je Datei, Marken wie „E-Mail“). Darunter zugeklappte Zonen, jede mit eigenem Balken (z. B. Schärfe, Details, Klang, Bewegung), dazu „Zielgröße genau“ und „Was sich ändert“. Das ersetzt das „Mehr“-Panel aus Abschnitt 3.
- **Übergänge:** Wurmloch. Das schwarze Loch bleibt über alle Schritte dasselbe Objekt: Es saugt die Fächer aus Schritt 1 ein und gibt sie links als Artwahl von Schritt 2 wieder frei, danach gibt es die Dateien als Blätter an den Eingangsstapel von Schritt 3 und geht im Pixelwirbel auf. Die Seiten selbst ändern sich dabei nicht.
- **Abschluss:** „Häkchen im Loch“. Nach dem letzten Wirbel bildet sich ein Loch und wird zum Häkchen, darunter das Ergebnis mit Anzahl und gesparter Größe. Fehlgeschlagene Dateien liegen wieder im Eingang, ihre Zeile zeigt den Grund, der Abschluss sagt „6 von 7“.
- **Barrierefreiheit:** Bei „Animationen reduzieren“ stehen alle Bewegungen still; Note und Balken haben `role="meter"` bzw. in WinUI `AutomationProperties.Name` mit Wert.

