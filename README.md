# DynoFanControl — Universal Fan Control, GPU Tuning & Telemetry Suite

A lightweight, high-performance, dark-themed universal fan control, hardware telemetry, and GPU overclocking/undervolting utility for Windows 10/11. Designed to operate across **all graphics cards** (NVIDIA GeForce/RTX/Quadro, AMD Radeon RX/APU, Intel Arc/Iris Xe) and **all computer platforms** — from custom desktop PCs with motherboard Super I/O headers to major gaming laptop manufacturers (Acer Nitro/Predator, ASUS ROG/TUF, Lenovo Legion/LOQ/ThinkPad, MSI Gaming, Dell Alienware/G-Series, and HP OMEN/Victus) — fully integrating the capabilities of **MSI Afterburner** and **RivaTuner Statistics Server (RTSS)**.

---

## 1. Purpose & Scope

Modern computers frequently struggle with bloated OEM utility suites (such as NitroSense, PredatorSense, Armoury Crate, Lenovo Vantage, MSI Center, Alienware Command Center, or HP OMEN Gaming Hub). Clean Windows installations often leave users with zero manual fan control or deafening cooling profiles. Desktop users often lack an all-in-one lightweight tool that seamlessly bridges motherboard fan headers, discrete GPU heatsinks, clock tuning, and in-game overlays.

DynoFanControl solves this with a **zero-dependency, universal hardware abstraction layer**:
- **Integrated MSI Afterburner Engine**:
  - **GPU Clock & Voltage Tuning**: Real-time adjustment of Core Clock Offset (-500 MHz to +1000 MHz), Memory Clock Offset (-500 MHz to +1500 MHz), Power Limit Target (50% to 130%), Temperature Limit Target (65°C to 90°C), and Core Voltage Offset (0 mV to 100 mV) via native NVIDIA NVAPI P-States 2.0 and NVML.
  - **Factory Tuning Presets**: Quick one-click profiles (`Stock`, `Undervolt & Silent`, `Balanced Gaming`, `Max OC`, `Eco`).
  - **User Profile Slots**: 5 persistent custom tuning slots (`[1]` to `[5]`) with instant load and hot-save.
  - **Apply at Windows Startup**: Automatic clock and power limit enforcement at logon with crash protection and failsafe reset.
- **Real-Time Rolling Telemetry Graphs**:
  - 11 rolling telemetry series tracking 60-second histories with live Minimum, Current, and Maximum metrics.
  - Ultra-smooth hardware-accelerated WPF vector sparklines for:
    1. GPU Temperature (°C)
    2. GPU Core Clock (MHz)
    3. GPU Memory Clock (MHz)
    4. GPU Core Load (%)
    5. GPU Power Draw (Watts)
    6. GPU VRAM Usage (MB)
    7. GPU Fan Speed (RPM)
    8. CPU Temperature (°C)
    9. CPU Package Load (%)
    10. CPU Package Power (Watts)
    11. System RAM Usage (%)
- **RivaTuner Statistics Server (RTSS) In-Game OSD Client**:
  - Direct native client interfacing with RTSS Shared Memory (`RTSSSharedMemoryV2` memory-mapped file protocol).
  - Displays rich in-game on-screen display (OSD) overlays in DirectX 9/11/12, Vulkan, and OpenGL titles without injecting DirectX DLL hooks.
  - Granular metric toggles and dynamic color-coded telemetry tags.
