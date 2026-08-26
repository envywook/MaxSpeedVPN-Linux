#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
pkgbuild="$root/packaging/arch/PKGBUILD"
desktop="$root/packaging/arch/maxspeedvpn.desktop"
unit="$root/packaging/systemd/maxspeedvpn-netd.service"
policy="$root/packaging/polkit/com.maxspeedvpn.Netd1.policy"

[[ -f "$pkgbuild" && -f "$desktop" && -f "$unit" && -f "$policy" ]]

if command -v shellcheck >/dev/null; then
  shellcheck "$0"
fi
if command -v desktop-file-validate >/dev/null; then
  desktop-file-validate "$desktop"
fi
if command -v systemd-analyze >/dev/null; then
  verify_output="$(systemd-analyze verify "$unit" 2>&1 || true)"
  while IFS= read -r line; do
    [[ -z "$line" ]] && continue
    case "$line" in
      *"Command /usr/lib/maxspeedvpn/maxspeedvpn-netd is not executable: No such file or directory"*) ;;
      *"snapd.service"*"RestartMode"*) ;;
      *) printf '%s\n' "$line" >&2; exit 1 ;;
    esac
  done <<< "$verify_output"
fi
python3 - "$policy" <<'PY'
import sys
import xml.etree.ElementTree as ET
ET.parse(sys.argv[1])
PY

# These are security invariants, not style checks.
grep -q '^User=maxspeedvpn$' "$unit"
grep -q '^NoNewPrivileges=yes$' "$unit"
grep -q '^CapabilityBoundingSet=CAP_NET_ADMIN$' "$unit"
! grep -Eq 'CAP_SYS_ADMIN|auth_admin_keep|flush ruleset' "$unit" "$policy"
grep -q '^Exec=maxspeedvpn$' "$desktop"

echo MAXSPEEDVPN_ARCH_PACKAGE_METADATA_OK
