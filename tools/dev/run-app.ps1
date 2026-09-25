# Developer helper: starts the unpackaged Debug build and moves its window to a secondary monitor,
# so test runs do not land on the primary screen. Never shipped with the app.
# Usage: powershell -File tools/dev/run-app.ps1 [-Monitor 1] [-StageFiles "a.png;b.jpg"] [-Left 40] [-Top 40]
param(
    [int]$Monitor = 1,            # 1 = first non-primary monitor, 2 = second, ...
    [string]$StageFiles = "",     # KVERTIS_STAGE_FILES (Debug builds only)
    [int]$Left = 40,
    [int]$Top = 40,
    [int]$Width = 1280,
    [int]$Height = 860
)
$exe = Join-Path $PSScriptRoot "..\..\src\Kvertis.App\bin\x64\Debug-unpackaged\Kvertis.exe"
if (-not (Test-Path $exe)) { throw "Build fehlt: $exe" }
if ($StageFiles) { $env:KVERTIS_STAGE_FILES = $StageFiles }
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public class KvWin { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool repaint); }'
[KvWin]::SetProcessDPIAware() | Out-Null
$secondary = [System.Windows.Forms.Screen]::AllScreens | Where-Object { -not $_.Primary } | Sort-Object { $_.Bounds.X }
$target = if ($secondary.Count -ge $Monitor) { $secondary[$Monitor - 1] } else { [System.Windows.Forms.Screen]::PrimaryScreen }
$p = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
$deadline = (Get-Date).AddSeconds(15)
while ((Get-Date) -lt $deadline) { $p.Refresh(); if ($p.MainWindowHandle -ne 0) { break }; Start-Sleep -Milliseconds 200 }
if ($p.MainWindowHandle -eq 0) { throw "Kein Fenster nach 15 s (Prozess beendet: $($p.HasExited))" }
$x = $target.Bounds.X + $Left; $y = $target.Bounds.Y + $Top
[KvWin]::MoveWindow($p.MainWindowHandle, $x, $y, $Width, $Height, $true) | Out-Null
"pid=$($p.Id) hwnd=$($p.MainWindowHandle) monitor=$($target.DeviceName) at $x,$y"
