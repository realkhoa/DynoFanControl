using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;

namespace DynoFanControl.Services.Osd;

public sealed class OsdSettings
{
    public bool Enabled { get; set; } = false;
    public bool ShowGpuTemp { get; set; } = true;
    public bool ShowGpuLoad { get; set; } = true;
    public bool ShowGpuClocks { get; set; } = true;
    public bool ShowGpuPower { get; set; } = true;
    public bool ShowGpuVram { get; set; } = true;
    public bool ShowGpuFanRpm { get; set; } = true;
    public bool ShowCpuTemp { get; set; } = true;
    public bool ShowCpuLoad { get; set; } = true;
    public bool ShowCpuPower { get; set; } = true;
    public bool ShowCpuFanRpm { get; set; } = true;
    public bool ShowRamUsage { get; set; } = true;
    public bool ShowActivePreset { get; set; } = true;
}

public class OsdTelemetrySnapshot
{
    public string GpuName { get; set; } = "GPU";
    public double GpuTemp { get; set; }
    public uint GpuLoad { get; set; }
    public uint GpuCoreClockMhz { get; set; }
    public uint GpuMemoryClockMhz { get; set; }
    public double GpuPowerWatts { get; set; }
    public double GpuVramUsedMb { get; set; }
    public double GpuVramTotalMb { get; set; }
    public uint GpuFanRpm { get; set; }

    public string CpuName { get; set; } = "CPU";
    public double CpuTemp { get; set; }
    public double CpuLoad { get; set; }
    public double CpuPowerWatts { get; set; }
    public uint CpuFanRpm { get; set; }

    public double RamUsedGb { get; set; }
    public double RamTotalGb { get; set; }

    public string ActivePreset { get; set; } = "Auto";
}

/// <summary>
/// RivaTuner Statistics Server (RTSS) OSD Integration Service.
/// Interacts directly with the RTSS Shared Memory mapped file protocol (RTSSSharedMemoryV2)
/// to render rich in-game overlays for DirectX, Vulkan, and OpenGL titles without hooking DirectX.
/// </summary>
public sealed class RtssOsdService : IDisposable
{
    private static readonly Lazy<RtssOsdService> _instance = new(() => new RtssOsdService());
    public static RtssOsdService Instance => _instance.Value;

    private const string MapNameV2 = "RTSSSharedMemoryV2";
    private const string MapNameV1 = "RTSSSharedMemory";
    private const uint RTSS_SIGNATURE = 0x53535452; // 'R' 'T' 'S' 'S'
    private const string AppOwner = "DynoFanControl";

    private readonly object _lock = new();
    private bool _isRtssRunning;
    private string _lastOsdPreview = "";

    public bool IsRtssRunning => _isRtssRunning;
    public string LastOsdPreview => _lastOsdPreview;

