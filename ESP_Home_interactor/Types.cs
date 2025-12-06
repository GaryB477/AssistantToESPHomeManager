namespace ESP_Home_Interactor;

public struct ESPConfig
{
    public required string Host { get; set; }
    public required int Port { get; set; }
    public EntitySwitch[]? Switches { get; set; }
    // will implement other entities later
}

public struct EntityBinarySensor
{
    public required uint Key { get; set; }
    public required string Name { get; set; }
    public required string ObjectId { get; set; }
    public required string DeviceClass { get; set; }
    public required string Icon { get; set; }
}

public struct EntityCover
{
    public required uint Key { get; set; }
    public required string Name { get; set; }
    public required string ObjectId { get; set; }
    public required string DeviceClass { get; set; }
    public required string Icon { get; set; }
    public required bool SupportsPosition { get; set; }
    public required bool SupportsTilt { get; set; }
}

public struct EntityFan
{
    public required uint Key { get; set; }
    public required string Name { get; set; }
    public required string ObjectId { get; set; }
    public required string Icon { get; set; }
    public required bool SupportsOscillation { get; set; }
    public required bool SupportsSpeed { get; set; }
    public required int SupportedSpeedCount { get; set; }
}

public struct EntityLight
{
    public required uint Key { get; set; }
    public required string Name { get; set; }
    public required string ObjectId { get; set; }
    public required string Icon { get; set; }
    public required bool LegacySupportsBrightness { get; set; }
    public required bool LegacySupportsRgb { get; set; }
}

public struct EntitySensor
{
    public required uint Key { get; set; }
    public required string Name { get; set; }
    public required string ObjectId { get; set; }
    public required string Icon { get; set; }
    public required string UnitOfMeasurement { get; set; }
    public required int AccuracyDecimals { get; set; }
    public required string DeviceClass { get; set; }
}

public struct EntitySwitch
{
    public required uint Key { get; set; }
    public required string Name { get; set; }
    public required string ObjectId { get; set; }
    public required string DeviceClass { get; set; }
    public required string Icon { get; set; }
}

public struct EntityTextSensor
{
    public required uint Key { get; set; }
    public required string Name { get; set; }
    public required string ObjectId { get; set; }
    public required string Icon { get; set; }
    public required string DeviceClass { get; set; }
}

public struct EntityClimate
{
    public required uint Key { get; set; }
    public required string Name { get; set; }
    public required string ObjectId { get; set; }
    public required string Icon { get; set; }
    public required bool SupportsCurrentTemperature { get; set; }
    public required float VisualMinTemperature { get; set; }
    public required float VisualMaxTemperature { get; set; }
}

public struct EntityNumber
{
    public required uint Key { get; set; }
    public required string Name { get; set; }
    public required string ObjectId { get; set; }
    public required string Icon { get; set; }
    public required float MinValue { get; set; }
    public required float MaxValue { get; set; }
    public required float Step { get; set; }
    public required string UnitOfMeasurement { get; set; }
}

public struct EntitySelect
{
    public required uint Key { get; set; }
    public required string Name { get; set; }
    public required string ObjectId { get; set; }
    public required string Icon { get; set; }
    public required string[] Options { get; set; }
}

public struct EntityButton
{
    public required uint Key { get; set; }
    public required string Name { get; set; }
    public required string ObjectId { get; set; }
    public required string Icon { get; set; }
    public required string DeviceClass { get; set; }
}