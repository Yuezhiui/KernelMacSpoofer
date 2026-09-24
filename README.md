# MacSpoof

A high-performance, native Windows MAC address spoofer and automated connection identity rotator developed with WinUI 3 and .NET 8.

Features a modern WinUI 3 interface, Material Symbols-derived icon assets, and direct Windows registry-level network adapter management.

---

## Overview

MacSpoof is a lightweight, standalone C# application engineered to randomize and manage network adapter MAC addresses on Windows operating systems without dependencies on external Python scripts, command-line wrappers, or third-party binaries.

The application allows instant single-execution spoofing with safety cooldowns as well as automated interval-based rotation across active Wi-Fi and Ethernet adapters.

---

## Features

- **Native C# Implementation**: Zero external dependencies on Python runtimes or third-party executables.
- **Intelligent Interface Detection**: Automatically discovers active Wi-Fi and Ethernet network interfaces.
- **IEEE Standards Compliance**: Generates cryptographically secure, locally administered unicast MAC addresses (`02:XX:XX:...`, `06:XX:XX:...`, `0A:XX:XX:...`, `0E:XX:XX:...`) with locally administered unicast bits set. Driver support varies; some Wi-Fi adapters ignore or reject overrides.
- **Execution Modes**:
  - **Once**: Immediate MAC randomization with an integrated 5-second cooldown cycle.
  - **Automated Intervals**: Continuous periodic rotation configurable from 5 minutes up to 24 hours, measured after each operation finishes.
- **Modern User Interface**: Native Windows App SDK / WinUI 3 controls with translucent frosted styling and dark-adapted controls.
- **Open Source Licensing**: Clean codebase distributed under the permissive MIT License.

---

## Prerequisites

- **Operating System**: Windows 10 (version 1809 / build 17763 or higher) or Windows 11
- **Privileges**: Administrator privileges (required for Windows network registry modification and adapter reset)
- **Runtime / SDK**: .NET 8.0 SDK (for compilation from source)

---

## Building and Publishing

### Clone the Repository
```bash
git clone https://github.com/Yuezhiui/KernelMacSpoofer.git MacSpoof
cd MacSpoof/MacSpoof/MacSpoof
```

### Compile Release Binary
```bash
dotnet build MacSpoof.csproj -c Release -p:Platform=x64
```

### Publish Standalone Executable
```bash
dotnet publish MacSpoof.csproj -c Release -p:Platform=x64 -p:PublishProfile=win-x64
```

The compiled standalone executable will be located in:
`MacSpoof/bin/Release/net8.0-windows10.0.19041.0/win-x64/publish/MacSpoof.exe`

---

## Project Structure

```
MacSpoof/
├── MacSpoof/                   # Main WinUI 3 Project
│   ├── Assets/                 # UI Assets and Graphical Resources
│   │   ├── material-*.svg      # Material Symbols-derived UI icons
│   │   └── ...
│   ├── Properties/
│   │   └── PublishProfiles/    # Build and Deployment Profiles
│   ├── App.xaml / App.xaml.cs  # Application Entry Point
│   ├── MainWindow.xaml / .cs   # User Interface and Controller Logic
│   ├── MacSpoofService.cs      # Native C# Windows Registry Spoofing Engine
│   ├── app.manifest            # UAC Administrator Execution Manifest
│   ├── Package.appxmanifest    # Windows Application Metadata
│   └── MacSpoof.csproj         # Project Configuration File
├── .gitignore                  # Git Ignore Rules
├── LICENSE                     # MIT License
└── README.md                   # Project Documentation
```

---

## License

MIT — Copyright (c) 2026 Zhi  
See [LICENSE](LICENSE) for full text.

### UI icon attribution

