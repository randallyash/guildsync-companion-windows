#!/usr/bin/env bash
# Publish self-contained Mac apps and pack each one as a disk image.
# Opening the image shows GuildSync Companion and an Applications shortcut.
set -euo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
cd "$root"

if [[ -z "${DOTNET_ROOT:-}" && -x "${HOME}/.local/share/mise/dotnet-root/dotnet" ]]; then
  export DOTNET_ROOT="${HOME}/.local/share/mise/dotnet-root"
  export PATH="${DOTNET_ROOT}:${PATH}"
fi

version=$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' src/GuildSync.Companion/GuildSync.Companion.csproj | head -1)
if [[ -z "$version" ]]; then
  echo "Could not read Version from the companion project." >&2
  exit 1
fi

dmg_tool="${GSC_DMG_TOOL:-${HOME}/.local/bin/dmg}"

# genisoimage writes the Apple disk, libdmg-hfsplus turns it into a .dmg Finder opens.
write_dmg() {
  local stage=$1
  local dmg=$2
  if [[ ! -x "$dmg_tool" ]]; then
    echo "Need $dmg_tool (libdmg-hfsplus) to write a disk image Finder can open." >&2
    exit 1
  fi
  if ! command -v genisoimage >/dev/null 2>&1; then
    echo "Need genisoimage from cdrtools." >&2
    exit 1
  fi

  ln -sfn /Applications "$stage/Applications"
  local iso
  iso=$(mktemp /tmp/gsc-mac.XXXXXX.iso)
  genisoimage -quiet -D -V "GuildSync Apple Silicon" -no-pad -r -apple -o "$iso" "$stage"
  rm -f "$dmg"
  "$dmg_tool" "$iso" "$dmg"
  rm -f "$iso"
}

pack() {
  local rid=$1
  local dmg_name=$2
  local publish="$root/dist/$rid"
  dotnet publish src/GuildSync.Companion -c Release -r "$rid" --self-contained true \
    -p:DebugType=none -p:DebugSymbols=false \
    -o "$publish"
  chmod 755 "$publish/GuildSyncCompanion"

  local stage
  stage=$(mktemp -d)
  local app="$stage/GuildSync Companion.app"
  mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
  cp -a "$publish"/. "$app/Contents/MacOS/"
  chmod -R a+rX "$app"
  cp -f "$root/src/GuildSync.Companion/Assets/crest.png" "$app/Contents/Resources/crest.png"
  # CFBundleIconFile "crest" loads crest.icns. ImageMagick writes a PNG under that name.
  python3 - "$root/src/GuildSync.Companion/Assets/crest.png" "$app/Contents/Resources/crest.icns" << 'PY'
import struct, sys
from io import BytesIO
from PIL import Image

src, dest = sys.argv[1], sys.argv[2]
image = Image.open(src).convert("RGBA")
kinds = {16: b"icp4", 32: b"icp5", 64: b"icp6", 128: b"ic07", 256: b"ic08", 512: b"ic09"}
chunks = []
for size, kind in kinds.items():
    buf = BytesIO()
    image.resize((size, size), Image.Resampling.LANCZOS).save(buf, format="PNG")
    data = buf.getvalue()
    chunks.append(kind + struct.pack(">I", len(data) + 8) + data)
body = b"".join(chunks)
blob = b"icns" + struct.pack(">I", len(body) + 8) + body
open(dest, "wb").write(blob)
PY
  printf 'APPL????' > "$app/Contents/PkgInfo"
  cat > "$app/Contents/Info.plist" << EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>GuildSync Companion</string>
<key>CFBundleDisplayName</key><string>GuildSync Companion</string>
<key>CFBundleIdentifier</key><string>co.twilighttavern.guildsync-companion</string>
<key>CFBundleVersion</key><string>${version}</string>
<key>CFBundleShortVersionString</key><string>${version}</string>
<key>CFBundleExecutable</key><string>GuildSyncCompanion</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleIconFile</key><string>crest</string>
<key>LSMinimumSystemVersion</key><string>12.0</string>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
EOF
  # Apple silicon refuses to start a Mach-O with no signature at all.
  # This is an ad-hoc signature, not an Apple Developer ID.
  local signer="${GSC_RCODESIGN:-${HOME}/.local/bin/rcodesign}"
  if [[ ! -x "$signer" ]]; then
    echo "Need $signer so Apple silicon will launch the app." >&2
    exit 1
  fi
  "$signer" sign "$app"
  local dmg="$root/dist/$dmg_name"
  write_dmg "$stage" "$dmg"
  rm -rf "$stage"
  echo "Built $dmg"
}

mkdir -p "$root/dist"
pack osx-arm64 GuildSyncCompanion-AppleSilicon.dmg
