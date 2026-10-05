#!/usr/bin/env bash
# Build/publish the auto-update flatpak repo for GuildSync Companion.
#
# Builds the app (same steps as build-flatpak.sh), exports it into a real
# OSTree repo with GPG-signed commits, imports the freedesktop runtime +
# Mesa GL extension from flathub so the repo is self-contained (users need
# NO flathub), and writes twilighttavern.flatpakrepo.
#
# The GPG signing key lives in a homedir NEXT TO the repo and must be kept
# across releases (same key signs every update, or clients refuse pulls).
#
# Output: $GSC_REPO_OUT/repo/ (publishable tree) + twilighttavern.flatpakrepo
set -euo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
cd "$root"

appid="co.twilighttavern.Companion"
runtime_ref="runtime/org.freedesktop.Platform/x86_64/24.08"
gl_ref="runtime/org.freedesktop.Platform.GL.default/x86_64/24.08"
flathub="${GSC_FLATHUB_URL:-https://dl.flathub.org/repo/}"
repo_out="${GSC_REPO_OUT:-/tmp/opencode/flatpak-repo-publish}"
# Signing key: durable + private, gitignored inside the wowforever workspace.
# The SAME key must sign every release or installed clients refuse updates.
keys_dir="${GSC_KEYS_DIR:-$HOME/Mounts/nas/nas/opencode/wowforever/flatpak-repo-key}"
work="${GSC_FLATPAK_WORK:-/tmp/opencode/gsc-flatpak}"

mkdir -p "$repo_out" "$keys_dir" "$work"

gpg_home="$keys_dir/gnupg"
key_id_file="$keys_dir/keyid"
if [[ ! -f "$key_id_file" ]]; then
  echo "==> generating repo signing key (kept in $gpg_home)"
  mkdir -p "$gpg_home" && chmod 700 "$gpg_home"
  gpg --batch --homedir "$gpg_home" --passphrase '' \
      --quick-gen-key "Twilight Tavern Flatpak" default default never
  gpg --homedir "$gpg_home" --list-keys --with-colons | awk -F: '/^pub/ {print $5; exit}' \
      > "$key_id_file"
fi
key_id=$(cat "$key_id_file")
echo "==> signing with key $key_id"

# --- app (reuses the bundle script's staging) ---------------------------------
echo "==> building app"
"$root/scripts/build-flatpak.sh" >/dev/null

echo "==> exporting app into the publish repo"
rm -rf "$repo_out/repo"
# Branch "stable" matches DefaultBranch in the .flatpakrepo.
# TODO: generate the appstream catalog (files/share/app-info) so Discover and
# GNOME Software render the app page nicely. appstreamcli compose only works
# inside the SDK sandbox (it resolves /usr), so this needs a
# `flatpak build $appdir appstreamcli compose ...` pass. Installs and
# `flatpak update` work fine without it.
flatpak build-export --gpg-homedir "$gpg_home" --gpg-sign "$key_id" \
  "$repo_out/repo" "$work/appdir" stable

echo "==> importing runtime + GL extension from flathub (self-contained repo)"
# Deployed refs live under the deploy/ prefix in local flatpak installs;
# the published repo keeps the plain runtime ref names.
system_repo="${GSC_SYSTEM_REPO:-/var/lib/flatpak/repo}"
for ref in "$runtime_ref" "$gl_ref"; do
  name=$(echo "$ref" | awk -F/ '{print $2}')
  if ! flatpak --system list --runtime --columns application,branch | grep -q "^$name\s"; then
    echo "    pulling $ref from flathub"
    flatpak --system install -y --noninteractive flathub "$ref" >/dev/null
  fi
  flatpak build-commit-from --gpg-homedir "$gpg_home" --gpg-sign "$key_id" \
    --src-repo="$system_repo" --src-ref="deploy/$ref" --force \
    "$repo_out/repo" "$ref"
done

echo "==> flatpak build-update-repo"
flatpak build-update-repo --gpg-homedir "$gpg_home" --gpg-sign "$key_id" \
  "$repo_out/repo"

echo "==> writing twilighttavern.flatpakrepo"
{
  echo "[Flatpak Repo]"
  echo "Version=1"
  echo "Url=https://twilighttavern.co/repo/repo"
  echo "Title=Twilight Tavern"
  echo "Comment=GuildSync Companion and friends from twilighttavern.co"
  echo "DefaultBranch=stable"
  echo "GPGKey=$(gpg --homedir "$gpg_home" --armor --export "$key_id" | base64 -w0)"
} > "$repo_out/twilighttavern.flatpakrepo"

echo "==> done: $repo_out (repo/ + twilighttavern.flatpakrepo)"
