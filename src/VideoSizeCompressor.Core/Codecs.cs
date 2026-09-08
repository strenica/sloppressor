using System.Globalization;

namespace VideoSizeCompressor.Core;

// A new encoder implements this strategy; the engine, retry gate, and both UIs
// stay unchanged. AV1 needs its own tested multipass implementation before listing it.
public interface IVideoCodec
{
    string Id { get; }
    string Encoder { get; }
    string DisplayName { get; }
    double BitsPerPixelFrame { get; }
    void AddArguments(List<string> args, CompressionOptions options, EncodingPlan plan, int pass);
}

public static class VideoCodecs
{
    public static IVideoCodec Get(string id) => id.ToLowerInvariant() switch
    {
        "hevc" or "h265" => new HevcCodec(),
        "h264" or "avc" => new AvcCodec(),
        _ => throw new CompressionException("Unknown codec. Choose hevc (H.265) or h264 (H.264).")
    };
}

internal sealed class HevcCodec : IVideoCodec
{
    public string Id => "hevc";
    public string Encoder => "libx265";
    public string DisplayName => "HEVC / H.265";
    public double BitsPerPixelFrame => 0.040;

    public void AddArguments(List<string> args, CompressionOptions options, EncodingPlan plan, int pass)
    {
        args.AddRange(["-c:v", Encoder, "-preset", options.Preset, "-b:v", plan.VideoBitrate.ToString(CultureInfo.InvariantCulture),
            "-x265-params", $"pass={pass}:stats=video.stats:pools={options.Threads}:frame-threads={Math.Min(4, options.Threads)}:log-level=warning",
            "-tag:v", "hvc1"]);
        // x265 MUST use its own pass= parameter. Generic FFmpeg -pass alone is not
        // sufficient. Relative ASCII stats paths avoid drive-colon/Unicode escaping
        // inside x265's colon-delimited options. WorkingDirectory is unique per pass pair.
    }
}

internal sealed class AvcCodec : IVideoCodec
{
    public string Id => "h264";
    public string Encoder => "libx264";
    public string DisplayName => "AVC / H.264";
    public double BitsPerPixelFrame => 0.060;

    public void AddArguments(List<string> args, CompressionOptions options, EncodingPlan plan, int pass) =>
        args.AddRange(["-c:v", Encoder, "-preset", options.Preset, "-b:v", plan.VideoBitrate.ToString(CultureInfo.InvariantCulture),
            "-pass", pass.ToString(CultureInfo.InvariantCulture), "-passlogfile", "video-pass"]);
}