- **Universal Multi-GPU Telemetry & Control**: Direct support for all graphics card architectures:
  - **NVIDIA**: Native Management Library (`nvml.dll`) and NVAPI (`nvapi64.dll`) for GeForce GTX/RTX, Quadro, and Mobile GPUs.
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
- **🎮 Per-Game & Per-App Automation Profiles**: Automatic foreground process detection via Win32 `GetForegroundWindow` seamlessly activating customized fan modes (e.g. `Turbo` or `Gaming Curve`) and RTSS OSD for games like Counter-Strike 2, Cyberpunk 2077, Dota 2, and Valorant, then quietly returning to `Silent` or `Balanced` upon switching back to desktop or productivity apps.
- **❄ CPU Thermal Cap & Power Limiting (Ryzen & Intel)**: Direct AMD Ryzen SMU and Intel MSR power management preventing CPUs from aggressively overshooting into 92°C-95°C thermal throttling territory, lowering temperatures by 10°C-15°C with zero noticeable FPS loss.
- **🔋 Battery-Aware Smart Switching**: Real-time AC line status and battery percentage detection (`kernel32.dll GetSystemPowerStatus`) automatically transitioning between high-performance plugged-in profiles and battery-saving silent eco curves when running on battery.
- **🔊 Fan Acoustics & Health Calibration Benchmark**: Automated duty cycle sweep (30% to 100%) measuring real-world tachometer curves (RPM vs. Duty Cycle) to verify bearing responsiveness, detect dust clogging, and record maximum physical fan velocity.
- **Hardware Telemetry & Sensor Drawer**: Real-time RPM tachometers, power consumption (Watts), clock speeds, fan utilization, and storage temperatures.
- **Failsafe Watchdog & Auto-Reset**: Automatic 100% emergency cooling engaged if thermal thresholds are breached; guaranteed restoration to BIOS/firmware control upon application exit or system shutdown.
- **Elevated Silent Auto-Start**: Boot-to-tray execution via Windows Task Scheduler without UAC prompts.

---

## 2. Architecture & Data Flow

DynoFanControl implements a decoupled, 4-tier modular architecture:

```
┌────────────────────────────────────────────────────────────────────────┐
│                        UI Layer (WPF / XAML)                           │
│  Modern Dark Glassmorphism, Dual Tachometers, Multi-GPU Selector,      │
│  MSI Afterburner Overclocking Modal, Telemetry Graphs, RTSS OSD Panel  │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ DataBinding / MVVM
┌───────────────────────────────────▼────────────────────────────────────┐
│                  ViewModel Layer (FanViewModel)                        │
│  11 TelemetrySeries, GpuTuningManager Coordinator, RTSS OSD Client,   │
│  Target GPU switching, Independent Curve Orchestration, Presets        │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ Commands / Events
┌───────────────────────────────────▼────────────────────────────────────┐
│                         Core Services Layer                            │
│  RtssOsdService │ Dual FanCurveController │ SensorMonitorService       │
│  GpuTuningManager │ StartupTaskScheduler │ ConfigManager               │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ Hardware Abstraction
┌───────────────────────────────────▼────────────────────────────────────┐
│                    Hardware Layer & Universal Drivers                  │
│                                                                        │
│  UniversalDriverCoordinator (Auto-detection, priority scoring)        │
│  ├── GPU Tuning & Clock Drivers (MSI Afterburner Engine):              │
│  │   ├── NvapiDriver (Native P/Invoke nvapi64.dll P-States 2.0)        │
│  │   └── NvmlDriver (Native P/Invoke nvml.dll power management)        │
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
│      ├── AmdGpuDriver (Native P/Invoke atiadlxx.dll / atiadlxy.dll)    │
│      └── LibreHardwareMonitorLib (Multi-vendor sensors & controllers)  │
└────────────────────────────────────────────────────────────────────────┘
```

