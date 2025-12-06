namespace ESP_Home_Interactor.Entities;

/// <summary>
/// Represents a boolean switch entity in ESPHome
/// Supports reading current state and controlling on/off
/// </summary>
public class SwitchEntity : EntityBase<bool>
{
    private bool? _currentState;
    
    public SwitchEntity(uint key, string name, string objectId) 
        : base(key, name, objectId)
    {
    }
    
    /// <summary>
    /// Get the current switch state
    /// </summary>
    public override bool GetValue()
    {
        return _currentState ?? false;
    }
    
    /// <summary>
    /// Update switch state from SwitchStateResponse message
    /// </summary>
    public override void UpdateState(byte[] messageData)
    {
        try
        {
            var switchState = SwitchStateResponse.Parser.ParseFrom(messageData);
            if (switchState.Key == Key)
            {
                _currentState = switchState.State;
                HasState = true;
                Logger.LogIncoming($"SwitchState: '{Name}' is {(_currentState.Value ? "ON" : "OFF")}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to parse switch state for {Name}: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Get human-readable state representation
    /// </summary>
    public override string GetDisplayValue()
    {
        return HasState ? (_currentState!.Value ? "ON" : "OFF") : "UNKNOWN";
    }
    
    /// <summary>
    /// Switches support read, write, and subscribe operations
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
    /// Set the switch state (ON/OFF)
    /// </summary>
    public async Task SetStateAsync(ESPHomeConnection connection, bool state)
    {
        var switchCommand = new SwitchCommandRequest
        {
            Key = Key,
            State = state
        };

        await connection.SendMessage(33, switchCommand);
        var stateName = state ? "ON" : "OFF";
        Logger.LogOutgoing($"Sent SwitchCommand: '{Name}' → {stateName}");
    }
}