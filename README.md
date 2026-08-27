# MaxSpeedVPN Linux

Standalone Arch-first MaxSpeedVPN client written from scratch with .NET and Avalonia.

## Architecture

- `MaxSpeedVPN.Core` owns profiles, deterministic sing-box/Xray config generation, connection state, and external-process lifecycle.
- `MaxSpeedVPN.Desktop` is a non-root Avalonia GUI with MaxSpeedVPN's own visual language.
- `sing-box` is the pinned bundled engine used by the current GUI; Xray has its own config adapter/runtime and can be selected when packaged. No v2rayN source, services, storage, view models, or UI are used.
- Runtime data lives in `$XDG_DATA_HOME/maxspeedvpn` (fallback `~/.local/share/maxspeedvpn`). Runtime directories are `0700`, configs are created as `0600`, and deleted on stop/error.

Current vertical slice imports one VLESS Reality TCP URI for the current process only, starts a localhost mixed proxy (`127.0.0.1:10808`), verifies listener readiness, observes unexpected core exit, and performs deterministic cleanup. Profiles are intentionally not persisted until Secret Service/keyring storage is implemented.

TUN, system proxy integration, split tunneling, and RF presets belong behind a narrow privileged helper and are not claimed as complete in this MVP.

## Build and verify

```bash
export PATH=/root/.dotnet:$PATH DOTNET_ROOT=/root/.dotnet
dotnet restore MaxSpeedVPN.slnx
dotnet run --project tests/MaxSpeedVPN.Tests/MaxSpeedVPN.Tests.csproj -c Release
dotnet build MaxSpeedVPN.slnx -c Release --no-restore
bash scripts/capture-ui.sh /tmp/maxspeedvpn-standalone.png
```
