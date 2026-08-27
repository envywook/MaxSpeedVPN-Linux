# MaxSpeedVPN Linux

Самостоятельный Arch-first клиент MaxSpeedVPN на Avalonia/.NET. Это clean-room Linux-приложение: оно не использует код, сборки или архитектуру v2rayN.

> **Статус 0.2.0 alpha:** текущий vertical slice поднимает локальный SOCKS/HTTP proxy на `127.0.0.1:10808`. Он пока не изменяет системный proxy, DNS или маршруты и не создаёт TUN-интерфейс.

## Что уже работает

- импорт VLESS Reality TCP URI;
- генерация проверенных конфигураций для sing-box и Xray;
- запуск core отдельным непривилегированным процессом;
- readiness-проверка локального listener перед состоянием «подключено»;
- корректный stop, обработка неожиданного завершения и cleanup временного конфига;
- приватные XDG runtime-каталоги и файлы с правами только для пользователя;
- собственный Avalonia UI без компонентов v2rayN;
- воспроизводимый Arch Linux пакет с bundled sing-box `1.13.19` и локальным Noto Sans.

## Чего пока нет

- TUN/VPN для всего устройства;
- system proxy;
- split routing и региональных пресетов;
- kill switch, DNS/policy-routing rollback;
- привилегированного D-Bus/Polkit helper;
- выбора Xray в UI и bundled Xray binary.

Эти функции не имитируются в интерфейсе и появятся только вместе с узкой, rollback-safe системной интеграцией.

## Установка на Arch Linux

Скачайте файл `maxspeedvpn-linux-0.2.0-1-x86_64.pkg.tar.zst` со страницы [Releases](https://github.com/envywook/MaxSpeedVPN-Linux/releases) и установите:

```bash
sudo pacman -U ./maxspeedvpn-linux-0.2.0-1-x86_64.pkg.tar.zst
maxspeedvpn
```

После импорта профиля направьте приложение в локальный proxy `127.0.0.1:10808`.

## Portable archive

Portable-архив распаковывается без установки:

```bash
tar -xf maxspeedvpn-linux-0.2.0-x86_64.tar.zst
./maxspeedvpn-linux-0.2.0-x86_64/maxspeedvpn
```

## Архитектура

- `MaxSpeedVPN.Core` — профиль, строгий parser поддерживаемого VLESS subset, deterministic sing-box/Xray config и lifecycle внешнего core;
- `MaxSpeedVPN.Desktop` — непривилегированный Avalonia GUI;
- sing-box/Xray — только внешние процессы;
- временный generated config хранится в пользовательском XDG runtime-каталоге и удаляется после остановки.

Будущий `maxspeedvpn-networkd` будет отдельным минимальным root-helper через D-Bus/Polkit. Он не будет принимать arbitrary shell commands, raw nftables scripts, произвольные пути или core JSON.

## Проверка

Для разработки требуется .NET SDK 10:

```bash
dotnet run --project tests/MaxSpeedVPN.Tests/MaxSpeedVPN.Tests.csproj -c Release
dotnet build MaxSpeedVPN.slnx -c Release
```

Перед `v0.2.0-alpha.1` прошли 14/14 тестов, реальные `sing-box check` и `xray run -test`, сборка Arch-пакета, установка через `pacman -U` и запуск установленного GUI под Xvfb.

## Лицензия и бренд

Исходный код этого standalone-клиента распространяется по [GPL-3.0-only](LICENSE). В пакет включаются внешние компоненты под их собственными лицензиями; см. [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Тексты лицензий устанавливаются в `/usr/share/licenses/maxspeedvpn-linux/`.

Название **MaxSpeedVPN**, официальный логотип, домены, каналы обновлений и подписи официальных релизов не предоставляются как часть GPL-лицензии на код. Производная сборка должна ясно обозначать независимое происхождение и не выдавать себя за официальный продукт MaxSpeedVPN; подробности — в [TRADEMARKS.md](TRADEMARKS.md).

## Целевая платформа

Сейчас поддерживается Arch Linux x86_64. Текущий alpha-релиз — честный локальный proxy MVP, а не полноценный system-wide VPN.
