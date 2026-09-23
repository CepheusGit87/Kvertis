#!/usr/bin/env bash
# Kvertis compliance gate. Runs in CI on every push and locally before a merge.
# Fails when:
#   1. a NuGet package in Directory.Packages.props has no row in docs/04-bibliotheken.md
#   2. src/ contains network code
#   3. src/ or docs/ mention forbidden libraries (GPL / non-free codecs)
#   4. app resources, presets or store texts contain a third-party brand name (tools/compliance/brands.txt)
#   5. the app manifest declares a network or broad file system capability
#   6. app resources (.resw) and XAML are inconsistent (tools/compliance/check-resw.py)
set -euo pipefail
cd "$(dirname "$0")/../.."

fail=0
note() { printf '  %s\n' "$*"; }
bad()  { printf 'FAIL: %s\n' "$*"; fail=1; }

echo "[1/6] Paketregister"
while read -r pkg; do
  if ! grep -Fq -- "$pkg" docs/04-bibliotheken.md; then
    bad "Paket '$pkg' fehlt in docs/04-bibliotheken.md"
  fi
done < <(grep -oE 'PackageVersion Include="[^"]+"' Directory.Packages.props | sed -E 's/.*="([^"]+)"/\1/')

echo "[2/6] Netzwerkcode"
if grep -rnE --include='*.cs' --include='*.xaml' \
     'HttpClient|WebRequest|WebClient|System\.Net\.Sockets|Windows\.Networking|HttpWebRequest|Socket\(' src/ ; then
  bad "Netzwerkcode in src/ gefunden"
fi

echo "[3/6] Verbotene Bibliotheken"
# Strict on purpose: the engine's own build checker assembles these strings from parts.
if grep -rniE --include='*.cs' --include='*.csproj' --include='*.props' --include='*.xaml' \
     'libx264|libx265|libfdk[_-]?aac|libxvid|enable-gpl|enable-nonfree|QuestPDF|FluentAssertions' src/ tests/ ; then
  bad "Verbotene Bibliothek oder GPL-Option referenziert"
fi

echo "[4/6] Markennamen"
if [ -f tools/compliance/brands.txt ]; then
  pattern=$(grep -vE '^\s*(#|$)' tools/compliance/brands.txt | paste -sd'|' -)
  if [ -n "$pattern" ]; then
    targets=()
    for p in src/Kvertis.App/Strings src/Kvertis.App/Views src/Kvertis.App/ViewModels src/Kvertis.Engine/Conversion/Presets.cs docs/07-store.md; do
      [ -e "$p" ] && targets+=("$p")
    done
    if [ ${#targets[@]} -gt 0 ] && grep -rniwE "$pattern" "${targets[@]}"; then
      bad "Fremder Markenname in UI-Texten, Presets oder Store-Text"
    fi
  fi
fi

echo "[5/6] Manifest-Berechtigungen"
for m in $(find src -name 'Package.appxmanifest' 2>/dev/null); do
  if grep -nE 'internetClient|internetClientServer|privateNetworkClientServer|broadFileSystemAccess|picturesLibrary|videosLibrary|musicLibrary|documentsLibrary|removableStorage' "$m"; then
    bad "Unerlaubte Berechtigung in $m"
  fi
done

echo "[6/6] App-Ressourcen (DE/EN, x:Uid, Fehlercodes)"
if [ -d src/Kvertis.App ]; then
  if ! python3 tools/compliance/check-resw.py; then
    bad "Ressourcen oder XAML der App inkonsistent"
  fi
fi

if [ $fail -ne 0 ]; then
  echo "Compliance-Prüfung fehlgeschlagen."
  exit 1
fi
echo "Compliance-Prüfung bestanden."
