#!/usr/bin/env bash
# Verifies that an ffmpeg binary is the Kvertis LGPL build: no GPL/nonfree, no forbidden encoders,
# no patent-encumbered decoders, no network protocols. Exit 1 on any violation.
set -euo pipefail
ffmpeg="${1:-ffmpeg}"
version="$("$ffmpeg" -hide_banner -version)"
encoders="$("$ffmpeg" -hide_banner -encoders 2>/dev/null)"
decoders="$("$ffmpeg" -hide_banner -decoders 2>/dev/null)"
protocols="$("$ffmpeg" -hide_banner -protocols 2>/dev/null)"
fail=0
echo "$version" | head -1

if echo "$version" | grep -q -- '--enable-gpl';      then echo "FAIL: --enable-gpl";      fail=1; fi
if echo "$version" | grep -q -- '--enable-nonfree';  then echo "FAIL: --enable-nonfree";  fail=1; fi
if echo "$version" | grep -q -- '--enable-version3'; then echo "WARN: --enable-version3 (LGPL 3 statt 2.1), Lizenztext prüfen"; fi
if echo "$version" | grep -q -- '--enable-network' || ! echo "$version" | grep -q -- '--disable-network'; then
  echo "WARN: Build nicht mit --disable-network konfiguriert"
fi

# Encoders that must never exist (GPL or non-free).
for enc in libx264 libx265 libfdk_aac libxvid libkvazaar; do
  if echo "$encoders" | grep -qw "$enc"; then echo "FAIL: verbotener Encoder $enc"; fail=1; fi
done

# Decoders for patent-encumbered formats: Kvertis decodes these through Windows Media Foundation only.
for dec in h264 hevc aac aac_fixed aac_latm mpeg4 msmpeg4v1 msmpeg4v2 msmpeg4v3 wmv1 wmv2 wmv3 vc1 \
           wmav1 wmav2 wmapro wmalossless wmavoice h263 prores dnxhd eac3 dca truehd amrnb amrwb \
           h264_qsv hevc_qsv h264_cuvid hevc_cuvid; do
  if echo "$decoders" | grep -qwE "^ *[VAS][A-Z.]{5} +$dec( |$)"; then echo "FAIL: patentbelasteter Decoder $dec"; fail=1; fi
done

# Network protocols must not be compiled in.
for proto in http https tcp udp tls rtmp rtp srt ftp sftp; do
  if echo "$protocols" | grep -qw "$proto"; then echo "FAIL: Netzwerkprotokoll $proto"; fail=1; fi
done

for enc in h264_mf aac_mf; do
  if ! echo "$encoders" | grep -qw "$enc"; then echo "WARN: Media-Foundation-Encoder $enc fehlt (MP4/M4A dann nicht verfügbar)"; fi
done

if [ $fail -eq 0 ]; then echo "OK: LGPL-Build ohne verbotene Encoder, Decoder und Netzwerkprotokolle"; else exit 1; fi