### Data Flow
1. **Platform Auto-Discovery**: `PlatformDetector` queries SMBIOS, Win32_ComputerSystem, and Win32_BaseBoard to determine motherboard vendor and chassis form-factor (Laptop vs. Desktop).
2. **GPU Enumeration & Tuning Init**: `GpuManager` detects all graphics processors in the system. `GpuTuningManager` initializes `nvapi64.dll` and `nvml.dll`.
3. **Driver Coordination**: `UniversalDriverCoordinator` ranks and initializes the highest-priority compatible fan control drivers for the host platform.
4. **Telemetry Polling**: `SensorMonitorService` queries CPU package sensors, NVML/ADL GPU metrics, VRAM usage, clocks, power draw, and tachometers every 1000ms.
5. **Rolling Graph Updates**: All 11 `TelemetrySeries` update their 60-point circular ring buffers and recompute vector polygon and polyline points.
6. **RTSS OSD Synchronization**: If enabled, `RtssOsdService` builds formatted overlay lines and writes directly to `RTSSSharedMemoryV2`.
7. **Curve Evaluation**: Dual `FanCurveController` engines independently calculate required fan speeds for the CPU and active target GPU based on user-configured curve points and 4-second downward hysteresis.
8. **Safety Watchdog**: If any sensor exceeds the configured Overheat Guard limit (default 88°C), 100% fan speed is immediately enforced.
9. **Hardware Dispatch**: `UniversalDriverCoordinator` delegates fan speed duty cycles to the appropriate hardware registers or LPC controllers.

---

## 3. Usage & Features

### System Requirements
- **Operating System**: Windows 10 or Windows 11 (64-bit, x64).
- **Supported Hardware**:
  - **Desktop PCs**: ASUS, MSI, Gigabyte, ASRock, EVGA, Biostar motherboards with Nuvoton or ITE Super I/O chips; all NVIDIA and AMD discrete graphics cards.
  - **Laptops**: Acer (Nitro/Predator), ASUS (ROG/TUF), Lenovo (Legion/LOQ), MSI (Gaming/Creator), Dell (Alienware/G-Series), and HP (OMEN/Victus).
- **Privileges**: Administrator rights (required for kernel IOCTL and Super I/O ring access).
- **Runtime**: .NET 9.0 Desktop Runtime (x64).

### MSI Afterburner Suite Features
| Feature | Details |
|---|---|
| **⚡ GPU Tuning (`[⚡ OC]` Header)** | Real-time Core Clock (-500 to +1000 MHz), Memory Clock (-500 to +1500 MHz), Power Limit (50% to 130%), Temp Limit (65°C to 90°C), and Voltage Offset. |
| **Presets & Profile Slots** | 5 built-in presets (`Stock`, `Undervolt`, `Balanced`, `Max OC`, `Eco`) and 5 user profile slots (`[1]` to `[5]`) with hot-save. |
| **Apply at Startup** | Automatically re-applies the saved tuning profile upon Windows login with failsafe recovery. |
| **📉 Telemetry Graphs (`[📉 Graphs]`)** | Live 60-second rolling charts for 11 hardware metrics with real-time Min, Current, and Max indicators. |
| **📺 In-Game OSD (`[📺 OSD]`)** | Zero-lag RTSS Shared Memory client rendering OSD overlays in DirectX and Vulkan titles with color tags. |
| **🎮 Game Automation (`[🎮 Game]`)** | Per-process foreground detection switching between Turbo/Gaming curves and Silent desktop mode. |
| **❄ CPU Thermal Cap (`[❄ CPU]`)** | AMD Ryzen SMU & Intel MSR power clamping to prevent 92°C-95°C boost overshooting. |
| **🔋 Battery-Aware Power Switch** | Seamless transition between high-performance AC power and silent battery-saver modes. |
| **🔊 Fan Calibration (`[🔊 Test]`)** | Automated duty cycle sweep (30% to 100%) benchmarking RPM curves and fan bearing health. |

### Fan Operating Modes
| Mode | Description |
|---|---|
| **AUTO** | Restores native BIOS / firmware / OEM fan curve control. |
| **SILENT** | Locks CPU and GPU fans to 30% for whisper-quiet office and productivity work. |
| **BALANCED** | Locks fans to 60% for everyday gaming and moderate workloads. |
| **TURBO** | Forces 100% fan speed and activates OEM CoolBoost / Overboost where supported. |
| **SMART CURVE** | Independently adjusts CPU and GPU fans based on customizable temperature curves. |
| **CUSTOM** | Direct manual speed sliders (0% to 100%) with optional linked synchronization. |

