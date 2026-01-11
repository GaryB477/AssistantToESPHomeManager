using ESP_Home_Interactor.Config;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace ESP_Home_Interactor.Services;

/// <summary>
/// Background service that manages ESP device connections and state updates
/// </summary>
public class EspDeviceService : BackgroundService
{
    private readonly ILogger<EspDeviceService> _logger;
    private readonly IConfiguration _configuration;
    private List<EspBase> _espDevices = new();

    public event Action? OnDevicesUpdated;

    public IReadOnlyList<EspBase> Devices => _espDevices.AsReadOnly();

    public EspDeviceService(ILogger<EspDeviceService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ESP Device Service starting...");

        try
        {
            // Load configuration
            var configPath = _configuration["EspConfigPath"] ??
                            "/Users/marc/git/private/HomePeter/ESP_Home_interactor/config.json";
            var espConfigs = await Config.Config.Read(configPath);

            // Initialize devices
            _espDevices = espConfigs.ESPNode.Select(esp => new EspBase(esp)).ToList();

            // Connect to all devices
            foreach (var esp in _espDevices)
            {
                try
                {
                    _logger.LogInformation("Connecting to {Host}...", esp.Host);
                    await esp.Init();
                    await esp.Sync();
                    _logger.LogInformation("Connected to {Host}", esp.Host);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to connect to {Host}", esp.Host);
                }
            }

            OnDevicesUpdated?.Invoke();

            // Periodic refresh loop - only update states, don't send commands
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(10000, stoppingToken); // Update every 10 seconds

                foreach (var esp in _espDevices)
                {
                    // Only sync if connection is established
                    if (esp.Connection == null)
                    {
                        // Try to reconnect once per minute
                        continue;
                    }

                    try
                    {
                        await esp.Sync();
                        _logger.LogDebug("Synced state from {Host}", esp.Host);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to sync with {Host}", esp.Host);
                    }
                }

                OnDevicesUpdated?.Invoke();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ESP Device Service error");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ESP Device Service stopping...");

        foreach (var esp in _espDevices)
        {
            try
            {
                await esp.Cleanup();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up {Host}", esp.Host);
            }
        }

        await base.StopAsync(cancellationToken);
    }
}
