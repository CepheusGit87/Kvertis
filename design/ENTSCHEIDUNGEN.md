# Oberfläche – Entwürfe und Entscheidungen

Stand: 24.09.2026. HTML-Entwürfe in diesem Ordner, jeweils auch als private Artifact-Seite veröffentlicht.

## Festgehalten

| Bereich | Entscheidung | Entwurf |
|---|---|---|
| Farben | ShoeBox-Tokens: Mint = einzige Aktionsfarbe, Blau = Bilder, Violett = Audio, Bernstein = Video, Cyan = Dokumente, Koralle = Fehler. Hell und Dunkel. | alle |
| Grundaufbau | Mischentwurf: Sortierregal (Schritt 1) → Ziel (Schritt 2) → Umwandeln (Schritt 3), roter Faden oben | `oberflaeche-mischentwurf.html` |
| Verlauf | „Zusammengeführt“: Zeitleiste links (aus 1), Detail rechts (aus 4), Suche nach Dateinamen, Filter-Knöpfe | `verlauf-konzepte.html`, erstes Fenster |
| Anpassen aus dem Verlauf | Schritt 2 zeigt die alten Einstellungen („damals“-Marken, „Alte Werte übernehmen“) | `oberflaeche-mischentwurf.html` |
| Sanduhr im Sortierregal | Glas-Sanduhr in 3D, alle Formate sichtbar; kippt nach vorn (Konzept 4), eigener Takt je Kiste: 25 / 35 / 45 / 55 / 65 s (fünfte Kiste: 3D-Modelle) | `sanduhr-wende.html` |
| Schritt 3 | Wirbel-Variante 5 „Stapel → Zielordner“: links Eingangsstapel, Mitte Pixelwirbel (zwei Dateien zugleich), rechts großer Zielordner mit „Speicherort ändern“ (Ziel für alle, auch per Ordner aus dem Explorer ziehen) und Tasche für Dateien mit eigenem Ziel. Darunter die Liste: Format → Format, voller Zielpfad, „Ändern“, danach „Öffnen“ und „Im Ordner zeigen“. Förderband und Sanduhr-Station entfallen, die Sanduhren im Sortierregal bleiben. | `oberflaeche-mischentwurf.html`, `wirbel-varianten.html` |
| Speicherort | Ein Ziel für alle (Neben dem Original, Unterordner „Kvertis“, eigener Ordner) plus Ausnahmen je Datei, siehe Schritt 3. Löst `speicherort-konzepte.html` ab. | `oberflaeche-mischentwurf.html` |
| Pro | In den Konzepten ausgeblendet, alle Funktionen sichtbar | alle |

## Offen

- Win2D (MIT) für den Pixelwirbel: Prüfung durch den Lizenz-Wächter, Eintrag in `docs/04-bibliotheken.md` erst bei Verwendung im Code.
- Eigene Akzentfarbe statt Systemakzent: braucht eine ADR in `docs/03-architektur.md` und eine Änderung in `docs/06-design.md`.
- Schrift Geist (SIL OFL): steht nicht auf der Lizenzliste in `CLAUDE.md`, Prüfung durch den Lizenz-Wächter; bis dahin Segoe UI Variable.
- Barrierefreiheit: alle 3D-Effekte und Animationen entfallen bei „Animationen reduzieren“ und Hohem Kontrast.

## Weitere Entwurfsseiten (Zwischenstände)

`oberflaeche-konzepte.html` (A Durchlauf, B Kartei, C Tiefenebenen), `wird-zu-ideen.html`, `sanduhr-varianten.html`, `sanduhr-glas-plattform.html`, `sanduhr-3d.html`, `umwandeln-konzepte.html`, `foerderband-kombis.html`, `sanduhr-foerderband.html`, `schritt3-konzepte.html`, `foerderband-effekte.html`, `pixel-konzepte.html`, `wirbel-varianten.html`.
