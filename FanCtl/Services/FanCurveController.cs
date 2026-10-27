namespace FanCtl.Services;

/// <summary>
/// Controls fan speeds dynamically based on temperature curves with hysteresis smoothing.
/// Prevents irritating fan pulsing and protects hardware smoothly.
/// </summary>
public class FanCurveController
{
    private double _smoothedTemp = 45.0;
    private int _currentSpeed = 30;
    private DateTime _lastSpeedChange = DateTime.MinValue;

    public static readonly List<CurvePoint> SilentCurve = new()
    {
        new(45, 0),
        new(55, 20),
        new(65, 40),
        new(75, 60),
        new(85, 85),
        new(90, 100)
    };

    public static readonly List<CurvePoint> BalancedCurve = new()
    {
        new(40, 20),
        new(50, 35),
        new(60, 50),
        new(72, 70),
        new(82, 90),
        new(88, 100)
    };

    public static readonly List<CurvePoint> GamingCurve = new()
    {
        new(40, 35),
        new(50, 55),
        new(60, 75),
        new(70, 90),
        new(78, 100)
    };

    /// <summary>
    /// Calculates fan speed percentage for a given peak temperature based on curve points.
    /// Incorporates temperature smoothing and a down-ramp delay.
    /// </summary>
    public int EvaluateSpeed(double rawPeakTemp, IList<CurvePoint> points)
    {
        if (points == null || points.Count == 0) return 50;

        // Sort points by temperature
        var sorted = points.OrderBy(p => p.Temperature).ToList();

        // Exponential moving average to eliminate sudden micro-spikes (e.g. background thread burst)
        _smoothedTemp = (_smoothedTemp * 0.7) + (rawPeakTemp * 0.3);

        // Calculate target speed from curve
        int targetSpeed;
        if (_smoothedTemp <= sorted[0].Temperature)
        {
            targetSpeed = sorted[0].FanSpeed;
        }
        else if (_smoothedTemp >= sorted[^1].Temperature)
        {
            targetSpeed = sorted[^1].FanSpeed;
        }
        else
        {
            targetSpeed = sorted[0].FanSpeed;
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                var p1 = sorted[i];
                var p2 = sorted[i + 1];
                if (_smoothedTemp >= p1.Temperature && _smoothedTemp <= p2.Temperature)
                {
                    double fraction = (_smoothedTemp - p1.Temperature) / (double)(p2.Temperature - p1.Temperature);
                    targetSpeed = (int)Math.Round(p1.FanSpeed + (fraction * (p2.FanSpeed - p1.FanSpeed)));
                    break;
                }
            }
        }

        targetSpeed = Math.Clamp(targetSpeed, 0, 100);

        // Hysteresis: If temperature rises, ramp UP fans immediately.
        // If temperature drops, hold speed for at least 4 seconds before lowering to prevent fan pulsing.
        var now = DateTime.Now;
        if (targetSpeed > _currentSpeed)
        {
            _currentSpeed = targetSpeed;
            _lastSpeedChange = now;
        }
        else if (targetSpeed < _currentSpeed)
        {
            if ((now - _lastSpeedChange).TotalSeconds >= 4)
            {
                _currentSpeed = targetSpeed;
                _lastSpeedChange = now;
            }
        }

        return _currentSpeed;
    }
}
