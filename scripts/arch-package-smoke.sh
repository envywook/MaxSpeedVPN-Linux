#!/usr/bin/env bash
set -euo pipefail
repo="${1:-/work}"
artifacts="${2:-/artifacts}"
build_root=/tmp/maxspeedvpn-standalone
source_archive=/tmp/maxspeedvpn-source.tar.gz
rm -rf "$build_root"
mkdir -p "$build_root" "$artifacts" /tmp/maxspeedvpn-dotnet-home
cp -a "$repo/." "$build_root/"
chown -R nobody:users "$build_root" /tmp/maxspeedvpn-dotnet-home

tar --exclude=.git --exclude=artifacts --exclude='*/bin' --exclude='*/obj' \
  -czf "$source_archive" -C "$repo" .
chown nobody:users "$source_archive"
cd "$build_root/packaging/arch"
cp PKGBUILD PKGBUILD.smoke
# Replace only the application source for an unpublished local snapshot; engine/font assets keep production URLs and checksums.
python3 - "$source_archive" <<'PY'
from pathlib import Path
import sys
p=Path('PKGBUILD.smoke')
s=p.read_text()
s=s.replace('  "maxspeedvpn-${pkgver}.tar.gz::https://github.com/envywook/MaxSpeedVPN-Linux/archive/${_source_commit}.tar.gz"', f'  "maxspeedvpn-local::file://{sys.argv[1]}"')
lines=s.splitlines()
checksum_index=lines.index('sha256sums=(') + 1
lines[checksum_index]="  'SKIP'"
s='\n'.join(lines) + '\n'
s=s.replace("  local source_dir\n  source_dir=$(find \"$srcdir\" -maxdepth 1 -type d -name 'MaxSpeedVPN-Linux-*' -print -quit)\n  [[ -n \"$source_dir\" ]]\n  rm -rf \"$srcdir/app\"\n  mv \"$source_dir\" \"$srcdir/app\"", "  rm -rf \"$srcdir/app\"\n  mkdir \"$srcdir/app\"\n  tar -xzf \"$srcdir/maxspeedvpn-local\" -C \"$srcdir/app\"")
p.write_text(s)
PY
chmod 644 PKGBUILD.smoke
chown nobody:users PKGBUILD.smoke
runuser -u nobody -- env HOME=/tmp/maxspeedvpn-dotnet-home DOTNET_CLI_HOME=/tmp/maxspeedvpn-dotnet-home NUGET_PACKAGES=/tmp/maxspeedvpn-dotnet-home/.nuget/packages DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 makepkg -p PKGBUILD.smoke --noconfirm
pkg=$(find . -maxdepth 1 -name 'maxspeedvpn-linux-*.pkg.tar.zst' -print -quit)
[[ -n "$pkg" ]]
pacman -Qip "$pkg" | grep -q '^Name *: maxspeedvpn-linux$'
bsdtar -tf "$pkg" | grep -q '^\.PKGINFO$'
bsdtar -tf "$pkg" | grep -q '^opt/maxspeedvpn/MaxSpeedVPN.Desktop$'
bsdtar -tf "$pkg" | grep -q '^opt/maxspeedvpn/bin/sing-box$'
bsdtar -tf "$pkg" | grep -q '^usr/bin/maxspeedvpn$'
bsdtar -tf "$pkg" | grep -q '^usr/share/licenses/maxspeedvpn-linux/sing-box-LICENSE$'
bsdtar -tf "$pkg" | grep -q '^usr/share/licenses/maxspeedvpn-linux/Noto-LICENSE$'
install -Dm644 "$pkg" "$artifacts/$(basename "$pkg")"
echo "MAXSPEEDVPN_STANDALONE_ARCH_PACKAGE_OK package=$(basename "$pkg")"
