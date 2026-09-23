# Verifies that an ffmpeg.exe is an LGPL build without forbidden encoders.
param([string]$Ffmpeg = "ffmpeg.exe")
$ErrorActionPreference = "Stop"
$version  = & $Ffmpeg -hide_banner -version 2>&1 | Out-String
$encoders = & $Ffmpeg -hide_banner -encoders 2>&1 | Out-String
$fail = $false
Write-Host ($version -split "`n")[0]
if ($version -match '--enable-gpl')     { Write-Host "FAIL: --enable-gpl";     $fail = $true }
if ($version -match '--enable-nonfree') { Write-Host "FAIL: --enable-nonfree"; $fail = $true }
if ($version -match '--enable-version3') { Write-Host "WARN: --enable-version3 (LGPL 3 statt 2.1), Lizenztext pruefen" }
foreach ($enc in @('libx264','libx265','libfdk_aac','libxvid')) {
  if ($encoders -match "\b$enc\b") { Write-Host "FAIL: verbotener Encoder $enc"; $fail = $true }
}
foreach ($enc in @('h264_mf','aac_mf')) {
  if ($encoders -notmatch "\b$enc\b") { Write-Host "WARN: Media-Foundation-Encoder $enc fehlt" }
}
if ($fail) { exit 1 } else { Write-Host "OK: LGPL-Build ohne verbotene Encoder" }
