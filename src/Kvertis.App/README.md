# Kvertis.App

WinUI-3-Oberfläche von Kvertis (Windows App SDK 2.5, .NET 10, MVVM mit CommunityToolkit.Mvvm, Single-Project-MSIX).
Die App spricht nur mit `Kvertis.Queue` und den Schnittstellen aus `Kvertis.Engine`; die Verdrahtung steht in
`Services/ServiceRegistration.cs`.

## Bauen (nur Windows)

Voraussetzungen: Windows 10 1809 oder neuer, .NET 10 SDK. Visual Studio ist nicht nötig.

```
dotnet build src/Kvertis.App/Kvertis.App.csproj -p:Platform=x64
dotnet build src/Kvertis.App/Kvertis.App.csproj -p:Platform=ARM64
dotnet build Kvertis.sln -c Release -p:Platform=x64        # wie in der CI (Job app-windows)
```

Unter Linux bricht der Build beim XAML-Compiler ab (`XamlCompiler.exe` ist ein Windows-Programm). Engine, Queue und
Tests bauen dort über `Kvertis.Core.slnf`.

Zum Starten aus Visual Studio das Projekt `Kvertis.App` mit dem Profil „Paket“ wählen; beim ersten Start wird ein
lokales Testzertifikat erzeugt. Zertifikate und Pakete gehören nie ins Repository (`.gitignore`).

## Starten ohne Visual Studio

**Unverpackt (Entwicklerschleife, kein Entwicklermodus nötig):**

```
dotnet build src/Kvertis.App/Kvertis.App.csproj -p:Platform=x64 -p:WindowsPackageType=None -p:OutDir=bin/x64/Debug-unpackaged/
src/Kvertis.App/bin/x64/Debug-unpackaged/Kvertis.exe
```

Voraussetzung ist die installierte Windows-App-Runtime 2.5 (auf Entwicklerrechnern meist vorhanden). Ohne Paket-Identität
liegen Einstellungen, Verlauf und Protokolle unter `%LOCALAPPDATA%Kvertis` (siehe `Services/PackageInfo.cs`); der
Store-Kauf ist in Debug-Builds ohnehin durch `DebugLicenseService` ersetzt.

**Verpackt (MSIX, wie im Store):** Entwicklermodus in den Windows-Einstellungen einschalten, dann

```
dotnet build src/Kvertis.App/Kvertis.App.csproj -p:Platform=x64
Add-AppxPackage -Register src/Kvertis.App/bin/x64/Debug/net10.0-windows10.0.19041.0/AppxManifest.xml
```

Danach steht „Kvertis“ im Startmenü. Entfernen mit `Get-AppxPackage Kvertis | Remove-AppxPackage`.

## FFmpeg

Audio und Video brauchen einen LGPL-Build von `ffmpeg.exe` und `ffprobe.exe` (ADR-002, `docs/02-rechtssicherheit.md` §2):

```
src/Kvertis.App/Tools/ffmpeg/x64/ffmpeg.exe
src/Kvertis.App/Tools/ffmpeg/x64/ffprobe.exe
src/Kvertis.App/Tools/ffmpeg/arm64/ffmpeg.exe
src/Kvertis.App/Tools/ffmpeg/arm64/ffprobe.exe
```

Der Ordner ist git-ignoriert und wird nur ins Paket übernommen, wenn er existiert. Die Engine prüft beim ersten Einsatz,
dass es ein LGPL-Build ohne verbotene Encoder ist, und verweigert sonst die Arbeit. Nutzer können in den Einstellungen
einen eigenen FFmpeg-Ordner angeben (LGPL-Austauschrecht). Ohne FFmpeg funktionieren Bilder weiter; Audio und Video
melden `ToolMissing`.

## Ressourcen und Prüfungen

- Alle sichtbaren Texte stehen in `Strings/de-DE/Resources.resw` und `Strings/en-US/Resources.resw` (gleiche Schlüssel,
  Muster `Screen_Element_Zweck`, `x:Uid`-Schlüssel mit `.Eigenschaft`).
- `python3 tools/compliance/check-resw.py` prüft Schlüssel, `x:Uid`, Fehlercodes (`Error_{Code}_Title/_Body`),
  Automation-Namen und Theme-Ressourcen. Es läuft als Schritt 6 in `tools/compliance/check.sh`.

## Platzhalter

- **Logos** unter `Assets/` (erzeugt mit `tools/assets/generate-placeholder-logos.py` aus der Geometrie von
  `Assets/Logo.svg`): schlichte Platzhalter, werden durch das finale Logo ersetzt.
- **Paket-Identität** in `Package.appxmanifest`: `Name="Kvertis"`, `Publisher="CN=Kvertis"` bis zur Reservierung im
  Partner Center.
- **Store-ID des Add-ons** `StoreLicenseService.KVERTIS_PRO_STORE_ID` (`Services/LicenseService.cs`). In Debug-Builds
  gilt Pro immer als aktiv (`DebugLicenseService`).
- **Gratis-Grenze** `FreemiumPolicy.FreeBatchLimit = 5` (endgültiger Wert offen, `docs/09-roadmap.md`).
- **Dokument-Konverter** sind noch nicht eingehängt (`TODO(wiring)` in `ServiceRegistration.cs`).
