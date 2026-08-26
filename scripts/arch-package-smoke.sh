#!/usr/bin/env bash
set -euo pipefail

repo="${1:-/work}"
tag="${2:-v0.1.0-alpha}"
cd "$repo"

pacman -Syu --noconfirm --needed base-devel git dotnet-sdk libx11 libice libsm fontconfig icu zlib xvfb xorg-xwd
useradd -m -s /bin/bash builder 2>/dev/null || true
chown -R builder:builder "$repo"

su builder -c "cd '$repo' && makepkg --config /etc/makepkg.conf -p packaging/arch/PKGBUILD --noconfirm --cleanbuild"
pkg=$(find "$repo" -maxdepth 1 -name 'maxspeedvpn-linux-*.pkg.tar.zst' -print -quit)
test -n "$pkg"
pacman -U --noconfirm "$pkg"

test -x /usr/bin/maxspeedvpn
test -x /opt/maxspeedvpn/v2rayN
desktop-file-validate /usr/share/applications/maxspeedvpn.desktop
pacman -Qkk maxspeedvpn-linux

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
