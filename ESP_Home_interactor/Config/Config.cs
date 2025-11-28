using System.Text.Json;
using System.Text.Json.Serialization;

namespace ESP_Home_Interactor.Config;

public class Config
{
    public ESPConfig[]? ESPs { get; set; }
    public class ESPConfig
    {
        public required string Host { get; set; }
        public required int Port { get; set; }
        
        public EntitySwitch[]? Switches { get; set; }
        // will implement other entities later
    }

    public static async Task<Config> Read(string path)
    {
        await using var stream = File.OpenRead(path);
        var configuration = await JsonSerializer.DeserializeAsync<Config>(
            stream,
            new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip });

        if (configuration is null)
        {
            throw new ArgumentException($"File at {path} is not a valid configuration file.");
        }

        return configuration;
    }
}
