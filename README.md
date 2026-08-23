# KernelMacSpoofer 🌊

A high-performance, native Windows MAC address spoofer and automated connection identity rotator built with **WinUI 3** and **.NET 8**.

Featuring a sleek dark glassmorphic design inspired by deep blue wave gradients and direct Windows kernel/registry interaction.

---

## 📖 Description

**KernelMacSpoofer** is a lightweight, pure C# application designed to randomize and manage network adapter MAC addresses on Windows without relying on external Python scripts or third-party CLI wrappers. Built for developers, privacy researchers, and network engineers, it provides automated interval rotation, instant spoofing, and driver-compliant address generation for both Wi-Fi and Ethernet adapters.

---

## ✨ Features

- **100% Native Pure C#**: No external Python scripts, third-party CLI tools, or wrappers.
- **Smart Adapter Detection**: Automatically identifies active Wi-Fi and Ethernet network interfaces.
- **Standards Compliant**: Generates cryptographically secure, locally administered unicast MAC addresses (`02:XX:XX:...`, `06:XX:XX:...`, `0A:XX:XX:...`, `0E:XX:XX:...`) guaranteed to work with Intel, Realtek, and modern network drivers.
- **Auto-Rotation**: Configurable automated timer rotation (from 5 seconds up to 24 hours).
- **Modern UI**: WinUI 3 desktop interface with fluent controls, translucent frosted cards, and deep blue wave gradient background.
- **Zero Licensing Conflicts**: 100% original codebase ready for open-source distribution under the MIT license.

---

## 🚀 Getting Started

### Prerequisites
- Windows 10 (version 1809 / build 17763 or higher) or Windows 11
- Administrator privileges (required to modify network registry settings and restart network adapters)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (for building from source)

### Building from Source

```bash
# Clone the repository
git clone https://github.com/Yuezhiui/KernelMacSpoofer.git
cd KernelMacSpoofer/MacSpoof

# Build x64 release
dotnet build MacSpoof.csproj -c Release -p:Platform=x64

# Publish standalone executable
dotnet publish MacSpoof.csproj -c Release -p:Platform=x64 -p:PublishProfile=win-x64
```

The published executable will be located in:
`MacSpoof/MacSpoof/bin/Release/net8.0-windows10.0.19041.0/win-x64/publish/MacSpoof.exe`

---

## 🛠️ Project Structure

```
KernelMacSpoofer/
├── MacSpoof/                   # Main WinUI 3 Project
│   ├── Assets/                 # UI Images and Backgrounds
│   │   ├── background.png      # Blue Wave Gradient Background
│   │   └── ...
│   ├── Properties/
│   │   └── PublishProfiles/    # Deployment profiles
│   ├── App.xaml / App.xaml.cs  # Application entrypoint
│   ├── MainWindow.xaml / .cs   # Main user interface
│   ├── MacSpoofService.cs      # Native C# MAC spoofing & registry engine
│   ├── app.manifest            # UAC Administrator execution elevation
│   ├── Package.appxmanifest    # Windows app metadata
│   └── MacSpoof.csproj         # Project configuration
├── .gitignore                  # Git ignore rules
├── LICENSE                     # MIT Open-Source License
└── README.md                   # Documentation
```

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
