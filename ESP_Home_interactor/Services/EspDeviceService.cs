using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace ESP_Home_Interactor.Services;

/// <summary>
/// Background service that manages ESP device connections.
/// Maintains one connection per configured device with automatic
/// reconnect (exponential backoff). State updates arrive via the
/// devices' read loops and are forwarded through OnDevicesUpdated.
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
            var configPath = _configuration["EspConfigPath"] ??
                            "/Users/marc/git/private/HomePeter/ESP_Home_interactor/config.json";
            var espConfigs = await Config.Config.Read(configPath);

            _espDevices = espConfigs.ESPNode.Select(esp => new EspBase(esp)).ToList();

            foreach (var esp in _espDevices)
            {
                esp.StateChanged += NotifyDevicesUpdated;
            }

            await Task.WhenAll(_espDevices.Select(esp => ManageDevice(esp, stoppingToken)));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ESP Device Service error");
        }
    }

    /// <summary>
    /// Connect to a device and keep it connected: on connection loss,
    /// retry with exponential backoff (5s doubling up to 60s).
    /// </summary>
    private async Task ManageDevice(EspBase esp, CancellationToken stoppingToken)
    {
        var reconnectDelay = TimeSpan.FromSeconds(5);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Connecting to {Host}...", esp.Host);
                await esp.Init();
                _logger.LogInformation("Connected to {Host}", esp.Host);
                reconnectDelay = TimeSpan.FromSeconds(5);
                NotifyDevicesUpdated();

                await esp.WaitForDisconnect(stoppingToken);
                _logger.LogWarning("Disconnected from {Host}", esp.Host);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError("Connection to {Host} failed: {Message}", esp.Host, ex.Message);
            }

            NotifyDevicesUpdated();

            try
            {
                await Task.Delay(reconnectDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            reconnectDelay = TimeSpan.FromSeconds(Math.Min(reconnectDelay.TotalSeconds * 2, 60));
        }
    }

    private void NotifyDevicesUpdated()
    {
        OnDevicesUpdated?.Invoke();
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
