# KernelMacSpoofer

A high-performance, native Windows MAC address spoofer and automated connection identity rotator developed with WinUI 3 and .NET 8.

Features a modern dark glassmorphism interface with deep blue wave gradient styling and direct Windows registry-level network adapter management.

---

## Overview

KernelMacSpoofer is a lightweight, standalone C# application engineered to randomize and manage network adapter MAC addresses on Windows operating systems without dependencies on external Python scripts, command-line wrappers, or third-party binaries.

The application allows instant single-execution spoofing with safety cooldowns as well as automated interval-based rotation across active Wi-Fi and Ethernet adapters.

---

## Features

- **Native C# Implementation**: Zero external dependencies on Python runtimes or third-party executables.
- **Intelligent Interface Detection**: Automatically discovers active Wi-Fi and Ethernet network interfaces.
- **IEEE Standards Compliance**: Generates cryptographically secure, locally administered unicast MAC addresses (`02:XX:XX:...`, `06:XX:XX:...`, `0A:XX:XX:...`, `0E:XX:XX:...`) fully compatible with Intel, Realtek, Qualcomm, and other modern network controller drivers.
- **Execution Modes**:
  - **Once**: Immediate MAC randomization with an integrated 5-second cooldown cycle.
  - **Automated Intervals**: Continuous periodic rotation configurable from 5 seconds up to 24 hours.
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
git clone https://github.com/Yuezhiui/KernelMacSpoofer.git
cd KernelMacSpoofer/MacSpoof
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
KernelMacSpoofer/
├── MacSpoof/                   # Main WinUI 3 Project
│   ├── Assets/                 # UI Assets and Graphical Resources
│   │   ├── background.png      # Deep Blue Wave Gradient Background
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
