#!/usr/bin/env bash
set -euo pipefail

repo="${1:-/work}"
tag="${2:-v0.1.0-alpha}"
artifact_dir="${3:-}"
cd "$repo"

pacman -Syu --noconfirm --needed base-devel git dotnet-sdk libx11 libice libsm fontconfig icu zlib xorg-server-xvfb xorg-xwd desktop-file-utils
useradd -m -s /bin/bash builder 2>/dev/null || true

build_repo="$repo"
if [[ ! -w "$repo" ]]; then
  build_repo=/tmp/MaxSpeedVPN-Linux
  rm -rf "$build_repo"
  cp -a "$repo" "$build_repo"
fi
chown -R builder:builder "$build_repo"

cp "$build_repo/packaging/arch/PKGBUILD" "$build_repo/PKGBUILD"
chown builder:builder "$build_repo/PKGBUILD"
su builder -c "cd '$build_repo' && makepkg --config /etc/makepkg.conf --noconfirm --cleanbuild"
pkg=$(find "$build_repo" -maxdepth 1 -name 'maxspeedvpn-linux-*.pkg.tar.zst' -print -quit)
test -n "$pkg"
if [[ -n "$artifact_dir" ]]; then
  mkdir -p "$artifact_dir"
  cp "$pkg" "$artifact_dir/"
fi
pacman -U --noconfirm "$pkg"

test -x /usr/bin/maxspeedvpn
test -x /opt/maxspeedvpn/v2rayN
test -x /opt/maxspeedvpn/bin/xray/xray
test -x /opt/maxspeedvpn/bin/sing_box/sing-box
desktop-file-validate /usr/share/applications/maxspeedvpn.desktop
verify_output="$(pacman -Qkk maxspeedvpn-linux 2>&1 || true)"
printf '%s\n' "$verify_output"
if grep -Eq '[1-9][0-9]* altered files|[1-9][0-9]* missing files' <<< "$verify_output"; then
  exit 1
fi

rm -rf /tmp/maxspeedvpn-smoke
mkdir -p /tmp/maxspeedvpn-smoke/home /tmp/maxspeedvpn-smoke/runtime
chown -R builder:builder /tmp/maxspeedvpn-smoke
set +e
su builder -c "HOME=/tmp/maxspeedvpn-smoke/home XDG_CONFIG_HOME=/tmp/maxspeedvpn-smoke/home/.config XDG_DATA_HOME=/tmp/maxspeedvpn-smoke/home/.local/share XDG_CACHE_HOME=/tmp/maxspeedvpn-smoke/home/.cache XDG_RUNTIME_DIR=/tmp/maxspeedvpn-smoke/runtime timeout 18s xvfb-run -a -s '-screen 0 1600x1000x24' /usr/bin/maxspeedvpn" >/tmp/maxspeedvpn-smoke/app.log 2>&1
rc=$?
set -e
if [[ $rc -ne 0 && $rc -ne 124 ]]; then
  cat /tmp/maxspeedvpn-smoke/app.log >&2
  exit "$rc"
fi
if grep -Eq 'Unhandled exception|DllNotFoundException|TypeInitializationException' /tmp/maxspeedvpn-smoke/app.log; then
  cat /tmp/maxspeedvpn-smoke/app.log >&2
  exit 1
fi

echo "MAXSPEEDVPN_ARCH_PACKAGE_SMOKE_OK package=$(basename "$pkg") launcher_rc=$rc tag=$tag"
