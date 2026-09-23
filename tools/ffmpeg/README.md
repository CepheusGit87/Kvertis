# FFmpeg für Kvertis (LGPL-Build)

Kvertis nutzt FFmpeg **nur als separaten Prozess** (`ffmpeg.exe`, `ffprobe.exe`) und **nur als LGPL-Build**.
Binärdateien liegen nicht im Repository (`tools/ffmpeg/bin/` ist ignoriert). Die App sucht sie unter
`Tools/ffmpeg/<arch>/` neben der Anwendung, ersatzweise im benutzerdefinierten Pfad aus den Einstellungen.

## Pflicht-Konfiguration

```
--disable-gpl --disable-nonfree --disable-version3
--enable-mediafoundation          # h264_mf, hevc_mf, aac_mf, mp3_mf (Windows-Systemcodecs)
--enable-libvpx --enable-libopus --enable-libvorbis --enable-libmp3lame
--enable-libaom --enable-libsvtav1 --enable-libdav1d --enable-libwebp
```

Verboten: `--enable-gpl`, `--enable-nonfree`, `libx264`, `libx265`, `libfdk-aac`, `libxvid`.

## Bezugsquellen (Stand: offener Punkt O-02, Entscheidung des Projektinhabers)

1. **Eigener Build** über ein Build-Skript (z. B. in einer GitHub-Actions-Pipeline mit MSYS2/MinGW oder
   `vcpkg`), das genau die obige Konfiguration setzt. Vorteil: volle Kontrolle, Quellcode-Angebot trivial
   (die Pipeline archiviert die Quell-Tarballs). Empfohlen.
2. **Fertige LGPL-Builds** von Drittanbietern, die ihre Build-Skripte veröffentlichen. Vor der Nutzung mit
   `check-build.sh` bzw. `check-build.ps1` prüfen und die Quellen des exakten Builds sichern.

Das Quellcode-Angebot (LGPL 2.1 §6) umfasst: FFmpeg-Quellen der verwendeten Version, alle Patches,
die Build-Konfiguration und die Quellen der LGPL-Bibliotheken (`libmp3lame`). Ablageort: siehe
`docs/09-roadmap.md` O-02.

## Prüfung

```
bash tools/ffmpeg/check-build.sh path/to/ffmpeg        # Linux/macOS/WSL
pwsh tools/ffmpeg/check-build.ps1 path\to\ffmpeg.exe   # Windows
```

Beide Skripte lesen `ffmpeg -version` und `ffmpeg -encoders` und schlagen fehl bei GPL/non-free oder
verbotenen Encodern. Dieselbe Prüfung läuft in der App (`FfmpegCompliance`) und im Integrationstest
`FfmpegBuildComplianceTests`.
