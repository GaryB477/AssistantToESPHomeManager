namespace ESP_Home_Interactor.AcInfinity;

/// <summary>AC Infinity section of config.json</summary>
public class AcInfinityConfig
{
    /// <summary>Host of the ESPHome Bluetooth proxy (must also be listed as ESPNode)</summary>
    public required string ProxyHost { get; init; }

    public required AcInfinityControllerConfig[] Controllers { get; init; }
}

public class AcInfinityControllerConfig
{
    public required string Name { get; init; }

    /// <summary>BLE MAC address, e.g. "18:8B:0E:F2:4C:32"</summary>
    public required string Mac { get; init; }

    /// <summary>Controller type byte; normally auto-detected from advertisements</summary>
    public int? Type { get; init; }

    public AcInfinityPortConfig[] Ports { get; init; } = Array.Empty<AcInfinityPortConfig>();
}

public class AcInfinityPortConfig
{
    public required int Port { get; init; }
    public required string Name { get; init; }
}