### Configuration & Settings
- **Global Preset Persistence**: Selected preset modes and curve profiles are automatically persisted to global multi-tier storage (`%ProgramData%\DynoFanControl\config.json`, `%LocalAppData%\DynoFanControl\config.json`, and portable `config.json`).
- **Visual Preset Status**: Top header displays a dynamic preset badge (e.g. `⚡ BALANCED`, `📈 CURVE (Gaming)`), while mode buttons visually highlight active states with glowing borders and accent colors.
- **Dual-Redundancy Windows Auto-Start**: Automatically enabled by default. Configures Windows Task Scheduler with highest logon privileges (`/rl highest`) to ensure silent boot without UAC prompts, combined with an `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` backup.
- **Target GPU Selection**: In multi-GPU systems, select which GPU drives the GPU fan curve in the Settings modal (`⚙`).
- **Overheat Guard**: Configurable thermal trigger (75°C - 95°C) that automatically overrides manual settings to 100% cooling.

---

## 4. Technical Notes, Edge Cases & Gotchas

### NVIDIA NVAPI P-States & Power Limits
- GPU clock offsets are applied via NVAPI P-States 2.0 (`NvAPI_GPU_SetPstates20`) targeting the P0 performance state.
- GPU Power limits are applied via NVML (`nvmlDeviceSetPowerManagementLimit`) in milliwatts.
- In mobile GPUs or laptop chassis where the vendor has locked P-state frequency offsets in VBIOS, DynoFanControl safely falls back to standard limits without crashing.

### RivaTuner Statistics Server (RTSS) Shared Memory Protocol
- Communication occurs via the `RTSSSharedMemoryV2` (and fallback `RTSSSharedMemory`) memory-mapped file.
- DynoFanControl identifies itself as `DynoFanControl` in the owner table and writes formatted text at `dwOSDArrOffset + 256`.
- RTSS must be installed and running in the background for in-game rendering; DynoFanControl monitors RTSS status in real-time and provides a live preview in the GUI.

### Desktop Super I/O LPC Bus Support
- Desktop motherboards do not expose standard ACPI EC registers. Instead, fan headers are wired to LPC Super I/O chips (Nuvoton NCT6779D/6791D/6796D/6798D or ITE IT8620E/IT8686E/IT8728F).
- `SuperIoFanDriver` binds to LibreHardwareMonitor's `SensorType.Control` (`IControl.SetSoftware()`), providing native fan control for desktop chassis fans and CPU AIO coolers.

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

### Configuration & Settings
- **Global Preset Persistence**: Selected preset modes (`AUTO`, `SILENT`, `BALANCED`, `CURVE`, `TURBO`, `CUSTOM`) and curve profiles are automatically persisted to global multi-tier storage (`%ProgramData%\DynoFanControl\config.json`, `%LocalAppData%\DynoFanControl\config.json`, and portable `config.json`). The chosen preset is retained across computer restarts and immediately highlighted in the UI and system tray.
- **Visual Preset Status**: Top header displays a dynamic preset badge (e.g. `⚡ BALANCED`, `📈 CURVE (Gaming)`), while mode buttons visually highlight active states with glowing borders and accent colors.
- **Dual-Redundancy Windows Auto-Start**: Automatically enabled by default. Configures Windows Task Scheduler with highest logon privileges (`/rl highest`) to ensure silent boot without UAC prompts, combined with an `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` backup.
- **Target GPU Selection**: In multi-GPU systems (e.g. laptop APU + discrete GPU or multi-card desktops), select which GPU drives the GPU fan curve in the Settings modal (`⚙`).
- **Independent vs. Synced Curves**: CPU and GPU curves operate independently by default. Enable `Sync CPU & GPU Curves` if you prefer unified fan speed ramping.
- **Overheat Guard**: Configurable thermal trigger (75°C - 95°C) that automatically overrides manual settings to 100% cooling.

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
