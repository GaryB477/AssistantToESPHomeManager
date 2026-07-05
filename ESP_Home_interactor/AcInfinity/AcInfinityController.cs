using ESP_Home_Interactor.helper;

namespace ESP_Home_Interactor.AcInfinity;

/// <summary>
/// One AC Infinity controller behind a Bluetooth proxy.
/// State (temperature, humidity, vpd) arrives passively via BLE advertisements.
/// Commands connect on demand and disconnect right away, because the
/// controller only accepts a single BLE connection (phone app!).
/// </summary>
public class AcInfinityController
{
    private readonly Logger _logger = new();
    private readonly BleProxyClient _ble;
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private int _sequence;

    public string Name { get; }
    public string Mac => _ble.Mac;
    public IReadOnlyList<AcInfinityPortConfig> Ports { get; }

    /// <summary>Controller type byte; parsed from advertisements, needed for multi-port commands</summary>
    public int? Type { get; private set; }

    public double? Temperature { get; private set; }
    public double? Humidity { get; private set; }
    public double? Vpd { get; private set; }
    public DateTime? LastSeen { get; private set; }

    /// <summary>Last level set per port (advertisements only carry the display-selected port)</summary>
    public Dictionary<int, int> PortLevels { get; } = new();

    public event Action? StateChanged;

    public AcInfinityController(BleProxyClient ble, string name, IReadOnlyList<AcInfinityPortConfig> ports,
        int? configuredType)
    {
        _ble = ble;
        Name = name;
        Ports = ports;
        Type = configuredType;
        _ble.AdvertisementReceived += OnAdvertisement;
    }

    private void OnAdvertisement(byte[] manufacturerData)
    {
        var adv = AcInfinityProtocol.ParseManufacturerData(manufacturerData);
        if (adv == null) return;

        if (Type == null)
        {
            Type = adv.Type;
            _logger.Log($"{Name}: detected type {adv.Type}, version {adv.Version}, name '{adv.Name}'");
        }

        Temperature = adv.Temperature;
        Humidity = adv.Humidity;
        Vpd = adv.Vpd ?? Vpd;
        LastSeen = DateTime.Now;
        StateChanged?.Invoke();
    }

    private int NextSequence()
    {
        if (_sequence >= 65535) _sequence = 0;
        return ++_sequence;
    }

    /// <summary>
    /// Set the fan level (0-10) of one port. Level 0 turns the port off.
    /// </summary>
    public async Task SetLevelAsync(int port, int level)
    {
        if (Type == null)
            throw new InvalidOperationException(
                $"{Name}: controller type unknown - no advertisement received yet");

        var workType = level > 0 ? 2 : 1;

        await _commandLock.WaitAsync();
        try
        {
            await _ble.ConnectAsync();
            var frame = AcInfinityProtocol.BuildSetLevel(Type.Value, workType, level, port, NextSequence());
            var response = await _ble.WriteAndAwaitNotification(frame);
            _logger.LogSuccess($"{Name}: port {port} set to level {level} " +
                               $"(response {Convert.ToHexString(response)})");
            PortLevels[port] = level;
            StateChanged?.Invoke();
        }
        finally
        {
            await _ble.DisconnectAsync();
            _commandLock.Release();
        }
    }

    /// <summary>
    /// Query work type and levels of one port from the controller.
    /// </summary>
    public async Task UpdateAsync(int port)
    {
        if (Type == null)
            throw new InvalidOperationException(
                $"{Name}: controller type unknown - no advertisement received yet");

        await _commandLock.WaitAsync();
        try
        {
            await _ble.ConnectAsync();
            var frame = AcInfinityProtocol.BuildGetModelData(Type.Value, port, NextSequence());
            var data = await _ble.WriteAndAwaitNotification(frame);

            // Offsets from hunterjm/ac-infinity-ble device.update()
            if (data.Length >= 19)
            {
                var workType = data[12];
                var levelOff = data[15];
                var levelOn = data[18];
                PortLevels[port] = workType == 2 ? levelOn : levelOff;
                _logger.LogIncoming($"{Name}: port {port} workType={workType} " +
                                    $"levelOff={levelOff} levelOn={levelOn}");
                StateChanged?.Invoke();
            }
            else if (data.Length == 0)
            {
                // Controller 69 firmware does not answer the state query - the
                // last commanded level in PortLevels stays authoritative
                _logger.Log($"{Name}: port {port} state query not answered by firmware");
            }
            else
            {
                _logger.LogWarning($"{Name}: unexpected model data response " +
                                   $"({data.Length} bytes): {Convert.ToHexString(data)}");
            }
        }
        finally
        {
            await _ble.DisconnectAsync();
            _commandLock.Release();
        }
    }
}
