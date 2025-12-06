using Google.Protobuf;

namespace ESP_Home_Interactor.Entities;

/// <summary>
/// Represents an analog sensor entity in ESPHome (e.g., temperature, light, humidity)
/// Supports reading numeric values with optional units
/// </summary>
public class SensorEntity : EntityBase<float>
{
    private float? _currentValue;
    
    public string UnitOfMeasurement { get; private set; }
    public int AccuracyDecimals { get; private set; }
    
    public SensorEntity(uint key, string name, string objectId, string unitOfMeasurement = "", int accuracyDecimals = 1) 
        : base(key, name, objectId)
    {
        UnitOfMeasurement = unitOfMeasurement;
        AccuracyDecimals = accuracyDecimals;
    }
    
    /// <summary>
    /// Get the current sensor value
    /// </summary>
    public override float GetValue()
    {
        return _currentValue ?? 0f;
    }
    
    /// <summary>
    /// Update sensor state from SensorStateResponse message
    /// </summary>
    public override void UpdateState(byte[] messageData)
    {
        try
        {
            var sensorState = SensorStateResponse.Parser.ParseFrom(messageData);
            if (sensorState.Key == Key)
            {
                _currentValue = sensorState.State;
                HasState = true;
                Logger.LogIncoming($"SensorState: '{Name}' = {GetDisplayValue()}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to parse sensor state for {Name}: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Get human-readable value with units
    /// </summary>
    public override string GetDisplayValue()
    {
        if (!HasState || _currentValue == null)
            return "UNKNOWN";
            
        var roundedValue = Math.Round(_currentValue.Value, AccuracyDecimals);
        var format = $"F{AccuracyDecimals}";
        return string.IsNullOrEmpty(UnitOfMeasurement) 
            ? roundedValue.ToString(format)
            : $"{roundedValue.ToString(format)} {UnitOfMeasurement}";
    }
    
    /// <summary>
    /// Sensors typically only support read and subscribe operations
    /// </summary>
    public override bool SupportsOperation(EntityOperation operation)
    {
        return operation switch
        {
            EntityOperation.Read => true,
            EntityOperation.Write => false,  // Most sensors are read-only
            EntityOperation.Subscribe => true,
            _ => false
        };
    }
}