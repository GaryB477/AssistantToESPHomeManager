namespace ESP_Home_Interactor;

/// <summary>
/// ESPHome Native API message type IDs
/// Based on ESPHome protocol documentation and protobuf definitions
/// </summary>
public enum MessageType : uint
{
    // Connection Management (1-10)
    HelloRequest = 1,
    HelloResponse = 2,
    AuthenticationRequest = 3,
    AuthenticationResponse = 4,
    DisconnectRequest = 5,
    DisconnectResponse = 6,
    PingRequest = 7,
    PingResponse = 8,
    DeviceInfoRequest = 9,
    DeviceInfoResponse = 10,

    // Entity Discovery and State (11-29)
    ListEntitiesRequest = 11,
    ListEntitiesBinarySensorResponse = 12,
    ListEntitiesCoverResponse = 13,
    ListEntitiesFanResponse = 14,
    ListEntitiesLightResponse = 15,
    ListEntitiesSensorResponse = 16,
    ListEntitiesSwitchResponse = 17,
    ListEntitiesTextSensorResponse = 18,
    ListEntitiesDoneResponse = 19,
    SubscribeStatesRequest = 20,
    BinarySensorStateResponse = 21,
    CoverStateResponse = 22,
    FanStateResponse = 23,
    LightStateResponse = 24,
    SensorStateResponse = 25,
    SwitchStateResponse = 26,
    TextSensorStateResponse = 27,
    SubscribeLogsRequest = 28,
    SubscribeLogsResponse = 29,

    // Entity Commands (30-35, 41-42)
    CoverCommandRequest = 30,
    FanCommandRequest = 31,
    LightCommandRequest = 32,
    SwitchCommandRequest = 33,
    SubscribeHomeassistantServicesRequest = 34,
    HomeassistantServiceResponse = 35,
    SubscribeHomeAssistantStatesRequest = 38,
    SubscribeHomeAssistantStateResponse = 39,
    HomeAssistantStateResponse = 40,
    ListEntitiesServicesResponse = 41,
    ExecuteServiceRequest = 42,

    // Camera (43-45)
    ListEntitiesCameraResponse = 43,
    CameraImageResponse = 44,
    CameraImageRequest = 45,

    // Climate (46-48)
    ListEntitiesClimateResponse = 46,
    ClimateStateResponse = 47,
    ClimateCommandRequest = 48,

    // Number (49-51)
    ListEntitiesNumberResponse = 49,
    NumberStateResponse = 50,
    NumberCommandRequest = 51,

    // Select (52-54)
    ListEntitiesSelectResponse = 52,
    SelectStateResponse = 53,
    SelectCommandRequest = 54,

    // Siren (55-57)
    ListEntitiesSirenResponse = 55,
    SirenStateResponse = 56,
    SirenCommandRequest = 57,

    // Lock (58-60)
    ListEntitiesLockResponse = 58,
    LockStateResponse = 59,
    LockCommandRequest = 60,

    // Button (61-62)
    ListEntitiesButtonResponse = 61,
    ButtonCommandRequest = 62,

    // Media Player (63-65)
    ListEntitiesMediaPlayerResponse = 63,
    MediaPlayerStateResponse = 64,
    MediaPlayerCommandRequest = 65,

    // Bluetooth Proxy (66-88, 93, 126-127)
    BluetoothProxySubscribeRequest = 66,
    BluetoothProxySubscribeResponse = 67,
    BluetoothDeviceRequest = 68,
    BluetoothDeviceConnectionResponse = 69,
    BluetoothGATTGetServicesRequest = 70,
    BluetoothGATTGetServicesResponse = 71,
    BluetoothGATTGetServicesDoneResponse = 72,
    BluetoothGATTReadRequest = 73,
    BluetoothGATTReadResponse = 74,
    BluetoothGATTWriteRequest = 75,
    BluetoothGATTWriteResponse = 76,
    BluetoothGATTNotifyRequest = 77,
    BluetoothGATTNotifyResponse = 78,
    BluetoothGATTNotifyDataResponse = 79,
    BluetoothLEAdvertisementResponse = 80,
    BluetoothDevicePairingResponse = 81,
    BluetoothDeviceUnpairingResponse = 82,
    BluetoothDeviceClearCacheResponse = 83,
    BluetoothGATTGetServiceRequest = 84,
    BluetoothGATTGetServiceResponse = 85,
    BluetoothDeviceConnectionStateResponse = 86,
    BluetoothGATTErrorResponse = 87,
    BluetoothGATTWriteDescriptorRequest = 88,

    // Voice Assistant (89-92, 106, 115, 119-123)
    VoiceAssistantRequest = 89,
    VoiceAssistantResponse = 90,
    VoiceAssistantEventResponse = 91,
    VoiceAssistantAudioSettings = 92,
    BluetoothDeviceRequestResponse = 93,

    // Alarm Control Panel (94-96)
    ListEntitiesAlarmControlPanelResponse = 94,
    AlarmControlPanelStateResponse = 95,
    AlarmControlPanelCommandRequest = 96,

    // Text (97-99)
    ListEntitiesTextResponse = 97,
    TextStateResponse = 98,
    TextCommandRequest = 99,

    // Date (100-102)
    ListEntitiesDateResponse = 100,
    DateStateResponse = 101,
    DateCommandRequest = 102,

    // Time (103-105)
    ListEntitiesTimeResponse = 103,
    TimeStateResponse = 104,
    TimeCommandRequest = 105,

    VoiceAssistantEventData = 106,

    // Event (107-108)
    ListEntitiesEventResponse = 107,
    EventResponse = 108,

    // Valve (109-111)
    ListEntitiesValveResponse = 109,
    ValveStateResponse = 110,
    ValveCommandRequest = 111,

    // DateTime (112-114)
    ListEntitiesDateTimeResponse = 112,
    DateTimeStateResponse = 113,
    DateTimeCommandRequest = 114,

    VoiceAssistantAudioSettingsRequest = 115,

    // Update (116-118)
    ListEntitiesUpdateResponse = 116,
    UpdateStateResponse = 117,
    UpdateCommandRequest = 118,

    // Voice Assistant Timer (119-123)
    VoiceAssistantTimerEventResponse = 119,
    VoiceAssistantAnnounceRequest = 120,
    VoiceAssistantAnnounceResponse = 121,
    VoiceAssistantWakeWordRequest = 122,
    VoiceAssistantWakeWordResponse = 123,

    // Noise Encryption (124-125)
    ConnectRequest = 124,
    ConnectResponse = 125,

    // Additional Bluetooth (126-127)
    BluetoothConnectionsFreeRequest = 126,
    BluetoothConnectionsFreeResponse = 127,

    // Z-Wave Proxy (128-129)
    UnsubscribeBluetoothLEAdvertisementsRequest = 128,
    BluetoothLERawAdvertisementsResponse = 129,

    // Services (130)
    GetTimeRequest = 36,
    GetTimeResponse = 37,
    BluetoothGATTNotifyDataRequest = 130
}

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