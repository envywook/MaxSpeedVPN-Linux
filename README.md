# MaxSpeedVPN Linux

Нативный клиент MaxSpeedVPN для Linux на базе [v2rayN](https://github.com/2dust/v2rayN), с приоритетной поддержкой Arch Linux.

## Статус

Ранняя разработка. Первый поддерживаемый target — Arch Linux x86_64, KDE Wayland/X11.

## Основные отличия

- Xray и sing-box в одном клиенте;
- журнал core и системных событий прямо на главном экране;
- региональные пресеты маршрутизации для пользователей из РФ;
- TUN, system proxy, split DNS и проектируемый fail-closed kill switch;
- тёмный интерфейс Avalonia с регулируемой прозрачностью и непрозрачным fallback;
- диагностика DNS, маршрутов, core и geo-data без вывода секретов;
- безопасное восстановление сети после ошибки или аварийного завершения.

Технические решения и критерии выпуска: [docs/MAXSPEEDVPN_LINUX_SPEC.md](docs/MAXSPEEDVPN_LINUX_SPEC.md).

## Сборка для разработки

Требуется .NET SDK 10.

```bash
git submodule update --init --recursive
cd v2rayN
dotnet restore v2rayN.slnx
dotnet build v2rayN.Desktop/v2rayN.Desktop.csproj
```

## Происхождение и лицензия

Этот репозиторий является производной работой от v2rayN и распространяется по GPL-3.0. Сохранены исходные уведомления об авторских правах. Подробности: [LICENSE](LICENSE), [NOTICE](NOTICE) и [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

Права GPL на код не предоставляют право выдавать производную сборку за официальный продукт MaxSpeedVPN. Название, логотип, официальные домены, каналы обновлений и подписи релизов регулируются отдельно: [TRADEMARKS.md](TRADEMARKS.md).

## Upstream

```bash
git fetch upstream
git rebase upstream/master
```

Проект находится на ранней стадии и пока не предназначен для защиты реального трафика.
