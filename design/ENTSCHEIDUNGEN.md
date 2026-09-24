# Oberfläche – Entwürfe und Entscheidungen

Stand: 24.09.2026. HTML-Entwürfe in diesem Ordner, jeweils auch als private Artifact-Seite veröffentlicht.

## Festgehalten

| Bereich | Entscheidung | Entwurf |
|---|---|---|
| Farben | ShoeBox-Tokens: Mint = einzige Aktionsfarbe, Blau = Bilder, Violett = Audio, Bernstein = Video, Cyan = Dokumente, Koralle = Fehler. Hell und Dunkel. | alle |
| Grundaufbau | Mischentwurf: Fächer-Galaxie (Schritt 1) → Ziel (Schritt 2) → Umwandeln (Schritt 3), roter Faden oben | `oberflaeche-mischentwurf.html` |
| Verlauf | „Zusammengeführt“: Zeitleiste links (aus 1), Detail rechts (aus 4), Suche nach Dateinamen, Filter-Knöpfe | `verlauf-konzepte.html`, erstes Fenster |
| Anpassen aus dem Verlauf | Schritt 2 zeigt die alten Einstellungen („damals“-Marken, „Alte Werte übernehmen“) | `oberflaeche-mischentwurf.html` |
| Schritt 1 | Fächer-Galaxie: oben groß fünf Umlaufbahnen um ein schwarzes Loch, eine je Dateiart, ohne Beschriftung. Darunter immer alle fünf Fächer, auch leere, mit Zahl, Dateinamen, Vorschaubild, Größe, Formatschild, Hinweis bei falscher Endung und „öffnet X → macht Y Formate“. Nicht Umwandelbares rechts oben mit Grund. Neue Dateien fliegen mit Formatschild auf ihre Bahn und gleichzeitig ins Fach. Klick auf Fach oder Bahn zoomt auf „Wege durchs Loch“ (links lesbare Formate, rechts Ziele, Empfehlung markiert), die Fächer werden zur Leiste. Maus beeinflusst die Bahnen sanft, alle Übergänge weich. Löst Sortierregal und Sanduhren ab. | `oberflaeche-mischentwurf.html`, `faecher-galaxie.html` |
| Spielereien in Schritt 1 | Maus etwa 4 s still auf einer Bahn: räumlicher Strudel, kleiner Blitz, ein neuer Planet entsteht (sechs Arten im Wechsel). Maus 10 s auf dem Loch: Sog wird immer schneller, Planeten stürzen mit Einschlag ins Loch, in den letzten 2 s wird die ganze Oberfläche eingesogen, dann Urknall und neues Universum. Nur in der Übersicht, nie bei „Animationen reduzieren“. | `faecher-galaxie.html` |
| Schritt 3 | Wirbel-Variante 5 „Stapel → Zielordner“: links Eingangsstapel, Mitte Pixelwirbel (zwei Dateien zugleich), rechts großer Zielordner mit „Speicherort ändern“ (Ziel für alle, auch per Ordner aus dem Explorer ziehen) und Tasche für Dateien mit eigenem Ziel. Darunter die Liste: Format → Format, voller Zielpfad, „Ändern“, danach „Öffnen“ und „Im Ordner zeigen“. Förderband und Sanduhr-Station entfallen. | `oberflaeche-mischentwurf.html`, `wirbel-varianten.html` |
| Speicherort | Ein Ziel für alle (Neben dem Original, Unterordner „Kvertis“, eigener Ordner) plus Ausnahmen je Datei, siehe Schritt 3. Löst `speicherort-konzepte.html` ab. | `oberflaeche-mischentwurf.html` |
| Pro | In den Konzepten ausgeblendet, alle Funktionen sichtbar | alle |

## Offen

- Schritt 2: `ziel-konzepte.html` → „Zoom-Wege“ gewählt, die Ausbauten in `zoom-einstellungen.html` wurden als zu unübersichtlich verworfen. Neuer Anlauf `ziel-einfach.html` mit der Regel „eine Frage auf einmal, große Knöpfe mit Größe, Details hinter ‚Mehr‘“: Ein Schieber, Wofür?, Vergleich, Karten, Einer für alle. Vorschlag: „Einer für alle“ als Einstieg, „Anders“ öffnet den Schieber je Art, Vergleich beim Überfahren. Bis zur Wahl bleibt Schritt 2 im Mischentwurf wie er ist.
- Win2D (MIT) für den Pixelwirbel und die Galaxie: Prüfung durch den Lizenz-Wächter, Eintrag in `docs/04-bibliotheken.md` erst bei Verwendung im Code.
- Eigene Akzentfarbe statt Systemakzent: braucht eine ADR in `docs/03-architektur.md` und eine Änderung in `docs/06-design.md`.
- Schrift Geist (SIL OFL): steht nicht auf der Lizenzliste in `CLAUDE.md`, Prüfung durch den Lizenz-Wächter; bis dahin Segoe UI Variable.
- Barrierefreiheit: alle 3D-Effekte und Animationen entfallen bei „Animationen reduzieren“ und Hohem Kontrast.

## Weitere Entwurfsseiten (Zwischenstände)

`oberflaeche-konzepte.html` (A Durchlauf, B Kartei, C Tiefenebenen), `wird-zu-ideen.html`, `sanduhr-varianten.html`, `sanduhr-glas-plattform.html`, `sanduhr-3d.html`, `umwandeln-konzepte.html`, `foerderband-kombis.html`, `sanduhr-foerderband.html`, `schritt3-konzepte.html`, `foerderband-effekte.html`, `pixel-konzepte.html`, `wirbel-varianten.html`, `sanduhr-wende.html`, `ereignishorizont-konzepte.html`, `umlaufbahn-varianten.html`, `fokus-varianten.html`, `zoom-varianten.html`, `einwurf-varianten.html`, `einwurf-uebersicht.html`, `ziel-konzepte.html`, `zoom-einstellungen.html`, `ziel-einfach.html`.
