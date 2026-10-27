# DynoFanControl — Acer Nitro 5 (AN515-45) Fan Controller

A lightweight, high-performance, dark-themed fan control and thermal monitoring utility for the **Acer Nitro 5 (AN515-45)** laptop family running Windows 10/11.

---

## 1. Purpose & Scope

Acer Nitro laptops often suffer from bloated, heavy background services (such as NitroSense / PredatorSense) or lack proper fan control after installing clean Windows without proprietary OEM software. DynoFanControl solves this problem by directly interfacing with the ACPI Embedded Controller (EC) and Windows Management Instrumentation (WMI) to provide:

- **Ultra-fast hardware response** using native WinRing0 kernel driver IOCTLs (<0.1ms latency) with automatic fallback to `ec-probe.exe`.
- **Advanced Multi-Sensor Telemetry**: Direct native NVIDIA Management Library (`nvml.dll`) integration for discrete GPU core temp, utilization (%), and power draw (W), combined with `LibreHardwareMonitorLib` for AMD Ryzen 5 5600H CPU package/cores, integrated Radeon APU, NVMe SSD storage, and ACPI thermal zones.
- **Dual fan control**: Independent or linked CPU and GPU fan speed regulation (Auto, Silent 30%, Balanced 60%, Custom Manual, or Max Turbo).
- **Acer CoolBoost™ integration**: Direct EC register manipulation for increased fan speed ceiling.
- **Smart Temperature Curves (Independent or Synced)**: Separate, independent curve controllers for CPU (`CpuTemp`, AMD Ryzen 5 5600H) and GPU (`GpuTemp`, NVIDIA GeForce GTX 1650). GPU fan stays whisper-quiet during CPU-heavy compilation tasks, and CPU fan stays calm during GPU-only gaming. An optional sync toggle is provided for users who prefer unified curve scaling.
- **Hardware Telemetry**: Real-time CPU and GPU temperatures alongside live fan tachometers (RPM).
- **Overheat Guard Watchdog**: Automatic 100% fan speed engagement if temperatures exceed user-configured thresholds (e.g. 88°C).
- **System Tray Integration**: Minimize to tray, dynamic stat tooltips, right-click quick preset switcher, and single-instance mutex.
- **Elevated Windows Auto-start**: Optional silent boot-to-tray execution via Windows Task Scheduler without UAC prompts.
- **Safety First**: Guaranteed failsafe reset to BIOS automatic fan curves on application exit, system shutdown, or unhandled exceptions.

---

## 2. Architecture & Data Flow

DynoFanControl follows a decoupled, 4-tier architecture:

```
[ UI Layer (WPF / XAML) ]
        ▲   │
        │   ▼
[ ViewModels (FanViewModel / RelayCommand) ]
        ▲   │
        │   ▼
[ Services Layer (FanCurveController / StartupManager / TrayIconManager) ]
        ▲   │
        │   ▼
[ Hardware Abstraction Layer (FanHardwareManager) ]
        ├── DirectEcDriver (WinRing0 Kernel Driver IOCTLs: 0x9C4060CC / 0x9C40A0D8)
        ├── EcProbeFallback (ec-probe.exe CLI runner)
        ├── NvmlDriver (P/Invoke to native nvml.dll for NVIDIA dGPU)
        ├── SensorMonitorService (LibreHardwareMonitor + NVML + ACPI WMI)
        └── AcerWmi (root\wmi: AcerGamingFunction & MSAcpi_ThermalZoneTemperature)
```

### Data Flow
1. **Hardware Polling**: `FanHardwareManager` polls ACPI Thermal Zones, NVML native driver, and LibreHardwareMonitor for all sensors, and queries WMI `GetGamingFanSpeed` to retrieve live tachometer RPM readings.
2. **Curve Evaluation**: In "Curve" mode, dual `FanCurveController` instances calculate target fan percentages independently for CPU and GPU based on smoothed temperatures and active curve profiles.
3. **Safety Watchdog**: If peak temperatures exceed the overheat threshold, the watchdog temporarily overrides manual settings to force 100% cooling.
4. **Hardware Write**: Write commands are sent to `DirectEcDriver` (primary IOCTLs to `\\.\WinRing0_1_2_0`) or `EcProbeFallback` (fallback process).
5. **UI & Tray Update**: Reactive properties update the WPF window and system tray tooltip.

