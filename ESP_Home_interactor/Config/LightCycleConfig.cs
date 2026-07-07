using System.Text.Json;

namespace ESP_Home_Interactor.Config;

/// <summary>
/// Light cycle configuration, organized by growth stage: each stage
/// (Seedling/Vegetative/Bloom) holds one preset schedule of phases
/// (e.g. High/Low), each with a start time and target states for all
/// actors. Only the preset of the active stage is enforced; the phase
/// whose start time was passed most recently is active (wraps around
/// midnight). Past stage selections are kept in StageHistory.
/// </summary>
public class LightCycleConfig
{
    public static readonly string[] Stages = { "Seedling", "Vegetative", "Bloom" };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>One preset schedule per growth stage</summary>
    public Dictionary<string, List<CyclePhase>> Presets { get; set; } = new();

    /// <summary>Currently selected growth stage; null pauses cycle enforcement</summary>
    public string? ActiveStage { get; set; }

    public DateTime? ActiveSince { get; set; }

    /// <summary>Completed stage selections, oldest first</summary>
    public List<StageHistoryEntry> StageHistory { get; set; } = new();

    /// <summary>Legacy field: old files held one flat schedule. Migrated into
    /// the stage presets on load and never written back (null is not serialized).</summary>
    public List<CyclePhase>? Phases { get; set; }

    /// <summary>The phases of the active stage's preset; empty when no stage is selected</summary>
    public List<CyclePhase> ActivePhases =>
        ActiveStage != null && Presets.TryGetValue(ActiveStage, out var phases)
            ? phases
            : new List<CyclePhase>();

    public static async Task<LightCycleConfig> Read(string path)
    {
        await using var stream = File.OpenRead(path);
        var config = await JsonSerializer.DeserializeAsync<LightCycleConfig>(stream, SerializerOptions);
        if (config == null)
            throw new ArgumentException($"File at {path} is not a valid light cycle configuration.");
        config.Migrate();
        return config;
    }

    public async Task Save(string path)
    {
        // Write to a temp file first: a crash mid-write must not be able to
        // corrupt the existing config (the scheduler depends on it at startup)
        var tmpPath = path + ".tmp";
        await using (var stream = File.Create(tmpPath))
        {
            await JsonSerializer.SerializeAsync(stream, this, SerializerOptions);
        }

        File.Move(tmpPath, path, overwrite: true);
    }

    /// <summary>
    /// Get the phase of the active preset that is active at the given time
    /// of day; null when no stage is selected or its preset is empty.
    /// </summary>
    public CyclePhase? GetActivePhase(TimeOnly now)
    {
        var phases = ActivePhases;
        if (phases.Count == 0) return null;

        var sorted = phases.OrderBy(p => p.Start).ToList();
        // Before the first start time of the day, the last phase (from yesterday) is still active
        return sorted.LastOrDefault(p => p.Start <= now) ?? sorted[^1];
    }

    /// <summary>Move a legacy flat schedule into the presets and ensure every stage has one</summary>
    private void Migrate()
    {
        if (Phases is { Count: > 0 })
        {
            // Independent copies: editing one stage's preset must not affect the others
            foreach (var stage in Stages)
                if (!Presets.TryGetValue(stage, out var existing) || existing.Count == 0)
                    Presets[stage] = Clone(Phases);
            Phases = null;
        }

        foreach (var stage in Stages)
            Presets.TryAdd(stage, new List<CyclePhase>());
    }

    private static List<CyclePhase> Clone(List<CyclePhase> phases) =>
        JsonSerializer.Deserialize<List<CyclePhase>>(
            JsonSerializer.Serialize(phases, SerializerOptions), SerializerOptions)!;
}

/// <summary>One completed period during which a growth stage was selected</summary>
public class StageHistoryEntry
{
    public required string Stage { get; set; }
    public required DateTime From { get; set; }
    public required DateTime To { get; set; }
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
/// Matched via node name + entity object id.
/// </summary>
public class ActorState
{
    /// <summary>Name of the ESP node as configured in config.json (legacy files may hold a host/IP)</summary>
    public string Node { get; set; } = "";

    public required string ObjectId { get; set; }
    public required bool On { get; set; }

    /// <summary>Brightness 0.0 - 1.0, lights only</summary>
    public float? Brightness { get; set; }

    /// <summary>Legacy field: old cycle files referenced the node by host/IP.
    /// Read-only mapping onto Node; never written back.</summary>
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Host
    {
        get => null;
        set
        {
            if (string.IsNullOrEmpty(Node) && value != null) Node = value;
        }
    }
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
