# PickmeTurn

## Русский

**PickmeTurn** — графический клиент для Windows на WPF, предназначенный для удобного управления подключением через **Free Turn Proxy + WireGuard**.

Приложение не является самостоятельной реализацией VPN-протокола. PickmeTurn предоставляет графический интерфейс, управляет локальным WireGuard-туннелем и клиентом Free Turn Proxy, а передача трафика выполняется через настроенную инфраструктуру FreeTurn/VK TURN.

### Возможности

- Тёмный интерфейс Windows-приложения на WPF.
- Несколько сохранённых профилей FreeTurn.
- Отдельная VK Call-ссылка для каждого профиля.
- Защита сохранённых данных профилей с помощью Windows DPAPI для текущего пользователя.
- Работа в системном трее.
- Динамический индикатор состояния подключения и фирменный смайлик PickmeTurn.
- Поддержка CAPTCHA через встроенный клиент Free Turn Proxy.
- Встроенные диагностика и просмотр журнала работы PickmeTurn, FreeTurn и WireGuard.
- Проверка обновлений приложения через GitHub с проверкой SHA-256 установщика.
- Корректная очистка WireGuard-туннеля, процессов FreeTurn и временных файлов при отключении и завершении работы.
- Возможность сохранить или удалить профили при удалении приложения.
- Self-contained x64-сборка: конечному пользователю не требуется отдельно устанавливать .NET.

### Требования

- Windows x64.
- Профиль FreeTurn и необходимая VK Call-ссылка.
- Совместимый сервер FreeTurn / WireGuard.

### Хранение данных

Постоянные пользовательские данные хранятся в:

```text
%LOCALAPPDATA%\PickmeTurn
```

Временные распакованные файлы среды выполнения хранятся в:

```text
%LOCALAPPDATA%\PickmeTurn\Runtime
```

Данные профилей защищаются с помощью Windows DPAPI и доступны только текущему пользователю Windows.

### Сторонние компоненты

PickmeTurn распространяется вместе с бинарными компонентами **Free Turn Proxy** и **WireGuard for Windows**. Эти компоненты остаются отдельными сторонними программными продуктами и распространяются на условиях их собственных лицензий.

Подробная информация, тексты лицензий и ссылки на исходные проекты находятся в `THIRD-PARTY-NOTICES.txt` и каталоге `third-party/`.

Free Turn Proxy:

https://github.com/samosvalishe/free-turn-proxy

WireGuard for Windows:

https://github.com/WireGuard/wireguard-windows

### Лицензия

Собственный исходный код PickmeTurn распространяется по лицензии MIT. Полный текст находится в файле `LICENSE`. Сторонние компоненты сохраняют свои соответствующие лицензии.

---

## English

**PickmeTurn** is a Windows WPF graphical client designed to provide a convenient interface for managing a **Free Turn Proxy + WireGuard** connection.

The application is not a standalone VPN protocol implementation. PickmeTurn provides the graphical interface, manages the local WireGuard tunnel and the Free Turn Proxy client, while traffic is carried through the configured FreeTurn/VK TURN infrastructure.

### Features

- Dark Windows-style WPF interface.
- Multiple saved FreeTurn profiles.
- A separate VK Call link for each profile.
- Windows DPAPI protection for saved profile data for the current user.
- System tray integration.
- Dynamic connection status indicator and PickmeTurn mascot.
- CAPTCHA support through the bundled Free Turn Proxy client.
- Built-in diagnostics and log viewing for PickmeTurn, FreeTurn and WireGuard.
- GitHub-based update checking with SHA-256 verification of the installer.
- Proper cleanup of the WireGuard tunnel, FreeTurn processes, and temporary files on disconnect and application exit.
- Uninstaller option to keep or remove saved profiles.
- Self-contained x64 build: the end user does not need to install .NET separately.

### Requirements

- Windows x64.
- A FreeTurn profile and the required VK Call link.
- A compatible Free Turn Proxy / WireGuard server.

### Data storage

Persistent user data is stored under:

```text
%LOCALAPPDATA%\PickmeTurn
```

Temporary extracted runtime files are stored under:

```text
%LOCALAPPDATA%\PickmeTurn\Runtime
```

Profile data is protected with Windows DPAPI and is available only to the current Windows user.

### Third-party components

PickmeTurn is distributed with binary components from **Free Turn Proxy** and **WireGuard for Windows**. These components remain separate third-party software products and are distributed under their respective licenses.

License information, license texts, and links to the upstream projects are provided in `THIRD-PARTY-NOTICES.txt` and the `third-party/` directory.

Free Turn Proxy:

https://github.com/samosvalishe/free-turn-proxy

WireGuard for Windows:

https://github.com/WireGuard/wireguard-windows

### License

PickmeTurn's own source code is released under the MIT License. The full text is available in `LICENSE`. Third-party components retain their respective licenses.
