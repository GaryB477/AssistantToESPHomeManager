using Microsoft.Extensions.Hosting;

namespace ESP_Home_Interactor.Services;

/// <summary>
/// Samples climate values of all AC Infinity controllers plus the effective
/// light level once per minute into an in-memory ring buffer (7 days). Offline
/// controllers produce no sample, which renders as a gap in the history charts.
/// </summary>
public class SensorHistoryService : BackgroundService
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan OnlineThreshold = TimeSpan.FromMinutes(2);
    private const int MaxSamples = 7 * 24 * 60;

    private readonly AcInfinityService _acService;
    private readonly EspDeviceService _espService;
    private readonly Dictionary<string, List<HistorySample>> _history = new();
    private readonly object _lock = new();

    public SensorHistoryService(AcInfinityService acService, EspDeviceService espService)
    {
        _acService = acService;
        _espService = espService;
    }

    /// <summary>Snapshot of the recorded samples for one controller, oldest first</summary>
    public IReadOnlyList<HistorySample> GetHistory(string controllerName)
    {
        lock (_lock)
        {
            return _history.TryGetValue(controllerName, out var samples)
                ? samples.ToList()
                : Array.Empty<HistorySample>();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                Sample();
                await Task.Delay(SampleInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown
        }
    }

    /// <summary>
    /// Effective light intensity 0.0-1.0, taken from the lamps' reported state
    /// (not the cycle target). Highest intensity wins when there are several
    /// lights; null when no connected light has reported a state yet.
    /// </summary>
    private double? CurrentLightLevel()
    {
        var lights = _espService.Devices
            .Where(d => d.IsConnected)
            .SelectMany(d => d.LightEntities)
            .Where(l => l.HasState)
            .ToList();
        if (lights.Count == 0) return null;

        return lights.Max(l => l.GetValue() ? (l.SupportsBrightness ? l.Brightness : 1f) : 0f);
    }

    private void Sample()
    {
        var light = CurrentLightLevel();

        foreach (var controller in _acService.Controllers)
        {
            var online = controller.LastSeen.HasValue &&
                         DateTime.Now - controller.LastSeen.Value < OnlineThreshold;
            if (!online || controller.Temperature == null || controller.Humidity == null)
                continue;

            lock (_lock)
            {
                if (!_history.TryGetValue(controller.Name, out var samples))
                {
                    samples = new List<HistorySample>();
                    _history[controller.Name] = samples;
                }

                samples.Add(new HistorySample(DateTime.Now,
                    controller.Temperature.Value, controller.Humidity.Value,
                    controller.Vpd, light));
                if (samples.Count > MaxSamples)
                    samples.RemoveAt(0);
            }
        }
    }
}

public record HistorySample(DateTime Time, double Temperature, double Humidity, double? Vpd, double? Light);
