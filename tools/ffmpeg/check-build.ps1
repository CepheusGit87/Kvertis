# Verifies that an ffmpeg.exe is the Kvertis LGPL build: no GPL/nonfree, no forbidden encoders,
# no patent-encumbered decoders, no network protocols. Exits 1 on any violation.
param([string]$Ffmpeg = "ffmpeg.exe")
$ErrorActionPreference = "Stop"
$version   = & $Ffmpeg -hide_banner -version   2>&1 | Out-String
$encoders  = & $Ffmpeg -hide_banner -encoders  2>&1 | Out-String
$decoders  = & $Ffmpeg -hide_banner -decoders  2>&1 | Out-String
$protocols = & $Ffmpeg -hide_banner -protocols 2>&1 | Out-String
$fail = $false
Write-Host ($version -split "`n")[0]

if ($version -match '--enable-gpl')      { Write-Host "FAIL: --enable-gpl";      $fail = $true }
if ($version -match '--enable-nonfree')  { Write-Host "FAIL: --enable-nonfree";  $fail = $true }
if ($version -match '--enable-version3') { Write-Host "WARN: --enable-version3 (LGPL 3 statt 2.1), Lizenztext pruefen" }
if ($version -notmatch '--disable-network') { Write-Host "WARN: Build nicht mit --disable-network konfiguriert" }

foreach ($enc in @('libx264','libx265','libfdk_aac','libxvid','libkvazaar')) {
  if ($encoders -match "\b$enc\b") { Write-Host "FAIL: verbotener Encoder $enc"; $fail = $true }
}
foreach ($dec in @('h264','hevc','aac','aac_fixed','aac_latm','mpeg4','msmpeg4v1','msmpeg4v2','msmpeg4v3','wmv1','wmv2','wmv3','vc1',
                   'wmav1','wmav2','wmapro','wmalossless','wmavoice','h263','prores','dnxhd','eac3','dca','truehd','amrnb','amrwb',
                   'h264_qsv','hevc_qsv','h264_cuvid','hevc_cuvid')) {
  if ($decoders -match "(?m)^\s*[VAS][A-Z.]{5}\s+$dec(\s|$)") { Write-Host "FAIL: patentbelasteter Decoder $dec"; $fail = $true }
}
foreach ($proto in @('http','https','tcp','udp','tls','rtmp','rtp','srt','ftp','sftp')) {
  if ($protocols -match "\b$proto\b") { Write-Host "FAIL: Netzwerkprotokoll $proto"; $fail = $true }
}
foreach ($enc in @('h264_mf','aac_mf')) {
  if ($encoders -notmatch "\b$enc\b") { Write-Host "WARN: Media-Foundation-Encoder $enc fehlt" }
}
if ($fail) { exit 1 } else { Write-Host "OK: LGPL-Build ohne verbotene Encoder, Decoder und Netzwerkprotokolle" }
