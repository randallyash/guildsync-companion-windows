#!/usr/bin/env bash
# Build the Linux flatpak bundle: GuildSyncCompanion-x64.flatpak.
#
# The app is a self-contained .NET publish (same as the PKGBUILD builds)
# wrapped in an org.freedesktop.Platform 24.08 sandbox. The bundle is a
# single release-asset file: installs with a double-click in Discover or
# `flatpak install ./GuildSyncCompanion-x64.flatpak`. Bundle installs have
# no update channel; repo installs (build-flatpak-repo.sh) auto-update.
#
# Output: dist/GuildSyncCompanion-x64.flatpak
set -euo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
cd "$root"

if [[ -z "${DOTNET_ROOT:-}" && -x "${HOME}/.local/share/mise/dotnet-root/dotnet" ]]; then
  export DOTNET_ROOT="${HOME}/.local/share/mise/dotnet-root/dotnet"
  export PATH="${DOTNET_ROOT}:${PATH}"
fi

version=$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' src/GuildSync.Companion/GuildSync.Companion.csproj | head -1)
if [[ -z "$version" ]]; then
  echo "Could not read Version from the companion project." >&2
  exit 1
fi

appid="co.twilighttavern.Companion"
runtime="org.freedesktop.Platform//24.08"
sdk="org.freedesktop.Sdk//24.08"
work="${GSC_FLATPAK_WORK:-/tmp/opencode/gsc-flatpak}"
out="dist"
mkdir -p "$work" "$out"

echo "==> dotnet publish (self-contained linux-x64)"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet publish src/GuildSync.Companion -c Release -r linux-x64 --self-contained true \
  -p:DebugType=none -p:DebugSymbols=false \
  -o "$work/publish"

echo "==> flatpak build-init"
rm -rf "$work/appdir"
flatpak build-init "$work/appdir" "$appid" "$sdk" "$runtime"

echo "==> stage files"
appdir="$work/appdir"
mkdir -p "$appdir/files/bin" \
         "$appdir/files/share/applications" \
         "$appdir/files/share/metainfo" \
         "$appdir/files/share/icons/hicolor/512x512/apps"
cp -a "$work/publish"/. "$appdir/files/bin/"
chmod 755 "$appdir/files/bin/GuildSyncCompanion"
cp packaging/flatpak/co.twilighttavern.Companion.desktop \
   "$appdir/files/share/applications/"
cp packaging/flatpak/co.twilighttavern.Companion.metainfo.xml \
   "$appdir/files/share/metainfo/"
cp src/GuildSync.Companion/Assets/crest.png \
   "$appdir/files/share/icons/hicolor/512x512/apps/co.twilighttavern.Companion.png"

echo "==> flatpak build-finish"
flatpak build-finish "$appdir" \
  --filesystem=home \
  --share=network \
  --socket=fallback-x11 \
  --socket=wayland \
  --device=dri \
  --command=GuildSyncCompanion

echo "==> flatpak build-export + build-bundle"
rm -rf "$work/repo"
flatpak build-export "$work/repo" "$appdir"
flatpak build-bundle \
  ${GSC_RUNTIME_REPO:+--runtime-repo="$GSC_RUNTIME_REPO"} \
  "$work/repo" "$out/GuildSyncCompanion-x64.flatpak" "$appid"

echo "==> done: $out/GuildSyncCompanion-x64.flatpak (v$version)"
