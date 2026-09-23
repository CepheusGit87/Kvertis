# 07 – Microsoft Store

## Manifest (`Package.appxmanifest`)

Kvertis ist eine paketierte Desktop-App (WinUI 3, MSIX). Jede Berechtigung braucht eine Begründung in dieser Tabelle. Alles, was hier nicht steht, ist verboten.

| Berechtigung | Enthalten? | Begründung |
|---|---|---|
| `runFullTrust` | ja | Pflicht für paketierte Desktop-Apps mit WinUI 3. Damit läuft Kvertis wie eine klassische Desktop-App; Dateizugriff erfolgt trotzdem ausschließlich über Picker, Drag-and-drop und Zwischenablage (Policy, geprüft im Review). |
| `internetClient` | **nein** | Kein Netzwerkcode. Die Visual-Studio-Vorlage trägt diese Berechtigung standardmäßig ein; sie wird entfernt. |
| `internetClientServer`, `privateNetworkClientServer` | **nein** | |
| `broadFileSystemAccess` | **nein** | |
| `picturesLibrary`, `videosLibrary`, `musicLibrary`, `documentsLibrary` | **nein** | Nicht nötig, da Picker und Drag-and-drop Zugriff gewähren. |
| `removableStorage` | **nein** | Picker reicht. |

Hinweis zur Ehrlichkeit: Eine Full-Trust-App könnte technisch auf Dateien und Netzwerk zugreifen. Das Versprechen „Ihre Dateien verlassen nie diesen PC“ wird deshalb durch drei Dinge abgesichert: kein Netzwerkcode (Grep im Review, `lizenz-waechter`), keine Netzwerkberechtigung im Manifest, und Dateizugriff nur auf vom Nutzer gewählte Pfade.

Weitere Manifest-Einträge:

- `Identity`: Name und Publisher werden aus dem Partner Center übernommen (offener Punkt: Store-Reservierung des Namens „Kvertis“).
- `DisplayName`: Kvertis. `Description`: aus den Store-Texten unten.
- Dateitypzuordnungen: keine in Phase 1. Phase 2: Explorer-Kontextmenü über `desktop4:FileExplorerContextMenus` mit einem Sparse-Package-Ansatz, dann neu bewerten.
- Mindestversion: Windows 10 1809 (Build 17763) als `MinVersion`, Ziel Windows 11. Mica ist auf Windows 10 nicht verfügbar; Fallback siehe `06-design.md`.
- `uap:SupportedRotations`, Hintergrundaufgaben, Startaufgaben: keine.

## MSIX-Paketierung

- Projekt `Kvertis.App` mit `WindowsPackageType=MSIX`, Single-Project-MSIX.
- Architekturen: x64 und ARM64. FFmpeg-Build muss für beide vorliegen (offener Punkt: ARM64-Build der LGPL-Konfiguration).
- Signierung: Store-Signatur bei Einreichung; lokale Testsignatur mit selbst erzeugtem Zertifikat, das nicht ins Repo kommt.
- Version `Major.Minor.Build.0` (Store verlangt letzte Stelle 0). Version wird aus einem Git-Tag gesetzt.
- Inhalte: `ffmpeg.exe`, `ffprobe.exe` unter `Tools/ffmpeg/<arch>/`, Lizenztexte unter `ThirdParty/`, Assets in allen geforderten Größen (Platzhalter).
- Paketgröße: Ziel unter 120 MB. FFmpeg-Build ohne unnötige Demuxer/Filter, um Größe und Angriffsfläche zu senken (Build-Konfiguration in `tools/ffmpeg/`).

## In-App-Kauf

- Ein Add-on: **Kvertis Pro**, dauerhaft (Durable), einmaliger Kauf.
- API: `Windows.Services.Store.StoreContext`. Bei WinUI 3 Desktop muss der `StoreContext` mit dem Fensterhandle initialisiert werden (`InitializeWithWindow`).
- `ILicenseService` in `Kvertis.App`: `IsPro`, `PurchaseAsync()`, `RefreshAsync()`. Der Status wird beim Start abgefragt und gecacht; ohne Store-Verbindung gilt der letzte bekannte Status. Der Store-Client selbst ist Teil von Windows, Kvertis enthält keinen eigenen Netzwerkcode.
- Limits Gratis: keine Video-Konvertierung; Batch-Größe begrenzt (Standard 5 Dateien, endgültiger Wert offener Punkt). Durchsetzung beim Erzeugen der Jobs in der UI.
- Keine Werbung, keine Wasserzeichen, keine Abo-Option in Phase 1.
- Kauf wiederherstellen: automatisch über den Store-Account des Nutzers; Button „Wiederherstellen“ ruft `RefreshAsync()`.

## Kryptografie-Deklaration

Phase 1: Kvertis nutzt keine Verschlüsselung. Antwort im Partner Center: **Nein**. Die App liest keine verschlüsselten Dateien und erzeugt keine.

Wird PDF-Passwortschutz (Verschlüsselung beim Erzeugen) geplant, gilt vor der Umsetzung:

