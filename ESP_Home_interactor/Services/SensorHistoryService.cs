using Microsoft.Extensions.Hosting;

namespace ESP_Home_Interactor.Services;

/// <summary>
/// Samples temperature and humidity of all AC Infinity controllers once per
/// minute into an in-memory ring buffer (48h). Offline controllers produce
/// no sample, which renders as a gap in the history charts.
/// </summary>
public class SensorHistoryService : BackgroundService
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan OnlineThreshold = TimeSpan.FromMinutes(2);
    private const int MaxSamples = 48 * 60;

    private readonly AcInfinityService _acService;
    private readonly Dictionary<string, List<HistorySample>> _history = new();
    private readonly object _lock = new();

    public SensorHistoryService(AcInfinityService acService)
    {
        _acService = acService;
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

    private void Sample()
    {
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
                    controller.Temperature.Value, controller.Humidity.Value));
                if (samples.Count > MaxSamples)
                    samples.RemoveAt(0);
            }
        }
    }
}

public record HistorySample(DateTime Time, double Temperature, double Humidity);
