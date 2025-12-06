using System.Text.Json;
using System.Text.Json.Serialization;

namespace ESP_Home_Interactor.Config;

public class Config
{
    public required ESPConfig[] ESPNode { get; init; }

    public static async Task<Config> Read(string path)
    {
        await using var stream = File.OpenRead(path);
        var configuration = await JsonSerializer.DeserializeAsync<Config>(
            stream,
            new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip });

        return configuration ?? throw new ArgumentException($"File at {path} is not a valid configuration file.");
    }
}