1. Deklaration neu bewerten (AES in PDF ist Verschlüsselung; in der Regel „ja, Standardalgorithmen für Dateischutz“ und damit ohne ECCN-Pflicht, aber zu prüfen).
2. Eintrag in `02-rechtssicherheit.md` §7 und hier.
3. Nie: Entfernen von Passwörtern aus fremden Dateien.

## Datenschutzerklärung

Der Store verlangt eine URL. Inhalt (kurz, wahr):

> Kvertis verarbeitet Ihre Dateien ausschließlich auf Ihrem Gerät. Die App hat keinen Internetzugang, erhebt keine Daten, sendet keine Daten und speichert keine personenbezogenen Daten außerhalb Ihres Geräts. Der Verlauf und die Einstellungen liegen lokal im App-Datenordner und können in der App gelöscht werden. In-App-Käufe werden vollständig vom Microsoft Store abgewickelt; Kvertis erhält dabei keine persönlichen Daten.

Ort der URL: offener Punkt (eigene Domain oder Repository-Seite).

## Store-Texte (Entwurf)

### Deutsch

**Kurzbeschreibung:** Dateien umwandeln, ohne dass sie Ihren PC verlassen. Bilder, Audio, Video und Dokumente. Offline, ohne Konto.

**Beschreibung:**
Kvertis wandelt Ihre Dateien direkt auf Ihrem PC um. Ziehen Sie eine Datei in das Fenster, bestätigen Sie den Vorschlag, fertig.

- Bilder: JPG, PNG, WebP, HEIC, TIFF, RAW und mehr
- Audio: MP3, WAV, FLAC, OGG, Opus, M4A
- Video (Pro): MP4, MKV, WebM
- Dokumente: PDF, Word, Excel, PowerPoint, Text, Markdown
- Presets: Für Messenger, Für E-Mail, Für Social Media, Für Website, Archiv
- Zielgröße eingeben, Kvertis berechnet den Rest
- Vorher/Nachher-Vorschau mit Größenvergleich
- EXIF- und GPS-Daten werden standardmäßig entfernt
- Mehrere Dateien gleichzeitig, mit Zeitschätzung

Ihre Dateien verlassen nie diesen PC. Kein Konto, keine Cloud, keine Werbung, keine Datensammlung.

Kvertis Pro (einmaliger Kauf) schaltet Video-Konvertierung und unbegrenzte Stapel frei.

### English

**Short description:** Convert files without them ever leaving your PC. Images, audio, video and documents. Offline, no account.

**Description:**
Kvertis converts your files right on your PC. Drop a file into the window, confirm the suggestion, done.

- Images: JPG, PNG, WebP, HEIC, TIFF, RAW and more
- Audio: MP3, WAV, FLAC, OGG, Opus, M4A
- Video (Pro): MP4, MKV, WebM
- Documents: PDF, Word, Excel, PowerPoint, text, Markdown
- Presets: For messaging, For email, For social media, For web, Archive
- Enter a target size and Kvertis works out the settings
- Before/after preview with size comparison
- EXIF and GPS data removed by default
- Convert many files at once, with time estimates

Your files never leave this PC. No account, no cloud, no ads, no data collection.

Kvertis Pro (one-time purchase) unlocks video conversion and unlimited batches.

Anmerkung: „Word“, „Excel“, „PowerPoint“ sind Formatbezeichnungen des Betriebssystem-Herstellers, in dessen Store die App erscheint. `lizenz-waechter` prüft vor der Einreichung, ob die Store-Richtlinien diese Nennung erlauben; andernfalls „DOCX, XLSX, PPTX“.

### Suchbegriffe

Konverter, umwandeln, HEIC zu JPG, MP4, MP3, PDF, WebP, komprimieren, offline, Datenschutz.

### Screenshots (Liste)

1. Hauptansicht mit Ablagefläche und Vertrauenszeile.
2. Job-Liste mit Formatvorschlägen.
3. Vorschau vorher/nachher.
4. „Mehr“-Panel mit Zielgröße.
5. Fertige Konvertierung mit Größenvergleich.

## Einreichungs-Checkliste

- [ ] Name „Kvertis“ im Partner Center reserviert.
- [ ] Manifest ohne Netzwerkberechtigungen, geprüft durch `lizenz-waechter`.
- [ ] Third-Party-Licenses-Seite vollständig, inkl. LGPL-Text und FFmpeg-Quellcode-Angebot.
- [ ] FFmpeg-Build-Nachweis (LGPL, keine libx26x) für x64 und ARM64.
- [ ] Datenschutz-URL erreichbar.
- [ ] Kryptografie-Deklaration: Nein.
- [ ] Altersfreigabe-Fragebogen ausgefüllt.
- [ ] Add-on „Kvertis Pro“ angelegt und in der App getestet (Sandbox).
- [ ] Store-Texte DE/EN ohne fremde Markennamen.
- [ ] Screenshots ohne fremde Inhalte oder Marken.
- [ ] Barrierefreiheit: Screenreader-Durchlauf des Hauptwegs dokumentiert.
