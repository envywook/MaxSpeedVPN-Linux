# MaxSpeedVPN Linux

Самостоятельный Arch-first клиент MaxSpeedVPN на Avalonia/.NET. Это clean-room Linux-приложение: оно не использует код, сборки или архитектуру v2rayN.

> **Статус 0.3.0 alpha:** стабильный режим — локальный SOCKS/HTTP proxy на `127.0.0.1:10808`. TUN boundary и rollback уже покрыты тестами, но system-wide TUN намеренно не включён в пакет до rooted end-to-end проверки Polkit/D-Bus helper без утечек маршрутов и DNS.

## Что работает

- импорт и сохранение профилей **VLESS Reality**, **NaiveProxy** и **Mieru** с настоящим именем и протоколом — без ярлыка `Custom`;
- общий TCP ping всех серверов и live ping каждые 5 секунд, пока приложение открыто;
- тёмный полупрозрачный Avalonia UI, официальный логотип приложения и tray icon;
- VLESS и NaiveProxy конфиги проходят реальный `sing-box 1.13.19 check`;
- Mieru simple links парсятся как Mieru, а пакет содержит нативный `mieru 3.36.0`; его запуск остаётся gated до изолированного lifecycle smoke;
- запуск core отдельным непривилегированным процессом, readiness local listener, stop и cleanup;
- приватное XDG-хранилище профилей и runtime-файлов с правами только пользователя;
- bundled sing-box `1.13.19`, `libcronet.so` для Naive outbound и Mieru `3.36.0`.

## Ограничения alpha

- TUN для всего устройства ещё не включён;
- system proxy, split routing, kill switch и DNS policy ещё не включены;
- Mieru import/ping доступен, но native connect gated до process-isolation smoke;
- Xray остаётся внешним adapter без bundled binary и выбора в UI.

Мы не рисуем фиктивные переключатели: privileged networking появится только после реального rooted E2E с rollback маршрутов, nftables и DNS.

## Установка на Arch Linux

Скачайте `maxspeedvpn-linux-0.3.0-1-x86_64.pkg.tar.zst` со страницы [Releases](https://github.com/envywook/MaxSpeedVPN-Linux/releases):

```bash
sudo pacman -U ./maxspeedvpn-linux-0.3.0-1-x86_64.pkg.tar.zst
maxspeedvpn
```

После выбора VLESS или NaiveProxy профиль поднимает локальный proxy `127.0.0.1:10808`.

## Архитектура

- `MaxSpeedVPN.Core` — protocol-aware profile store, parser, ping monitor, sing-box/Xray adapters и lifecycle core;
- `MaxSpeedVPN.Desktop` — непривилегированный Avalonia GUI + tray lifecycle;
- sing-box/Mieru/Xray — отдельные процессы, без выполнения произвольных root-команд;
- `TunRequest`/`TunTransaction` — фиксированный typed contract и обратный rollback partial failure;
- `packaging/{dbus,polkit,systemd}` — подготовленная узкая boundary для будущего `maxspeedvpn-networkd`, не устанавливаемая до rooted gate.

Helper не принимает shell commands, raw nft scripts, произвольные пути или core JSON. Polkit требует отдельную административную авторизацию на настройку сессии и не кэширует её для следующих сессий.

## Проверка

```bash
dotnet run --project tests/MaxSpeedVPN.Tests/MaxSpeedVPN.Tests.csproj -c Release
dotnet build src/MaxSpeedVPN.Desktop/MaxSpeedVPN.Desktop.csproj -c Release -r linux-x64
```

0.3.0 alpha имеет 25/25 core tests, real-engine sing-box/Xray validation, profile-permission tests, live-ping cancellation tests и TUN rollback tests. Релиз публикуется только после package/install/runtime smoke и GitHub asset read-back.

## Лицензия и бренд

Исходный код распространяется по [GPL-3.0-only](LICENSE). Bundled компоненты сохраняют собственные лицензии; см. [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Политика бренда — [TRADEMARKS.md](TRADEMARKS.md).

## Целевая платформа

Arch Linux x86_64. 0.3.0 alpha — multi-protocol local-proxy client, не готовый system-wide VPN.