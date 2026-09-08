using System.Text.Json;
using VideoSizeCompressor.Core;

namespace VideoSizeCompressor.Commands;

public sealed record UserSettings
{
    public string Codec { get; init; } = "hevc";
    public string Preset { get; init; } = "medium";
    public decimal SafetyMarginPercent { get; init; } = 2.5m;
    public int MaxRetries { get; init; } = 3;
    public int? AudioBitrateKbps { get; init; }
    public int Threads { get; init; } = Math.Clamp(Environment.ProcessorCount - 1, 1, 16);
    public string? FFmpegDirectory { get; init; }

    public static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VideoSizeCompressor", "settings.json");

    public CompressionOptions OptionsFor(string input, decimal size) => new()
    {
        InputPath = input, MaximumMegabytes = size, Codec = Codec, Preset = Preset,
        SafetyMarginPercent = SafetyMarginPercent, MaxRetries = MaxRetries,
        AudioBitrateKbps = AudioBitrateKbps, Threads = Threads, FFmpegDirectory = FFmpegDirectory
    };

    public static UserSettings Load()
    {
        if (!File.Exists(SettingsPath)) return new();
        try
        {
            var settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(SettingsPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new JsonException("Empty settings.");
            settings.OptionsFor("validation.mp4", 20m).Validate();
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or CompressionException)
        { throw new CompressionException($"Could not load settings. Correct or rename {SettingsPath}.\n{ex.Message}", ex); }
    }

    public void Save()
    {
        OptionsFor("validation.mp4", 20m).Validate();
        string directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $"settings-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, SettingsPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
