# Установщик PickmeTurn

## Русский

Установщик PickmeTurn создаётся с помощью Inno Setup 6.

### Сборка

1. Установите .NET 8 SDK на машине сборки.
2. Установите Inno Setup 6.
3. Из корневого каталога репозитория выполните:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\build-installer.ps1
```

Результат:

```text
installer-output\PickmeTurn-Setup-1.1.0.exe
```

---

## English

The PickmeTurn installer is built with Inno Setup 6.

### Build

1. Install the .NET 8 SDK on the build machine.
2. Install Inno Setup 6.
3. From the repository root run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\build-installer.ps1
```

Output:

```text
installer-output\PickmeTurn-Setup-1.1.0.exe
```
