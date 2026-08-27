#!/usr/bin/env bash
set -euo pipefail
export PATH=/root/.dotnet:$PATH
export DOTNET_ROOT=/root/.dotnet
export DISPLAY=:99
out=${1:-/tmp/maxspeedvpn-standalone.png}
Xvfb :99 -screen 0 1400x900x24 -nolisten tcp >/tmp/maxspeedvpn-xvfb.log 2>&1 &
xvfb_pid=$!
app_pid=
cleanup() {
  if [[ -n "${app_pid:-}" ]]; then kill "$app_pid" 2>/dev/null || true; fi
  kill "$xvfb_pid" 2>/dev/null || true
}
trap cleanup EXIT
for _ in $(seq 1 30); do xdpyinfo -display :99 >/dev/null 2>&1 && break; sleep .1; done
dotnet /root/maxspeedvpn-standalone/src/MaxSpeedVPN.Desktop/bin/Release/net10.0/MaxSpeedVPN.Desktop.dll >/tmp/maxspeedvpn-ui.log 2>&1 &
app_pid=$!
for _ in $(seq 1 50); do
  if ! kill -0 "$app_pid" 2>/dev/null; then cat /tmp/maxspeedvpn-ui.log >&2; exit 1; fi
  window=$(/usr/bin/xwininfo -root -tree -display :99 2>/dev/null | sed -n 's/^[[:space:]]*\(0x[0-9a-f]*\).*MaxSpeedVPN.*/\1/p' | head -1)
  [[ -n "$window" ]] && break
  sleep .1
done
[[ -n "${window:-}" ]]
sleep 2
geometry=$(/usr/bin/xwininfo -display :99 -id "$window" | awk '/Absolute upper-left X:/{x=$4} /Absolute upper-left Y:/{y=$4} /^  Width:/{w=$2} /^  Height:/{h=$2} END{print w"x"h"+"x"+"y}')
[[ -n "$geometry" ]]
import -display :99 -window root -crop "$geometry" +repage "$out"
file "$out"