    public bool UpdateOsd(OsdSettings settings, OsdTelemetrySnapshot snapshot)
    {
        if (!settings.Enabled)
        {
            ClearOsd();
            return false;
        }

        var osdText = BuildOsdText(settings, snapshot);
        _lastOsdPreview = osdText;

        lock (_lock)
        {
            try
            {
                MemoryMappedFile? mmf = null;
                try
                {
                    mmf = MemoryMappedFile.OpenExisting(MapNameV2, MemoryMappedFileRights.ReadWrite);
                }
                catch (FileNotFoundException)
                {
                    try
                    {
                        mmf = MemoryMappedFile.OpenExisting(MapNameV1, MemoryMappedFileRights.ReadWrite);
                    }
                    catch (FileNotFoundException)
                    {
                        _isRtssRunning = false;
                        return false;
                    }
                }

                using (mmf)
                using (var view = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite))
                {
                    uint sig = view.ReadUInt32(0);
                    if (sig != RTSS_SIGNATURE)
                    {
                        _isRtssRunning = false;
                        return false;
                    }

                    _isRtssRunning = true;
                    uint ver = view.ReadUInt32(4);
                    if (ver < 0x00020000) return false;

                    uint osdEntrySize = view.ReadUInt32(20);
                    uint osdArrOffset = view.ReadUInt32(24);
                    uint osdArrSize   = view.ReadUInt32(28);

                    if (osdArrSize == 0 || osdEntrySize == 0) return false;

                    // Locate or acquire slot
                    long targetEntryOffset = -1;
                    byte[] ownerBuf = new byte[256];

                    for (uint i = 0; i < osdArrSize; i++)
                    {
                        long entryOffset = osdArrOffset + (i * osdEntrySize);
                        view.ReadArray(entryOffset, ownerBuf, 0, 256);
                        string owner = Encoding.ASCII.GetString(ownerBuf).TrimEnd('\0').Trim();

                        if (owner == AppOwner || string.IsNullOrEmpty(owner))
                        {
                            targetEntryOffset = entryOffset;
                            break;
                        }
                    }

                    if (targetEntryOffset == -1)
                        targetEntryOffset = osdArrOffset; // Default to first slot

                    // Write Owner
                    byte[] appOwnerBytes = Encoding.ASCII.GetBytes(AppOwner + "\0");
                    view.WriteArray(targetEntryOffset, appOwnerBytes, 0, Math.Min(appOwnerBytes.Length, 256));

                    // Write OSD String (offset + 256)
                    byte[] textBytes = Encoding.UTF8.GetBytes(osdText + "\0");
                    long textOffset = targetEntryOffset + 256;
                    int maxLen = (int)Math.Min(textBytes.Length, osdEntrySize - 256);
                    view.WriteArray(textOffset, textBytes, 0, maxLen);

                    // Increment frame counter to trigger re-render
                    uint frame = view.ReadUInt32(32);
                    view.Write(32, frame + 1);

                    return true;
                }
            }
            catch
            {
                _isRtssRunning = false;
                return false;
            }
        }
    }

    public void ClearOsd()
    {
        lock (_lock)
        {
            try
            {
                using var mmf = MemoryMappedFile.OpenExisting(MapNameV2, MemoryMappedFileRights.ReadWrite);
                using var view = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);

                uint sig = view.ReadUInt32(0);
                if (sig != RTSS_SIGNATURE) return;

                uint osdEntrySize = view.ReadUInt32(20);
                uint osdArrOffset = view.ReadUInt32(24);
                uint osdArrSize   = view.ReadUInt32(28);

                byte[] ownerBuf = new byte[256];
                for (uint i = 0; i < osdArrSize; i++)
                {
                    long entryOffset = osdArrOffset + (i * osdEntrySize);
                    view.ReadArray(entryOffset, ownerBuf, 0, 256);
                    string owner = Encoding.ASCII.GetString(ownerBuf).TrimEnd('\0').Trim();

                    if (owner == AppOwner)
                    {
                        byte[] zero = new byte[osdEntrySize];
                        view.WriteArray(entryOffset, zero, 0, zero.Length);

                        uint frame = view.ReadUInt32(32);
                        view.Write(32, frame + 1);
                        break;
                    }
                }
            }
            catch { }
        }
    }

    private string BuildOsdText(OsdSettings s, OsdTelemetrySnapshot t)
    {
        var sb = new StringBuilder();

        // GPU Line
        sb.Append("<C=00E5FF>GPU<C> ");
        if (s.ShowGpuTemp) sb.Append($"<C=FFD600>{t.GpuTemp:F0}°C<C>  ");
        if (s.ShowGpuLoad) sb.Append($"<C=76FF03>{t.GpuLoad}%<C>  ");
        if (s.ShowGpuClocks && t.GpuCoreClockMhz > 0) sb.Append($"{t.GpuCoreClockMhz}MHz  ");
        if (s.ShowGpuVram && t.GpuVramUsedMb > 0) sb.Append($"{t.GpuVramUsedMb:F0}MB  ");
        if (s.ShowGpuPower && t.GpuPowerWatts > 0) sb.Append($"{t.GpuPowerWatts:F1}W  ");
        if (s.ShowGpuFanRpm && t.GpuFanRpm > 0) sb.Append($"{t.GpuFanRpm}RPM");
        sb.AppendLine();

        // CPU Line
        sb.Append("<C=FF5252>CPU<C> ");
        if (s.ShowCpuTemp) sb.Append($"<C=FFD600>{t.CpuTemp:F0}°C<C>  ");
        if (s.ShowCpuLoad && t.CpuLoad > 0) sb.Append($"<C=76FF03>{t.CpuLoad:F0}%<C>  ");
        if (s.ShowCpuPower && t.CpuPowerWatts > 0) sb.Append($"{t.CpuPowerWatts:F1}W  ");
        if (s.ShowCpuFanRpm && t.CpuFanRpm > 0) sb.Append($"{t.CpuFanRpm}RPM");
        sb.AppendLine();

        // System / RAM Line
        if (s.ShowRamUsage && t.RamUsedGb > 0)
        {
            sb.Append($"<C=B0BEC5>RAM<C> {t.RamUsedGb:F1}GB / {t.RamTotalGb:F1}GB  ");
        }
        if (s.ShowActivePreset)
        {
            sb.Append($"<C=00E5FF>[{t.ActivePreset.ToUpperInvariant()}]<C>");
        }

        return sb.ToString().TrimEnd();
    }

    public void Dispose()
    {
        ClearOsd();
    }
}
