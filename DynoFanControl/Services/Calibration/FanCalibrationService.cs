using System.Collections.ObjectModel;
using DynoFanControl.Hardware;

namespace DynoFanControl.Services.Calibration;

public class CalibrationResultPoint
{
    public int DutyCyclePercent { get; set; }
    public uint CpuRpm { get; set; }
    public uint GpuRpm { get; set; }
    public string FormattedCpu => $"{CpuRpm:N0} RPM";
    public string FormattedGpu => $"{GpuRpm:N0} RPM";
}

/// <summary>
/// Diagnostic Fan Speed & Acoustic Calibration Benchmarker.
/// Sweeps through PWM duty cycles (30% to 100%) to measure real-world tachometer curves,
/// detect dust clogging or worn bearings, and calibrate RPM responsiveness.
/// </summary>
public sealed class FanCalibrationService
{
    private static readonly Lazy<FanCalibrationService> _instance = new(() => new FanCalibrationService());
    public static FanCalibrationService Instance => _instance.Value;

    private readonly object _lock = new();
    private bool _isRunning;
    private int _currentProgressPercent;
    private string _currentStatus = "Ready to Calibrate";
    private CancellationTokenSource? _cts;

    public bool IsRunning => _isRunning;
    public int CurrentProgressPercent => _currentProgressPercent;
    public string CurrentStatus => _currentStatus;
    public ObservableCollection<CalibrationResultPoint> Results { get; } = new();

    public event Action<int, string>? ProgressUpdated;
    public event Action<bool, string>? CalibrationCompleted;

    private FanCalibrationService() { }

    public async Task StartCalibrationAsync(string initialMode, int initialCpuSpeed, int initialGpuSpeed)
    {
        lock (_lock)
        {
            if (_isRunning) return;
            _isRunning = true;
            _currentProgressPercent = 0;
            _currentStatus = "Starting calibration sweep...";
            _cts = new CancellationTokenSource();
            Results.Clear();
        }

        var token = _cts.Token;

        try
        {
            int[] testSteps = new[] { 30, 40, 50, 60, 70, 80, 90, 100 };
            int totalSteps = testSteps.Length;

            for (int i = 0; i < totalSteps; i++)
            {
                if (token.IsCancellationRequested) break;

                int pct = testSteps[i];
                _currentStatus = $"Testing duty cycle {pct}% ({i + 1}/{totalSteps})...";
                _currentProgressPercent = (int)((i / (double)totalSteps) * 100);
                ProgressUpdated?.Invoke(_currentProgressPercent, _currentStatus);

                // Set hardware fan speed
                FanHardwareManager.Instance.SetCpuFan(false, pct);
                FanHardwareManager.Instance.SetGpuFan(false, pct);

                // Wait 3.5 seconds for fans to spin up to steady-state velocity
                await Task.Delay(3500, token);

                // Sample 3 readings to obtain a smooth average
                uint cpuSum = 0;
                uint gpuSum = 0;
                int samples = 3;

                for (int s = 0; s < samples; s++)
                {
                    if (token.IsCancellationRequested) break;
                    var reading = FanHardwareManager.Instance.PollHardware();
                    cpuSum += reading.CpuRpm;
                    gpuSum += reading.GpuRpm;
                    await Task.Delay(500, token);
                }

                uint avgCpu = cpuSum / (uint)samples;
                uint avgGpu = gpuSum / (uint)samples;

                App.Current?.Dispatcher.Invoke(() =>
                {
                    Results.Add(new CalibrationResultPoint
                    {
                        DutyCyclePercent = pct,
                        CpuRpm = avgCpu,
                        GpuRpm = avgGpu
                    });
                });
            }

            if (!token.IsCancellationRequested)
            {
                _currentProgressPercent = 100;
                string healthMsg = AnalyzeHealth();
                _currentStatus = healthMsg;
                CalibrationCompleted?.Invoke(true, healthMsg);
            }
            else
            {
                _currentStatus = "Calibration Cancelled";
                CalibrationCompleted?.Invoke(false, "Cancelled by user");
            }
        }
        catch (OperationCanceledException)
        {
            _currentStatus = "Calibration Cancelled";
            CalibrationCompleted?.Invoke(false, "Cancelled by user");
        }
        catch (Exception ex)
        {
            _currentStatus = $"Calibration error: {ex.Message}";
            CalibrationCompleted?.Invoke(false, ex.Message);
        }
        finally
        {
            // Restore previous fan state
            if (initialMode == "Auto")
            {
                FanHardwareManager.Instance.SetCpuFan(true, 0);
                FanHardwareManager.Instance.SetGpuFan(true, 0);
            }
            else
            {
                FanHardwareManager.Instance.SetCpuFan(false, initialCpuSpeed);
                FanHardwareManager.Instance.SetGpuFan(false, initialGpuSpeed);
            }

            lock (_lock)
            {
                _isRunning = false;
                _cts?.Dispose();
                _cts = null;
            }
        }
    }

    public void Cancel()
    {
        lock (_lock)
        {
            _cts?.Cancel();
        }
    }

    private string AnalyzeHealth()
    {
        if (Results.Count == 0) return "No calibration data collected";

        var max100 = Results.FirstOrDefault(r => r.DutyCyclePercent == 100);
        if (max100 == null) return "Calibration Complete";

        if (max100.CpuRpm == 0 && max100.GpuRpm == 0)
            return "Notice: Tachometers not reporting (EC direct control active)";

        if (max100.CpuRpm > 0 && max100.CpuRpm < 2500)
            return $"Warning: CPU Max Fan Speed low ({max100.CpuRpm} RPM). Check for dust accumulation.";

        return $"Calibration Successful! Peak RPM: CPU {max100.CpuRpm:N0} RPM | GPU {max100.GpuRpm:N0} RPM";
    }
}