---

## 3. Usage & Features

### System Requirements
- **OS**: Windows 10 / Windows 11 (64-bit).
- **Hardware**: Acer Nitro 5 (specifically AN515-45 / AMD Ryzen + NVIDIA GeForce).
- **Privileges**: Administrator rights (UAC elevation requested via `app.manifest`).
- **Runtime**: .NET 9.0 Desktop Runtime.

### Operating Modes
| Mode | Description |
|---|---|
| **AUTO** | Restores native BIOS/EC fan control (registers `0x22 = 0x04`, `0x21 = 0x10`). |
| **SILENT** | Locks CPU and GPU fans to 30% for quiet office work or studying. |
| **BALANCED** | Sets fans to 60% for balanced cooling and moderate noise. |
| **TURBO** | Forces both fans to 100% and enables Acer CoolBoost™ for maximum gaming performance. |
| **SMART CURVE**| Dynamically adjusts fan speeds according to the temperature curve with 4-second downward hysteresis. |
| **CUSTOM** | Direct manual control via individual CPU / GPU sliders or quick chips (30%, 50%, 75%, 100%). |

### Keyboard & UI Controls
- **Link Fans**: Synchronizes manual CPU and GPU sliders so adjusting one moves both.
- **CoolBoost**: Toggles EC register `0x10` (`0x01` On / `0x00` Off).
- **Curves Panel (`📈 Curves`)**: Toggle between `[💻 CPU Fan Curve]` and `[🎮 GPU Fan Curve]` tabs with live target speed previews (e.g. `52°C ➔ 38%`), independent profile presets (Silent, Balanced, Gaming), and an optional `Sync CPU & GPU Curves` checkbox.
- **Sensors Panel (`🌡 Sensors`)**: Collapsible hardware drawer displaying live readings for all sensors detected by NVML, LibreHardwareMonitor, and ACPI.
- **Settings Panel (`⚙`)**: Toggle auto-start, start minimized, close to tray, and overheat threshold.

---

## 4. Technical Notes, Edge Cases & Gotchas

### Embedded Controller (EC) Register Map for AN515-45
| Register | Purpose | Typical Values |
|---|---|---|
| `0x10` | Acer CoolBoost | `0x01` = Enabled, `0x00` = Disabled |
| `0x21` | GPU Fan Mode Control | `0x30` (48) = Manual Control, `0x10` (16) = Auto (BIOS) |
| `0x22` | CPU Fan Mode Control | `0x0C` (12) = Manual Control, `0x04` (4) = Auto (BIOS) |
| `0x37` | CPU Fan Target Speed | `0` to `100` (%) |
| `0x3A` | GPU Fan Target Speed | `0` to `100` (%) |
| `0x13` | CPU Fan RPM Tachometer | Raw tachometer pulse counter |
| `0x15` | GPU Fan RPM Tachometer | Raw tachometer pulse counter |

### Driver & IO Port Access
- Standard ACPI EC uses ports `0x62` (Data) and `0x66` (Status/Command).
- WinRing0 IOCTL `0x9C4060CC` reads byte from IO port; `0x9C40A0D8` writes byte with `PortNumber` (uint) and `Value` (byte).
- Mutex synchronization (`_lock`) guarantees atomic EC read/write sequences, preventing interleaved commands from freezing the controller.

### Windows Task Scheduler Auto-Start
Standard `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` registry entries are blocked by Windows UAC on login for applications requiring Administrator privileges. DynoFanControl solves this by registering an elevated logon task via `schtasks.exe /rl highest`, allowing silent startup directly into the system tray without UAC prompts.

---

## 5. Building from Source

```powershell
# Build Debug
dotnet build DynoFanControl.sln

# Build Release x64 using MSBuild
msbuild DynoFanControl.sln /p:Configuration=Release /p:Platform=x64 /m /v:m

# Publish standalone/framework-dependent distribution
dotnet publish DynoFanControl\DynoFanControl.csproj -c Release -r win-x64 --self-contained false -o Publish
```
Output binaries are generated in `Publish\DynoFanControl.exe`.
