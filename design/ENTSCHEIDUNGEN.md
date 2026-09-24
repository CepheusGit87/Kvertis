# Oberfläche – Entwürfe und Entscheidungen

Stand: 24.09.2026. HTML-Entwürfe in diesem Ordner, jeweils auch als private Artifact-Seite veröffentlicht.

## Festgehalten

| Bereich | Entscheidung | Entwurf |
|---|---|---|
| Farben | ShoeBox-Tokens: Mint = einzige Aktionsfarbe, Blau = Bilder, Violett = Audio, Bernstein = Video, Cyan = Dokumente, Rosa = 3D-Modelle, Koralle = Fehler. Hell und Dunkel. | alle |
| Grundaufbau | Mischentwurf: Sortierregal (Schritt 1) → Ziel (Schritt 2) → Umwandeln (Schritt 3), roter Faden oben | `oberflaeche-mischentwurf.html` |
| Verlauf | „Zusammengeführt“: Zeitleiste links (aus 1), Detail rechts (aus 4), Suche nach Dateinamen, Filter-Knöpfe | `verlauf-konzepte.html`, erstes Fenster |
| Anpassen aus dem Verlauf | Schritt 2 zeigt die alten Einstellungen („damals“-Marken, „Alte Werte übernehmen“) | `oberflaeche-mischentwurf.html` |
| Sanduhr im Sortierregal | Glas-Sanduhr in 3D, alle Formate sichtbar; kippt nach vorn (Konzept 4), eigener Takt je Kiste: 25 / 35 / 45 / 55 / 65 s | `sanduhr-wende.html` |
| Schritt 3 | Förderband + Sanduhr-Station 1: Datei zerfällt zu Partikeln, sickert durch, kommt als neues Blatt heraus, Sanduhr kippt nach vorn und bleibt so stehen; darunter die Kompakt-Liste | `sanduhr-foerderband.html` |
| 3D-Modelle | Fünfte Kiste (ADR-016) in Rosa (#f08fd0 dunkel, #9a2f7d hell), Takt 65 s. Schritt 2 ohne Qualitätsregler und Metadaten-Schalter, nur Hinweis „Form verlustfrei, Farben und Materialien fallen weg“. Breite: 5 groß ab 1 350 px, 5 verkleinert ab 1 000 px, darunter 3 + 2 | `oberflaeche-mischentwurf.html`, `sanduhr-fuenf-kisten.html` |
| Pro | In den Konzepten ausgeblendet, alle Funktionen sichtbar | alle |

## Offen

- Speicherort: global und je Datei, fünf Konzepte in `speicherort-konzepte.html` (Vorschlag: 1 Global mit Ausnahmen, 2 als Erweiterung).
- Eigene Akzentfarbe statt Systemakzent: braucht eine ADR in `docs/03-architektur.md` und eine Änderung in `docs/06-design.md`.
- Schrift Geist (SIL OFL): steht nicht auf der Lizenzliste in `CLAUDE.md`, Prüfung durch den Lizenz-Wächter; bis dahin Segoe UI Variable.
- Barrierefreiheit: alle 3D-Effekte und Animationen entfallen bei „Animationen reduzieren“ und Hohem Kontrast.

## Weitere Entwurfsseiten (Zwischenstände)

`oberflaeche-konzepte.html` (A Durchlauf, B Kartei, C Tiefenebenen), `wird-zu-ideen.html`, `sanduhr-varianten.html`, `sanduhr-glas-plattform.html`, `sanduhr-3d.html`, `umwandeln-konzepte.html`, `foerderband-kombis.html`, `sanduhr-fuenf-kisten.html`.
