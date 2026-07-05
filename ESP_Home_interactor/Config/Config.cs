using System.Text.Json;
using System.Text.Json.Serialization;
using ESP_Home_Interactor.AcInfinity;

namespace ESP_Home_Interactor.Config;

public class Config
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public required ESPConfig[] ESPNode { get; init; }
    public AcInfinityConfig? AcInfinity { get; init; }

    public static async Task<Config> Read(string path)
    {
        await using var stream = File.OpenRead(path);
        var configuration = await JsonSerializer.DeserializeAsync<Config>(stream, SerializerOptions);

        return configuration ?? throw new ArgumentException($"File at {path} is not a valid configuration file.");
    }

    public async Task Save(string path)
    {
        // Write to a temp file first: a crash mid-write must not be able to
        // corrupt the config every service reads at startup
        var tmpPath = path + ".tmp";
        await using (var stream = File.Create(tmpPath))
        {
            await JsonSerializer.SerializeAsync(stream, this, SerializerOptions);
        }

        File.Move(tmpPath, path, overwrite: true);
    }
}
