# PickmeTurn

## Русский

**PickmeTurn** — графический клиент для Windows на WPF, предназначенный для удобного запуска и управления рабочим сценарием подключения **Free Turn Proxy + WireGuard**.

Приложение не является самостоятельной реализацией VPN-протокола: PickmeTurn предоставляет графический интерфейс, управляет локальным WireGuard-туннелем и клиентом Free Turn Proxy, а передача трафика выполняется через настроенную инфраструктуру FreeTurn/VK TURN.

### Возможности

- Тёмный интерфейс Windows-приложения на WPF.
- Несколько сохранённых профилей FreeTurn.
- Отдельная VK Call-ссылка для каждого профиля.
- Защита сохранённых данных профилей с помощью Windows DPAPI для текущего пользователя.
- Работа в системном трее.
- Динамический индикатор состояния подключения и фирменный смайлик PickmeTurn.
- Поддержка ручного прохождения CAPTCHA через встроенный клиент Free Turn Proxy.
- Корректная очистка WireGuard-туннеля, процессов FreeTurn и временных файлов при отключении и завершении работы.
- Self-contained x64-сборка: конечному пользователю не требуется отдельно устанавливать .NET.

### Требования

#### Для пользователя

- Windows x64.
- Профиль FreeTurn и необходимая VK Call-ссылка.
- Совместимый сервер FreeTurn / WireGuard.

#### Для сборки

- Windows x64.
- .NET 8 SDK.
- PowerShell.
- Для создания установщика — Inno Setup 6 (также совместим с Inno Setup 7).

### Сборка приложения

Из корневого каталога репозитория:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\build.ps1
```

Готовый self-contained исполняемый файл будет создан здесь:

```text
publish\PickmeTurn.exe
```

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

**PickmeTurn** is a Windows WPF graphical client designed to provide a convenient interface for managing a **Free Turn Proxy + WireGuard** connection workflow.

The application is not a standalone VPN protocol implementation. PickmeTurn provides the graphical interface, manages the local WireGuard tunnel and the Free Turn Proxy client, while traffic is carried through the configured FreeTurn/VK TURN infrastructure.

### Features

- Dark Windows-style WPF interface.
- Multiple saved FreeTurn profiles.
- A separate VK Call link for each profile.
- Windows DPAPI protection for saved profile data for the current user.
- System tray integration.
- Dynamic connection status indicator and PickmeTurn mascot.
- Manual CAPTCHA flow supported by the bundled Free Turn Proxy client.
- Deterministic cleanup of the WireGuard tunnel, FreeTurn processes, and temporary files on disconnect and application exit.
- Self-contained x64 build: the end user does not need to install .NET separately.

### Requirements

#### End user

- Windows x64.
- A FreeTurn profile and the required VK Call link.
- A compatible Free Turn Proxy / WireGuard server.

#### Build machine

- Windows x64.
- .NET 8 SDK.
- PowerShell.
- Inno Setup 6 for building the Windows installer (Inno Setup 7 is also supported).

### Building the application

From the repository root:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\build.ps1
```

The self-contained executable is produced as:

```text
publish\PickmeTurn.exe
```

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

## 1.1.0

PickmeTurn 1.1.0 updates the bundled FreeTurn Windows core to 4.0.1 and enables the core's native automatic CAPTCHA flow with browser-based manual fallback when required. The application explicitly uses the desktop authentication platform.

The existing PickmeTurn UI, profile storage, DPAPI protection, WireGuard integration, tray behavior, and cleanup flow are retained.

The build script downloads the pinned FreeTurn 4.0.1 Windows binary from the upstream GitHub release and verifies its SHA-256 against the published checksums.txt before embedding it into PickmeTurn.

See RELEASE-NOTES-1.1.0.md for the release details.
