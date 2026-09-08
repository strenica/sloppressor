using System.Globalization;

namespace VideoSizeCompressor.Core;

public sealed record CompressionOptions
{
    public required string InputPath { get; init; }
    public required decimal MaximumMegabytes { get; init; }
    public string? OutputPath { get; init; }
    public string Codec { get; init; } = "hevc";
    public string Preset { get; init; } = "medium";
    public decimal SafetyMarginPercent { get; init; } = 2.5m;
    public int MaxRetries { get; init; } = 3;
    public int? AudioBitrateKbps { get; init; }
    public int Threads { get; init; } = Math.Clamp(Environment.ProcessorCount - 1, 1, 16);
    public string? FFmpegDirectory { get; init; }

    // Decimal arithmetic avoids binary rounding at the maximum-size boundary.
    public long MaximumBytes => checked((long)decimal.Floor(MaximumMegabytes * 1_000_000m));
    public string SizeLabel => (MaximumBytes / 1_000_000m).ToString("0.######", CultureInfo.InvariantCulture);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(InputPath)) throw new CompressionException("Choose a video file first.");
        if (string.IsNullOrWhiteSpace(Codec)) throw new CompressionException("Choose a codec: hevc or h264.");
        if (MaximumMegabytes <= 0 || MaximumMegabytes > 1_000_000m)
            throw new CompressionException("Maximum size must be greater than zero and at most 1,000,000 MB.");
        if (MaximumBytes < 1) throw new CompressionException("Maximum size must be at least one byte.");
        if (SafetyMarginPercent < 0.5m || SafetyMarginPercent > 25m)
            throw new CompressionException("Safety margin must be between 0.5 and 25 percent.");
        if (MaxRetries is < 0 or > 5) throw new CompressionException("Retries must be between 0 and 5.");
        if (AudioBitrateKbps is < 8 or > 320)
            throw new CompressionException("Audio bitrate must be between 8 and 320 kbps.");
        if (Threads is < 1 or > 64) throw new CompressionException("Threads must be between 1 and 64.");
        if (!new[] { "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow" }.Contains(Preset))
            throw new CompressionException("Unknown encoder preset. Use medium, fast, slow, or another x26x preset.");
        _ = VideoCodecs.Get(Codec);
    }
}
