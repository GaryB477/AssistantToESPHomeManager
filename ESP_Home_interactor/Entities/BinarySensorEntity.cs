using Google.Protobuf;

namespace ESP_Home_Interactor.Entities;

/// <summary>
/// Represents a binary sensor entity in ESPHome (e.g., motion detector, door sensor)
/// Supports reading boolean states but typically not controllable
/// </summary>
public class BinarySensorEntity : EntityBase<bool>
{
    private bool? _currentState;
    
    public string DeviceClass { get; private set; }
    
    public BinarySensorEntity(uint key, string name, string objectId, string deviceClass = "") 
        : base(key, name, objectId)
    {
        DeviceClass = deviceClass;
    }
    
    /// <summary>
    /// Get the current binary sensor state
    /// </summary>
    public override bool GetValue()
    {
        return _currentState ?? false;
    }
    
    /// <summary>
    /// Update binary sensor state from BinarySensorStateResponse message
    /// </summary>
    public override void UpdateState(byte[] messageData)
    {
        try
        {
            var binarySensorState = BinarySensorStateResponse.Parser.ParseFrom(messageData);
            if (binarySensorState.Key == Key)
            {
                _currentState = binarySensorState.State;
                HasState = true;
                Logger.LogIncoming($"BinarySensorState: '{Name}' is {GetDisplayValue()}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to parse binary sensor state for {Name}: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Get human-readable state representation
    /// </summary>
    public override string GetDisplayValue()
    {
        if (!HasState || _currentState == null)
            return "UNKNOWN";
            
        // Use device class to provide meaningful state names
        return DeviceClass.ToLower() switch
        {
            "motion" => _currentState.Value ? "MOTION" : "NO MOTION",
            "door" or "window" => _currentState.Value ? "OPEN" : "CLOSED",
            "occupancy" => _currentState.Value ? "OCCUPIED" : "CLEAR",
            "safety" => _currentState.Value ? "UNSAFE" : "SAFE",
            "connectivity" => _currentState.Value ? "CONNECTED" : "DISCONNECTED",
            _ => _currentState.Value ? "ON" : "OFF"
        };
    }
    
    /// <summary>
    /// Binary sensors typically only support read and subscribe operations
    /// </summary>
    public override bool SupportsOperation(EntityOperation operation)
    {
        return operation switch
        {
            EntityOperation.Read => true,
            EntityOperation.Write => false,  // Binary sensors are typically read-only
            EntityOperation.Subscribe => true,
            _ => false
        };
    }
}