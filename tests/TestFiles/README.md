# Testdateien

Alle Testeingaben werden **im Code erzeugt** (Magick.NET für Bilder, PDFsharp für PDF, OpenXml SDK für Office,
Byte-Arrays für Magic-Byte-Tests). So liegen keine Binärdateien unklarer Herkunft im Repository.

Dateien in diesem Ordner sind ausschließlich selbst erzeugte, kleine Fixtures (< 200 KB) mit Herkunftsangabe
in dieser Tabelle. Neue Dateien nur mit Eintrag.

| Datei | Herkunft | Zweck |
|---|---|---|
| (noch keine) | | |

Integrationstests, die echte `ffmpeg`-Binärdateien brauchen, erzeugen ihre Eingaben zur Laufzeit mit ffmpeg
(`-f lavfi -i sine`, `-f lavfi -i testsrc`) und werden übersprungen, wenn ffmpeg fehlt.
