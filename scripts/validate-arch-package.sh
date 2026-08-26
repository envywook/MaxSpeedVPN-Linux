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
if grep -Eq 'CAP_SYS_ADMIN|auth_admin_keep|flush ruleset' "$unit" "$policy"; then
  echo "unsafe privilege or firewall directive found" >&2
  exit 1
fi
grep -q '^Exec=maxspeedvpn$' "$desktop"
grep -q 'v2rayN-core-bin/raw/00107fb83fabdce90bb402a79ec3d9631f26f16d/v2rayN-linux-64.zip' "$pkgbuild"
grep -q 'd35f5527c6338b376676aa518eaf1852708bc28f8b5fac45306b58c8e2bbe898' "$pkgbuild"
grep -q 'GlobalHotKeys/archive/162d401dfe0140b41d1fa349b9aadb4060e739b1.tar.gz' "$pkgbuild"

echo MAXSPEEDVPN_ARCH_PACKAGE_METADATA_OK
