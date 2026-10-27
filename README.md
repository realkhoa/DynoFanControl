# DynoFanControl — Universal Fan Control & Hardware Telemetry Suite

A lightweight, high-performance, dark-themed universal fan control and thermal telemetry utility for Windows 10/11. Designed to operate across **all graphics cards** (NVIDIA GeForce/RTX/Quadro, AMD Radeon RX/APU, Intel Arc/Iris Xe) and **all computer platforms** — from custom desktop PCs with motherboard Super I/O headers to major gaming laptop manufacturers (Acer Nitro/Predator, ASUS ROG/TUF, Lenovo Legion/LOQ/ThinkPad, MSI Gaming, Dell Alienware/G-Series, and HP OMEN/Victus).

---

## 1. Purpose & Scope

Modern computers frequently struggle with bloated OEM utility suites (such as NitroSense, PredatorSense, Armoury Crate, Lenovo Vantage, MSI Center, Alienware Command Center, or HP OMEN Gaming Hub). Clean Windows installations often leave users with zero manual fan control or deafening cooling profiles. Desktop users often lack an all-in-one lightweight tool that seamlessly bridges motherboard fan headers and discrete GPU heatsinks with dedicated temperature curves.

DynoFanControl solves this with a **zero-dependency, universal hardware abstraction layer**:
- **Universal Multi-GPU Telemetry & Control**: Direct support for all graphics card architectures:
  - **NVIDIA**: Native Management Library (`nvml.dll`) and NVAPI for GeForce GTX/RTX, Quadro, and Mobile GPUs.
  - **AMD Radeon**: Native AMD Display Library (`atiadlxx.dll` / `atiadlxy.dll`) for discrete RX 5000/6000/7000/8000 series and integrated Radeon Vega/RDNA APUs.
  - **Intel Arc & Iris Xe**: Dedicated hardware telemetry and curve targets via LibreHardwareMonitor.
- **Multi-Platform Fan Hardware Backends**:
  - **Desktop PCs & Custom Builds**: Motherboard LPC Super I/O bus control (Nuvoton NCT67xx, ITE IT86xx/IT87xx, Fintek, Winbond) and discrete GPU fan driver.
  - **Acer Gaming (Nitro & Predator)**: Sub-millisecond direct ACPI Embedded Controller (EC) I/O via WinRing0 kernel driver and `root\wmi: AcerGamingFunction`.
  - **ASUS ROG & TUF Gaming**: Direct ACPI WMI driver (`root\wmi: ASUS_WMI`) with discrete Silent, Performance, and Turbo modes.
  - **Lenovo Legion, LOQ & ThinkPad**: Thermal WMI driver (`LENOVO_GAMEZONE_DATA`) with Quiet, Balanced, and Extreme Performance presets.
  - **MSI Gaming (Dragon Center / MSI Center)**: Direct EC register manipulation for Katana, Raider, Stealth, Pulse, Vector, and Titan with Cooler Boost.
  - **Dell Alienware & G-Series**: Alienware Command Center WMI interface (`AWCC_WmiMethod`).
- **Independent Smart Fan Curves**: Dual independent temperature curve controllers for CPU and GPU with downward hysteresis to eliminate fan oscillation.
- **Hardware Telemetry & Sensor Drawer**: Real-time RPM tachometers, power consumption (Watts), clock speeds, fan utilization, and storage temperatures.
- **Failsafe Watchdog & Auto-Reset**: Automatic 100% emergency cooling engaged if thermal thresholds are breached; guaranteed restoration to BIOS/firmware control upon application exit or system shutdown.
- **Elevated Silent Auto-Start**: Boot-to-tray execution via Windows Task Scheduler without UAC prompts.

---

## 2. Architecture & Data Flow

DynoFanControl implements a decoupled, 4-tier modular architecture:

