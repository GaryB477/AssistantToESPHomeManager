using ESP_Home_Interactor.helper;

namespace ESP_Home_Interactor.Entities;

/// <summary>
/// Abstract base class for all ESPHome entities
/// Provides common functionality for entity identification and value retrieval
/// </summary>
public abstract class EntityBase<T>
{
    protected readonly Logger Logger = new Logger();
    
    public uint Key { get; protected set; }
    public string Name { get; protected set; }
    public string ObjectId { get; protected set; }
    public bool HasState { get; protected set; }

    protected EntityBase(uint key, string name, string objectId)
    {
        Key = key;
        Name = name;
        ObjectId = objectId;
        HasState = false;
    }

    /// <summary>
    /// Get the current value/state of this entity
    /// Returns default(T) if no state is available
    /// </summary>
    public abstract T GetValue();
    
    /// <summary>
    /// Update the entity's state from a received message
    /// </summary>
    public abstract void UpdateState(byte[] messageData);
    
    /// <summary>
    /// Get a human-readable representation of the current value
    /// </summary>
    public abstract string GetDisplayValue();
    
    /// <summary>
    /// Indicates whether this entity supports the given operation
    /// </summary>
    public abstract bool SupportsOperation(EntityOperation operation);
}

/// <summary>
/// Operations that entities can support
/// </summary>
public enum EntityOperation
{
    Read,      // Can read current value
    Write,     // Can set/control value
    Subscribe  // Can receive state updates
}