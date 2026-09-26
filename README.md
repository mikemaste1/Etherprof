# Etherprof

<p align="center">
  <img src="src/Etherprof.App/app.ico" alt="Etherprof Logo" width="96" height="96" />
</p>

<p align="center">
  <strong>Fast, Specialized Windows Network Adapter Management, Visual Diagnostics &amp; Performance Toolkit</strong>
</p>

<p align="center">
  <a href="https://github.com/mikemaste1/Etherprof/releases"><img src="https://img.shields.io/badge/version-v0.4.2-blue.svg" alt="Version" /></a>
  <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-10.0-512BD4.svg" alt=".NET 10" /></a>
  <a href="https://learn.microsoft.com/en-us/windows/package-manager/"><img src="https://img.shields.io/badge/winget-mikemaste1.Etherprof-0078D4.svg" alt="WinGet Package" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-green.svg" alt="License: MIT" /></a>
  <img src="https://img.shields.io/badge/tests-130%20passing-brightgreen.svg" alt="Tests: 130 Passing" />
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011%20(x64)-0078D7.svg" alt="Platform" />
</p>

---

## 📌 Overview

**Etherprof** is a high-productivity Windows desktop utility engineered for network administrators, field engineers, DevOps specialists, and industrial automation/PLC technicians. 

Switching network configurations in Windows usually requires navigating cumbersome legacy dialogs (`ncpa.cpl`) or memorizing long elevated PowerShell / `netsh` commands. Etherprof turns IP changes, subnet scans, and multi-target connectivity verification into an instantaneous, 1-click operation with real-time visual feedback.

---

## ✨ Key Features

### ⚡ 1. Rapid IP Configuration & Profiles
- **1-Click Profile Switching**: Switch between static IP profiles (lab subnets, PLC racks, field switches) and DHCP in under a second.
- **Mimic DHCP**: 1-click feature that clones your current dynamic DHCP lease into a permanent static profile with all gateway and DNS settings intact.
- **Quick IP Bar**: Type or paste any IP address (with or without CIDR `/24`), auto-calculates subnet masks, and applies it immediately.
- **Secondary IP Addresses**: Add additional IPv4 addresses to the active adapter on the fly without overwriting the primary IP.

### 📊 2. Continuous Visual Ping & Connectivity Diagnostics
- **Color-Coded Target Badges**: Instant green/red status indicators with numeric latency readouts.
- **Real-Time History Trails**: 20-sample visual sparkline trails plotting latency history for each host.
- **Ad-Hoc Ping**: Instant IP suggestion dialog based on the active subnet with a youth-byte focus.
  - **Complement Mode**: Option to append ad-hoc pings to the active set rather than replacing it.
  - **🌐 Open Browser**: 1-click web launcher with HTTP/HTTPS and custom port selection.
  - **🛣️ Interactive Traceroute**: Built-in multi-hop traceroute dialog with live latency measurements.
- **⚡ Quick Ping Mode**: Fast 200 ms ping intervals for rapid cable testing and port flapping detection.

### 📌 3. Floating Always-On-Top Mini-HUD
- A draggable, transparent floating status HUD that remains on top of all windows.
- Displays continuous ping responses and history sparklines even when Etherprof is minimized or when working inside full-screen consoles, IDEs, or RDP sessions.

### 🌐 4. Ephemeral Subnet Prefix Scanner ("Notebook Scans")
- Scan entire `/24` (or custom prefix) subnets to discover all active hosts.
- **Non-modal & Concurrent**: Operates independently without blocking the main window; spawn multiple scan instances simultaneously.
- **Status Notebook**: Each scan window is numbered (`Scan #1`, `Scan #2`) and timestamped, allowing you to minimize and keep previous scan snapshots as historical notebooks.
- **Feed to Continuous Ping**: 1-click button to feed discovered hosts directly into the main window's visual ping runner.

### 📶 5. Wi-Fi Control & Signal Monitor
- **Radio Toggle**: Toggle wireless connectivity on/off cleanly (matching Windows Action Center `Win + A` radio control).
- **Signal Monitor**: Real-time RSSI signal strength graph, channel details, BSSID inspection, and access point scanner.

