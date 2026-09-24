# Oberfläche – Entwürfe und Entscheidungen

Stand: 23.09.2026. HTML-Entwürfe in diesem Ordner, jeweils auch als private Artifact-Seite veröffentlicht.

## Festgehalten

| Bereich | Entscheidung | Entwurf |
|---|---|---|
| Farben | ShoeBox-Tokens: Mint = einzige Aktionsfarbe, Blau = Bilder, Violett = Audio, Bernstein = Video, Cyan = Dokumente, Koralle = Fehler. Hell und Dunkel. | alle |
| Grundaufbau | Mischentwurf: Sortierregal (Schritt 1) → Ziel (Schritt 2) → Umwandeln (Schritt 3), roter Faden oben | `oberflaeche-mischentwurf.html` |
| Verlauf | „Zusammengeführt“: Zeitleiste links (aus 1), Detail rechts (aus 4), Suche nach Dateinamen, Filter-Knöpfe | `verlauf-konzepte.html`, erstes Fenster |
| Anpassen aus dem Verlauf | Schritt 2 zeigt die alten Einstellungen („damals“-Marken, „Alte Werte übernehmen“) | `oberflaeche-mischentwurf.html` |
| Sanduhr im Sortierregal | Glas-Sanduhr in 3D, alle Formate sichtbar; kippt nach vorn (Konzept 4), eigener Takt je Kiste: 25 / 35 / 45 / 55 s | `sanduhr-wende.html` |
| Schritt 3 | Förderband + Sanduhr-Station 1: Datei zerfällt zu Partikeln, sickert durch, kommt als neues Blatt heraus, Sanduhr kippt nach vorn und bleibt so stehen; darunter die Kompakt-Liste | `sanduhr-foerderband.html` |
| Pro | In den Konzepten ausgeblendet, alle Funktionen sichtbar | alle |

## Offen

- Schritt 3: Förderband bleibt, Sanduhr fällt weg. Fünf Stationen in `foerderband-effekte.html` (Lichttor, Stempel, Pixel-Tausch, Tunnel, Wirbel), Liste nur „Format → Format“ in drei Ansichten (Pfeil, Vorher/Nachher, Kapsel). Vorschlag: Lichttor + Pfeil. Die sechs freien Konzepte in `schritt3-konzepte.html` sind verworfen.
- Speicherort: global und je Datei, fünf Konzepte in `speicherort-konzepte.html` (Vorschlag: 1 Global mit Ausnahmen, 2 als Erweiterung).
- Eigene Akzentfarbe statt Systemakzent: braucht eine ADR in `docs/03-architektur.md` und eine Änderung in `docs/06-design.md`.
- Schrift Geist (SIL OFL): steht nicht auf der Lizenzliste in `CLAUDE.md`, Prüfung durch den Lizenz-Wächter; bis dahin Segoe UI Variable.
- Barrierefreiheit: alle 3D-Effekte und Animationen entfallen bei „Animationen reduzieren“ und Hohem Kontrast.

## Weitere Entwurfsseiten (Zwischenstände)

`oberflaeche-konzepte.html` (A Durchlauf, B Kartei, C Tiefenebenen), `wird-zu-ideen.html`, `sanduhr-varianten.html`, `sanduhr-glas-plattform.html`, `sanduhr-3d.html`, `umwandeln-konzepte.html`, `foerderband-kombis.html`.
