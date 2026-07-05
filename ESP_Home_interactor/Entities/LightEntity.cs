namespace ESP_Home_Interactor.Entities;

/// <summary>
/// Represents a light entity in ESPHome (e.g., monochromatic dimmable light)
/// Supports on/off state and brightness control
/// </summary>
public class LightEntity : EntityBase<bool>
{
    private bool? _currentState;
    private float _brightness;

    public LightEntity(uint key, string name, string objectId, bool supportsBrightness)
        : base(key, name, objectId)
    {
        SupportsBrightness = supportsBrightness;
    }

    public bool SupportsBrightness { get; }

    /// <summary>
    /// Current brightness in range 0.0 - 1.0
    /// </summary>
    public float Brightness => _brightness;

    /// <summary>
    /// Get the current on/off state
    /// </summary>
    public override bool GetValue()
    {
        return _currentState ?? false;
    }

    /// <summary>
    /// Update light state from LightStateResponse message
    /// </summary>
    public override void UpdateState(byte[] messageData)
    {
        try
        {
            var lightState = LightStateResponse.Parser.ParseFrom(messageData);
            if (lightState.Key == Key)
            {
                _currentState = lightState.State;
                _brightness = lightState.Brightness;
                HasState = true;
                Logger.LogIncoming($"LightState: '{Name}' is {GetDisplayValue()}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to parse light state for {Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Get human-readable state representation
    /// </summary>
    public override string GetDisplayValue()
    {
        if (!HasState) return "UNKNOWN";
        if (!_currentState!.Value) return "OFF";
        return SupportsBrightness ? $"ON ({_brightness:P0})" : "ON";
    }

    /// <summary>
    /// Lights support read, write, and subscribe operations
    /// </summary>
    public override bool SupportsOperation(EntityOperation operation)
    {
        return operation switch
        {
            EntityOperation.Read => true,
            EntityOperation.Write => true,
            EntityOperation.Subscribe => true,
            _ => false
        };
    }

    /// <summary>
    /// Set the light state (ON/OFF) and optionally brightness (0.0 - 1.0)
    /// </summary>
    public async Task SetStateAsync(ESPHomeConnection connection, bool state, float? brightness = null)
    {
        var lightCommand = new LightCommandRequest
        {
            Key = Key,
            HasState = true,
            State = state
        };

        if (brightness.HasValue && SupportsBrightness)
        {
            lightCommand.HasBrightness = true;
            lightCommand.Brightness = Math.Clamp(brightness.Value, 0f, 1f);
        }

        await connection.SendMessage((uint)MessageType.LightCommandRequest, lightCommand);
        var stateName = state ? "ON" : "OFF";
        var brightnessInfo = brightness.HasValue ? $" @ {brightness.Value:P0}" : "";
        Logger.LogOutgoing($"Sent LightCommand: '{Name}' → {stateName}{brightnessInfo}");
    }
}
