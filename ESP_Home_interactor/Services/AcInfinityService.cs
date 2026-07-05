using ESP_Home_Interactor.AcInfinity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ESP_Home_Interactor.Services;

/// <summary>
/// Manages AC Infinity controllers behind an ESPHome Bluetooth proxy.
/// Subscribes to BLE advertisements whenever the proxy connection is up
/// (state arrives passively) and exposes the controllers for the GUI.
/// </summary>
public class AcInfinityService : BackgroundService
{
    private readonly ILogger<AcInfinityService> _logger;
    private readonly EspDeviceService _espService;
    private readonly IConfiguration _configuration;
    private readonly List<AcInfinityController> _controllers = new();

    public IReadOnlyList<AcInfinityController> Controllers => _controllers.AsReadOnly();

    /// <summary>Host of the BLE proxy; the GUI hides this device's own card</summary>
    public string? ProxyHost { get; private set; }

    public event Action? OnControllersUpdated;

    public AcInfinityService(ILogger<AcInfinityService> logger, EspDeviceService espService,
        IConfiguration configuration)
    {
        _logger = logger;
        _espService = espService;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var configPath = _configuration["EspConfigPath"] ??
                             "/Users/marc/git/private/HomePeter/ESP_Home_interactor/config.json";
            var config = (await Config.Config.Read(configPath)).AcInfinity;

            if (config == null)
            {
                _logger.LogInformation("No AcInfinity section in config - service disabled");
                return;
            }

            ProxyHost = config.ProxyHost;
            var proxy = await WaitForProxy(config.ProxyHost, stoppingToken);
            _logger.LogInformation("AC Infinity service using proxy {Host}", config.ProxyHost);

            BleProxyClient? subscriptionClient = null;
            foreach (var controllerConfig in config.Controllers)
            {
                var ble = new BleProxyClient(proxy, controllerConfig.Mac);
                subscriptionClient ??= ble;
                var controller = new AcInfinityController(ble, controllerConfig.Name,
                    controllerConfig.Ports, controllerConfig.Type);
                controller.StateChanged += NotifyUpdated;
                _controllers.Add(controller);
            }

            NotifyUpdated();

            foreach (var controller in _controllers)
            {
                _ = InitialStateQuery(controller, stoppingToken);
            }

            // Re-subscribe to advertisements after every proxy (re)connect
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!proxy.IsConnected)
                {
                    await Task.Delay(2000, stoppingToken);
                    continue;
                }

                try
                {
                    await subscriptionClient!.SubscribeAdvertisements();
                    await proxy.WaitForDisconnect(stoppingToken);
                    _logger.LogWarning("Proxy {Host} disconnected - waiting for reconnect", config.ProxyHost);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError("Advertisement subscription failed: {Message}", ex.Message);
                    await Task.Delay(5000, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AC Infinity service error");
        }
    }

    /// <summary>
    /// Query current levels of all configured ports once the controller type
    /// is known (first advertisement received).
    /// </summary>
    private async Task InitialStateQuery(AcInfinityController controller, CancellationToken stoppingToken)
    {
        try
        {
            while (controller.Type == null)
                await Task.Delay(1000, stoppingToken);

            foreach (var port in controller.Ports)
            {
                try
                {
                    await controller.UpdateAsync(port.Port);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Initial state query for {Name} port {Port} failed: {Message}",
                        controller.Name, port.Port, ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown
        }
    }

    private async Task<EspBase> WaitForProxy(string proxyHost, CancellationToken stoppingToken)
    {
        while (true)
        {
            var proxy = _espService.Devices.FirstOrDefault(d => d.Host == proxyHost);
            if (proxy != null) return proxy;
            await Task.Delay(1000, stoppingToken);
        }
    }

    private void NotifyUpdated()
    {
        OnControllersUpdated?.Invoke();
    }
}
