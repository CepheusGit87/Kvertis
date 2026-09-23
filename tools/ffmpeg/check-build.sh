#!/usr/bin/env bash
# Verifies that an ffmpeg binary is an LGPL build without forbidden encoders.
set -euo pipefail
ffmpeg="${1:-ffmpeg}"
version="$("$ffmpeg" -hide_banner -version)"
encoders="$("$ffmpeg" -hide_banner -encoders 2>/dev/null)"
fail=0
echo "$version" | head -1
if echo "$version" | grep -q -- '--enable-gpl';     then echo "FAIL: --enable-gpl";     fail=1; fi
if echo "$version" | grep -q -- '--enable-nonfree'; then echo "FAIL: --enable-nonfree"; fail=1; fi
if echo "$version" | grep -q -- '--enable-version3'; then echo "WARN: --enable-version3 (LGPL 3 statt 2.1), Lizenztext prüfen"; fi
for enc in libx264 libx265 libfdk_aac libxvid; do
  if echo "$encoders" | grep -qw "$enc"; then echo "FAIL: verbotener Encoder $enc"; fail=1; fi
done
for enc in h264_mf aac_mf; do
  if ! echo "$encoders" | grep -qw "$enc"; then echo "WARN: Media-Foundation-Encoder $enc fehlt (MP4/M4A dann nicht verfügbar)"; fi
done
[ $fail -eq 0 ] && echo "OK: LGPL-Build ohne verbotene Encoder" || exit 1
