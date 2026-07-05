using System.Text.Json;

namespace ESP_Home_Interactor.Config;

/// <summary>
/// Light cycle configuration: a day consists of phases (e.g. High/Low),
/// each with a start time and target states for all actors.
/// The phase whose start time was passed most recently is active
/// (wraps around midnight).
/// </summary>
public class LightCycleConfig
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public required List<CyclePhase> Phases { get; set; }

    public static async Task<LightCycleConfig> Read(string path)
    {
        await using var stream = File.OpenRead(path);
        var config = await JsonSerializer.DeserializeAsync<LightCycleConfig>(stream, SerializerOptions);
        return config ?? throw new ArgumentException($"File at {path} is not a valid light cycle configuration.");
    }

    public async Task Save(string path)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, this, SerializerOptions);
    }

    /// <summary>
    /// Get the phase active at the given time of day
    /// </summary>
    public CyclePhase GetActivePhase(TimeOnly now)
    {
        var sorted = Phases.OrderBy(p => p.Start).ToList();
        // Before the first start time of the day, the last phase (from yesterday) is still active
        return sorted.LastOrDefault(p => p.Start <= now) ?? sorted[^1];
    }
}

public class CyclePhase
{
    public required string Name { get; set; }
    public required TimeOnly Start { get; set; }
    public required List<ActorState> Actors { get; set; }
    public List<AcFanState> AcFans { get; set; } = new();
}

/// <summary>
/// Target state of one actor (light or switch) during a phase.
/// Matched via device host + entity object id.
/// </summary>
public class ActorState
{
    public required string Host { get; set; }
    public required string ObjectId { get; set; }
    public required bool On { get; set; }

    /// <summary>Brightness 0.0 - 1.0, lights only</summary>
    public float? Brightness { get; set; }
}

/// <summary>
/// Target level of one AC Infinity fan port during a phase.
/// Matched via controller name + port number.
/// </summary>
public class AcFanState
{
    public required string Controller { get; set; }
    public required int Port { get; set; }

    /// <summary>Fan level 0 - 10, 0 turns the port off</summary>
    public required int Level { get; set; }
}