The refreshed interface uses SVG icon assets derived from [Google Material Symbols](https://fonts.google.com/icons). Material Symbols are provided by Google under the [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0). Those third-party icon assets retain their Apache 2.0 license; MacSpoof's own source code remains licensed under MIT by Zhi. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for the bundled third-party notice.


## Connection reliability and cache cleanup

Select the intended adapter, including a disconnected adapter when recovering from a failed change. Changes match its registry entry by GUID, serialize operations across app instances, re-resolve the adapter alias before restart commands, check command failures/timeouts, and verify the effective MAC. If an adapter that had a usable local IP before the change does not recover one within 45 seconds, the previous registry setting is restored and the adapter restarted. This checks local connectivity, not Internet access. Rotation stops on MAC/recovery errors, while a cache-refresh warning does not incorrectly report the spoof itself as failed.

- **Restore default MAC** removes the NetworkAddress override and restarts the selected adapter. Windows Wi-Fi randomization or the driver may still determine the effective address.
- **Clear network caches** flushes the system DNS cache, clears the selected adapter's active IPv4/IPv6 neighbor caches, and renews IPv4 DHCP if enabled. It does not release the current lease first, delete saved Wi-Fi passwords, change static IP settings, or reset the whole network stack. A checkbox enables the same cleanup after each successful change.
- A MAC override replaces the previous override; there is no list of old MAC addresses in this setting to delete. Cleanup cannot erase the factory MAC, router logs, DHCP server history, or make the computer a completely new device. A DHCP renewal does not guarantee a different IP address.
- If Wi-Fi fails, stop rotation, select Wi-Fi, choose **Restore default MAC**, then reconnect through Windows Wi-Fi settings. Some drivers and access points do not support the requested change.

Command references: [Microsoft ipconfig documentation](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/ipconfig) and [Microsoft netsh interface documentation](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/netsh-interface).

Non-disruptive checks: `dotnet run --project tests/MacSpoof.Checks`. These cover generated addresses, validation, formatting, rollback expectations, usable-IP state, safe command argument construction, partial cache-warning behavior, and missing adapters. They do not perform live registry writes or adapter restarts; hardware testing is still required.


## Windows installer and portable release (v1.2.0)

Download `MacSpoof-Setup-v1.2.0-x64.exe` from [GitHub Releases](https://github.com/Yuezhiui/KernelMacSpoofer/releases/latest), or use the portable `MacSpoof-v1.2.0-Portable-x64.zip`. Release artifacts follow the patterns `MacSpoof-Setup-vX-x64.exe` and `MacSpoof-vX-Portable-x64.zip`.

The installer contains the self-contained Windows x64 app; an optional desktop shortcut and Windows uninstall entry are included. No network settings are changed by installation or uninstallation. Use Restore default MAC inside the app before uninstalling if you want to remove an applied override. This is a Windows application, not a macOS application.

To build from the repository root with .NET 8 and Inno Setup 6:

```powershell
dotnet publish MacSpoof/MacSpoof/MacSpoof.csproj -c Release -p:Platform=x64 -p:PublishProfile=win-x64 -o MacSpoof_Fixed
iscc installer.iss
```

The installer is generated under `artifacts/`. The portable ZIP includes `Run_MacSpoof.bat`, `LICENSE`, and `THIRD_PARTY_NOTICES.md`; the launcher also works from the repository root after publishing to `MacSpoof_Fixed`. Every release includes `SHA256SUMS.txt` for the installer and portable ZIP. Build and non-disruptive checks passed; adapter-specific Wi-Fi reconnection and rollback still require live hardware testing.

### Release signing

The GitHub Actions release workflow supports optional Authenticode signing with these repository secrets:

- `WINDOWS_SIGNING_PFX_BASE64` — the Base64-encoded contents of the code-signing `.pfx` file.
- `WINDOWS_SIGNING_PFX_PASSWORD` — the password for that `.pfx` file.

When both secrets are configured, the workflow signs `MacSpoof.exe` before creating the installer and portable ZIP, signs the generated installer, and only then writes `SHA256SUMS.txt` and publishes the release. When both secrets are absent, the release still builds and the workflow/release notes clearly report that the artifacts are unsigned. A partial signing configuration fails the workflow instead of silently producing an unexpected unsigned release.