```
┌────────────────────────────────────────────────────────────────────────┐
│                        UI Layer (WPF / XAML)                           │
│     Modern Dark Glassmorphism, Dual Tachometers, Multi-GPU Selector   │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ DataBinding / MVVM
┌───────────────────────────────────▼────────────────────────────────────┐
│                  ViewModel Layer (FanViewModel)                        │
│     Target GPU switching, Independent Curve Orchestration, Presets    │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ Commands / Events
┌───────────────────────────────────▼────────────────────────────────────┐
│                         Core Services Layer                            │
│  Dual FanCurveController │ SensorMonitorService │ StartupTaskScheduler │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ Hardware Abstraction
┌───────────────────────────────────▼────────────────────────────────────┐
│                    Hardware Layer & Universal Drivers                  │
│                                                                        │
│  UniversalDriverCoordinator (Auto-detection, priority scoring)        │
│  ├── Desktop PC Drivers:                                               │
│  │   ├── SuperIoFanDriver (Nuvoton NCT67xx, ITE IT86xx/87xx, Fintek)   │
│  │   └── DesktopGpuFanDriver (NVIDIA NVAPI/NVML & AMD ADL)             │
│  ├── Laptop OEM Drivers:                                               │
│  │   ├── AcerEcDriver / AcerWmiDriver (Nitro 5 & Predator)             │
│  │   ├── AsusWmiDriver (ROG Zephyrus, Strix, TUF Gaming)               │
│  │   ├── LenovoEcDriver (Legion 5/7/Pro, LOQ, IdeaPad Gaming)          │
│  │   ├── MsiEcDriver (MSI Katana, Raider, Stealth, Pulse, Titan)       │
│  │   └── DellAlienwareWmiDriver (Alienware m15/m16/m18, Dell G15/G16)   │
│  └── Universal GPU Subsystem:                                          │
│      ├── GpuManager (Multi-GPU coordinator & hot-selection)            │
│      ├── NvmlDriver (Native P/Invoke nvml.dll)                         │
│      ├── AmdGpuDriver (Native P/Invoke atiadlxx.dll / atiadlxy.dll)    │
│      └── LibreHardwareMonitorLib (Multi-vendor sensors & controllers)  │
└────────────────────────────────────────────────────────────────────────┘
```

### Data Flow
1. **Platform Auto-Discovery**: `PlatformDetector` queries SMBIOS, Win32_ComputerSystem, and Win32_BaseBoard to determine motherboard vendor and chassis form-factor (Laptop vs. Desktop).
2. **GPU Enumeration**: `GpuManager` detects all graphics processors in the system (e.g. AMD Radeon 780M iGPU + NVIDIA GeForce RTX 4070 dGPU).
3. **Driver Coordination**: `UniversalDriverCoordinator` ranks and initializes the highest-priority compatible fan control drivers for the host platform.
4. **Telemetry Polling**: `SensorMonitorService` queries CPU package sensors, NVML/ADL GPU metrics, and tachometers every 1000ms.
5. **Curve Evaluation**: Dual `FanCurveController` engines independently calculate required fan speeds for the CPU and active target GPU based on user-configured curve points and 4-second downward hysteresis.
6. **Safety Watchdog**: If any sensor exceeds the configured Overheat Guard limit (default 88°C), 100% fan speed is immediately enforced.
7. **Hardware Dispatch**: `UniversalDriverCoordinator` delegates fan speed duty cycles to the appropriate hardware registers or LPC controllers.

---

## 3. Usage & Features

### System Requirements
- **Operating System**: Windows 10 or Windows 11 (64-bit, x64).
- **Supported Hardware**:
  - **Desktop PCs**: ASUS, MSI, Gigabyte, ASRock, EVGA, Biostar motherboards with Nuvoton or ITE Super I/O chips; all NVIDIA and AMD discrete graphics cards.
  - **Laptops**: Acer (Nitro/Predator), ASUS (ROG/TUF), Lenovo (Legion/LOQ), MSI (Gaming/Creator), Dell (Alienware/G-Series), and HP (OMEN/Victus).