### 🚀 6. Performance Benchmarking
- **Integrated Speed Test**: Measure internet upload and download throughput directly within the application.
- **LAN Stream Test Client & Server**: High-throughput multi-stream TCP and UDP bandwidth generator to stress-test local networks, Wi-Fi links, and managed switch ports.

### ⇄ 7. Quick Qualifying Adapter Switcher
- Instantly toggles between your machine's qualifying physical network interfaces (e.g. Realtek Ethernet GbE and Intel Wi-Fi), automatically filtering out virtual VMware, Hyper-V, VPN, and TAP adapters.

### 🔴🟢 8. Prominent Media Link & Dynamic Color Signaling
- **Media Link Badge**: Displays physical cable link state (`LINK UP` in vivid green / `LINK DOWN` in rich red).
- **Google DNS Health**: Background internet check (8.8.8.8) with inline badge and title bar update.
- **Dynamic Adapter Card Color**: The entire adapter card changes its background and border between soft green (connected) and soft red (media disconnected) for unmistakable physical link awareness.

### 📋 9. Topic-Filtered Activity & Event Log
- Integrated activity log tracking link state transitions, IP changes, profile creations, and diagnostic events.
- Instant topic filter chips: `[ALL]`, `[ADAPTER]`, `[IP]`, `[PROFILE]`, `[PING]`, `[WIFI]`, `[SPEED]`, `[STREAM]`.

---

## 📥 Installation

### Method 1: Windows Package Manager (WinGet) — Recommended
Install directly from PowerShell or Command Prompt:

```powershell
winget install mikemaste1.Etherprof
```

To update in the future:
```powershell
winget upgrade mikemaste1.Etherprof
```

### Method 2: Inno Setup Installer Download
1. Head to the **[Latest Release](https://github.com/mikemaste1/Etherprof/releases/latest)** page.
2. Download `Etherprof-Setup-0.4.2.exe`.
3. Run the installer. It configures standard Program Files installation, Start Menu shortcuts, and uninstaller.
   *(Silent installation is supported via `/VERYSILENT /NORESTART`).*

> **Note**: Because Etherprof modifies network adapter IP configurations and binds raw sockets for ICMP/UDP tests, Windows requires Administrator privileges. Click **Yes** on the UAC prompt when launching.

---

## 🛠️ Building & Releasing

### Prerequisites
- Windows 10 (1809+) or Windows 11 (x64)
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Inno Setup 6](https://jrsoftware.org/isdl.php) (`winget install JRSoftware.InnoSetup`)

### 1. Build and Run Tests
```powershell
dotnet build --nologo
dotnet test --nologo
```

### 2. Build Release Installer
A single script builds the self-contained publication and compiles the Inno Setup installer:
```powershell
.\build-release.ps1
```
The resulting installer is generated at:
`artifacts/Etherprof-Setup-0.4.2.exe`

For detailed release workflows and WinGet submission instructions, see **[docs/RELEASING.md](docs/RELEASING.md)**.

---

## 🏛️ Architecture Overview

The solution is architected according to clean architectural boundaries with complete separation of UI, network interop, and business logic:

```
Etherprof/
├── src/
│   ├── Etherprof.Contracts/         # Interfaces, domain models, DTOs
│   ├── Etherprof.Core/              # Subnet math, CIDR validation, IP parsing
│   ├── Etherprof.Network.Windows/   # WMI, CIM, NetAdapter, and NetIP PowerShell interop
│   ├── Etherprof.Wifi.Windows/      # Native Windows WLAN API (wlanapi.dll) integration
│   ├── Etherprof.Testing/           # Continuous ICMP ping runner with history buffers
│   ├── Etherprof.SpeedTest/         # Multi-connection HTTP speed test engine
│   ├── Etherprof.StreamTest/        # High-throughput TCP/UDP client and server
│   ├── Etherprof.Storage/           # JSON file repository for profiles and test sets
│   └── Etherprof.App/               # WPF UI (MVVM), custom controls, HUD, and dialogs
└── tests/
    ├── Etherprof.Core.Tests/        # SubnetCalculator and IP parsing unit tests
    ├── Etherprof.StreamTest.Tests/  # Client/server socket integration tests
    └── Etherprof.Testing.Tests/     # TestRunner and ping history tests
```

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
