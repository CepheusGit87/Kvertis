# FFmpeg für Kvertis (eigener LGPL-Build, nur patentfreie Komponenten)

Kvertis nutzt FFmpeg **nur als separaten Prozess** (`ffmpeg.exe`, `ffprobe.exe`) und **nur als eigenen Build**,
in dem ausschließlich patentfreie Codecs enthalten sind. Binärdateien liegen nicht im Repository
(`tools/ffmpeg/bin/` ist ignoriert). Die App sucht sie unter `Tools/ffmpeg/<arch>/` neben der Anwendung,
ersatzweise im benutzerdefinierten Pfad aus den Einstellungen.

## Warum ein eigener Build

Auch ein regulärer LGPL-Build von FFmpeg enthält Decoder für H.264, HEVC, AAC, MPEG-4 Part 2 und WMV/WMA.
Wer sie ausliefert, verbreitet patentbelastete Codecs, auch wenn die App sie nie aufruft. Kvertis liefert
deshalb einen Build aus, in dem diese Decoder **gar nicht einkompiliert** sind. Alles, was H.264, HEVC, AAC
oder Windows-Media-Formate betrifft, dekodiert und kodiert Windows selbst (Media Foundation), siehe ADR-015
in `docs/03-architektur.md`.

## Konfiguration

Die vollständige Allowlist steht in [`configure-allowlist.txt`](configure-allowlist.txt). Kernpunkte:

- `--disable-gpl --disable-nonfree --disable-version3` (LGPL 2.1)
- `--disable-everything` und danach nur die aufgeführten Demuxer, Muxer, Decoder, Encoder, Parser, Filter
- `--disable-network`, Protokolle nur `file` und `pipe`
- Decoder: VP8, VP9, AV1 (dav1d), Theora, MPEG-1/2 (Patente abgelaufen 2018), MJPEG, PNG, GIF, BMP, Opus,
  Vorbis, FLAC, MP3 (abgelaufen 2017), MP2, AC-3 (abgelaufen 2017), ALAC, WavPack, PCM
- Encoder: VP8/VP9 (libvpx), AV1 (SVT-AV1), Opus, Vorbis, FLAC, ALAC, MP3 (LAME, LGPL), PNG, MJPEG, GIF, PCM
  sowie die Media-Foundation-Wrapper `h264_mf`, `hevc_mf`, `aac_mf`, `mp3_mf` (nutzen die Windows-Systemcodecs)
- Externe Bibliotheken: libvpx (BSD), libopus (BSD), libvorbis (BSD), libmp3lame (LGPL), dav1d (BSD-2),
  SVT-AV1 (BSD-2 mit Patentklausel)

**Verboten** und durch die Prüfskripte abgefangen: `--enable-gpl`, `--enable-nonfree`, Encoder `libx264`,
`libx265`, `libfdk_aac`, `libxvid`, Decoder `h264`, `hevc`, `aac`, `mpeg4`, `wmv*`, `wma*`, `vc1`, `h263`,
`prores`, `dnxhd`, `eac3`, `dca`, `truehd`, `amr*`, Netzwerkprotokolle.

## Bauen

Der Workflow [`.github/workflows/ffmpeg-build.yml`](../../.github/workflows/ffmpeg-build.yml) (manuell
auslösbar, Eingabe: FFmpeg-Tag) baut `ffmpeg.exe` und `ffprobe.exe` für Windows x64 mit MSYS2/MinGW,
prüft sie mit `check-build.sh` und legt ein Artefakt mit Binärdateien, Lizenztext und **Quellcode-Angebot**
(FFmpeg-Quellen, Commit, Konfiguration, Paketliste der Bibliotheken) ab. Dieses Artefakt ist bei jeder
Veröffentlichung zusammen mit der App zu archivieren (LGPL 2.1 §6). ARM64 ist offener Punkt O-02.

Der Workflow ist noch nicht gelaufen (erster Lauf unter GitHub Actions steht aus).

## Prüfen

```
bash tools/ffmpeg/check-build.sh path/to/ffmpeg        # Linux/macOS/WSL
pwsh tools/ffmpeg/check-build.ps1 path\to\ffmpeg.exe   # Windows
```

Beide Skripte lesen `-version`, `-encoders`, `-decoders` und `-protocols` und schlagen bei jedem Verstoß fehl.
Dieselbe Prüfung läuft in der App (`FfmpegCompliance`) und im Integrationstest `FfmpegBuildComplianceTests`.