- **Privileges**: Administrator rights (required for kernel IOCTL and Super I/O ring access).
- **Runtime**: .NET 9.0 Desktop Runtime (x64).

### Operating Modes
| Mode | Description |
|---|---|
| **AUTO** | Restores native BIOS / firmware / OEM fan curve control. |
| **SILENT** | Locks CPU and GPU fans to 30% for whisper-quiet office and productivity work. |
| **BALANCED** | Locks fans to 60% for everyday gaming and moderate workloads. |
| **TURBO** | Forces 100% fan speed and activates OEM CoolBoost / Overboost where supported. |
| **SMART CURVE** | Independently adjusts CPU and GPU fans based on customizable temperature curves. |
| **CUSTOM** | Direct manual speed sliders (0% to 100%) with optional linked synchronization. |

### Configuration & Settings
- **Target GPU Selection**: In multi-GPU systems (e.g. laptop APU + discrete GPU or multi-card desktops), select which GPU drives the GPU fan curve in the Settings modal (`⚙`).
- **Independent vs. Synced Curves**: CPU and GPU curves operate independently by default. Enable `Sync CPU & GPU Curves` if you prefer unified fan speed ramping.
- **Overheat Guard**: Configurable thermal trigger (75°C - 95°C) that automatically overrides manual settings to 100% cooling.
- **Elevated Auto-Start**: Automatically configures a Windows Task Scheduler logon trigger with highest privileges (`/rl highest`) to ensure silent boot into system tray without UAC prompts.

---

## 4. Technical Notes, Edge Cases & Gotchas

### Kernel Driver & Direct IO Port Access
- Direct ACPI EC communication uses standard IO ports `0x62` (Data) and `0x66` (Status/Command).
- WinRing0 kernel driver IOCTLs (`0x9C4060CC` for port read, `0x9C40A0D8` for port write) execute in sub-millisecond time.
- All hardware writes are protected by thread synchronization locks (`lock (_lock)`) to prevent interleaved commands from corrupting controller state.

### Desktop Super I/O LPC Bus Support
- Desktop motherboards do not expose standard ACPI EC registers. Instead, fan headers are wired to LPC Super I/O chips (Nuvoton NCT6779D/6791D/6796D/6798D or ITE IT8620E/IT8686E/IT8728F).
- `SuperIoFanDriver` binds to LibreHardwareMonitor's `SensorType.Control` (`IControl.SetSoftware()`), providing native fan control for desktop chassis fans and CPU AIO coolers without needing proprietary motherboard bloatware.

### Discrete GPU Fan Overrides
- Laptop discrete GPUs (NVIDIA RTX Mobile, AMD Radeon RX Mobile) share thermal dissipation heat pipes and are regulated directly via the laptop's embedded controller.
- Desktop discrete GPUs regulate their on-shroud heatsink fans independently via NVAPI / NVML or AMD ADL (`atiadlxx.dll`). `DesktopGpuFanDriver` handles this seamlessly.

### Hybrid Graphics & Sleep States
- When a laptop discrete GPU enters deep runtime power management (RTD3 / Sleep state), `nvmlDeviceGetTemperature` may return `NVML_ERROR_GPU_IS_LOST` or `0°C`.
- `GpuManager` detects sleeping states and safely retains the last valid temperature, preventing fan speed spikes when the GPU enters or leaves power-saving mode.

---

## 5. Building & Publishing

### Compilation via Native MSBuild (Release x64)
```powershell
# Restore and compile solution with zero warnings
msbuild DynoFanControl.sln /p:Configuration=Release /p:Platform=x64 /m /v:m
```

### Publishing Standalone Binary
```powershell
# Publish framework-dependent x64 release
dotnet publish DynoFanControl\DynoFanControl.csproj -c Release -r win-x64 --self-contained false -o Publish

# Optional: Ensure backward-compatible binary alias
Copy-Item Publish\DynoFanControl.exe Publish\FanCtl.exe -Force
```
Binaries and assets are output to `Publish\DynoFanControl.exe`.
