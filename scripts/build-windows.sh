#!/usr/bin/env bash
# Publish the self-contained Windows app and pack GuildSyncCompanion-Setup.exe.
set -euo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
cd "$root"

if [[ -z "${DOTNET_ROOT:-}" && -x "${HOME}/.local/share/mise/dotnet-root/dotnet" ]]; then
  export DOTNET_ROOT="${HOME}/.local/share/mise/dotnet-root"
  export PATH="${DOTNET_ROOT}:${PATH}"
fi

dotnet publish src/GuildSync.Companion -c Release -r win-x64 --self-contained true \
  -p:DebugType=none -p:DebugSymbols=false \
  -o dist/win-x64

version=$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' src/GuildSync.Companion/GuildSync.Companion.csproj | head -1)
if [[ -z "$version" ]]; then
  echo "Could not read Version from the companion project." >&2
  exit 1
fi

if command -v makensis >/dev/null 2>&1; then
  makensis_bin=$(command -v makensis)
elif [[ -x "${HOME}/.local/opt/nsis-root/usr/bin/makensis" ]]; then
  makensis_bin="${HOME}/.local/opt/nsis-root/usr/bin/makensis"
  export NSISDIR="${HOME}/.local/opt/nsis-root/usr/share/nsis"
else
  echo "makensis was not found. Install NSIS (Debian/Ubuntu: sudo apt install nsis)." >&2
  exit 1
fi

mkdir -p dist
"$makensis_bin" \
  -DAPP_VERSION="$version" \
  -DOUTFILE="${root}/dist/GuildSyncCompanion-Setup.exe" \
  -DPAYLOAD="${root}/dist/win-x64" \
  -DICON="${root}/src/GuildSync.Companion/Assets/crest.ico" \
  "${root}/scripts/installer.nsi"

echo "Built ${root}/dist/GuildSyncCompanion-Setup.exe"
