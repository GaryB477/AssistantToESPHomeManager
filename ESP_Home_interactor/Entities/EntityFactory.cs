using Google.Protobuf;

namespace ESP_Home_Interactor.Entities;

/// <summary>
/// Factory for creating appropriate entity types based on ESPHome message types
/// </summary>
public static class EntityFactory
{
    /// <summary>
    /// Create an entity from a ListEntities message
    /// </summary>
    public static object? CreateEntity(uint messageType, byte[] messageData)
    {
        return messageType switch
        {
            17 => CreateSwitchEntity(messageData),        // ListEntitiesSwitchResponse
            24 => CreateSensorEntity(messageData),        // ListEntitiesSensorResponse  
            23 => CreateBinarySensorEntity(messageData),  // ListEntitiesBinarySensorResponse
            // Add more entity types as needed:
            // 25 => CreateTextSensorEntity(messageData),    // ListEntitiesTextSensorResponse
            // 26 => CreateLightEntity(messageData),         // ListEntitiesLightResponse
            // 27 => CreateFanEntity(messageData),           // ListEntitiesFanResponse
            // 28 => CreateCoverEntity(messageData),         // ListEntitiesCoverResponse
            _ => null // Unknown or unsupported entity type
        };
    }
    
    private static SwitchEntity CreateSwitchEntity(byte[] messageData)
    {
        var switchInfo = ListEntitiesSwitchResponse.Parser.ParseFrom(messageData);
        return new SwitchEntity(switchInfo.Key, switchInfo.Name, switchInfo.ObjectId);
    }
    
    private static SensorEntity CreateSensorEntity(byte[] messageData)
    {
        var sensorInfo = ListEntitiesSensorResponse.Parser.ParseFrom(messageData);
        return new SensorEntity(
            sensorInfo.Key, 
            sensorInfo.Name, 
            sensorInfo.ObjectId,
            sensorInfo.UnitOfMeasurement,
            sensorInfo.AccuracyDecimals
        );
    }
    
    private static BinarySensorEntity CreateBinarySensorEntity(byte[] messageData)
    {
        var binarySensorInfo = ListEntitiesBinarySensorResponse.Parser.ParseFrom(messageData);
        return new BinarySensorEntity(
            binarySensorInfo.Key, 
            binarySensorInfo.Name, 
            binarySensorInfo.ObjectId,
            binarySensorInfo.DeviceClass
        );
    }
    
    /// <summary>
    /// Update an entity's state based on a state response message
    /// </summary>
    public static bool UpdateEntityState(object entity, uint messageType, byte[] messageData)
    {
        try
        {
            switch (messageType)
            {
                case 26 when entity is SwitchEntity switchEntity:         // SwitchStateResponse
                    switchEntity.UpdateState(messageData);
                    return true;
                case 27 when entity is SensorEntity sensorEntity:         // SensorStateResponse  
                    sensorEntity.UpdateState(messageData);
                    return true;
                case 28 when entity is BinarySensorEntity binarySensorEntity:   // BinarySensorStateResponse
                    binarySensorEntity.UpdateState(messageData);
                    return true;
                    
                default:
                    return false; // Message type doesn't match entity type
            }
        }
        catch
        {
            return false;
        }
    }
}