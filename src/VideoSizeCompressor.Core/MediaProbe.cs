using System.Globalization;
using System.Text.Json;

namespace VideoSizeCompressor.Core;

public interface IMediaProbe
{
    Task<MediaInfo> InspectAsync(string path, ToolPaths tools, JobLog log, CancellationToken cancellationToken);
}

public sealed class MediaProbe(IProcessRunner runner) : IMediaProbe
{
    public async Task<MediaInfo> InspectAsync(string path, ToolPaths tools, JobLog log, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        ProcessResult result;
        try
        {
            result = await runner.RunAsync(new(tools.FFprobe,
                ["-v", "error", "-protocol_whitelist", "file,pipe", "-show_format", "-show_streams", "-of", "json", path],
                Path.GetTempPath(), CaptureOutput: true), log, null, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new CompressionException("FFprobe could not inspect the video within 60 seconds. Check the file or copy it to a local drive."); }
        if (result.ExitCode != 0) throw new CompressionException("The video could not be read. It may be unsupported, incomplete, or corrupted. View the FFmpeg log for details.");
        try { return Parse(path, new FileInfo(path).Length, result.StandardOutput); }
        catch (JsonException ex) { throw new CompressionException("FFprobe returned invalid metadata for this file.", ex); }
    }

    public static MediaInfo Parse(string path, long fileSize, string json)
    {
        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array)
            throw new CompressionException("No media streams were found in the file.");
        var all = streams.EnumerateArray().ToArray();
        var videos = all.Where(s => Text(s, "codec_type") == "video" && Flag(s, "attached_pic") == 0)
            .OrderByDescending(s => Flag(s, "default")).ToArray();
        if (videos.Length == 0) throw new CompressionException("This file contains no video stream (cover images do not count as video).");
        JsonElement v = videos[0];
        var audios = all.Where(s => Text(s, "codec_type") == "audio").OrderByDescending(s => Flag(s, "default")).ToArray();
        root.TryGetProperty("format", out var format);
        double? streamDuration = PositiveNumber(v, "duration");
        if (streamDuration is null && v.TryGetProperty("tags", out var tags)
            && TimeSpan.TryParse(Text(tags, "DURATION"), CultureInfo.InvariantCulture, out var time) && time.TotalSeconds > 0)
            streamDuration = time.TotalSeconds;
        double duration = streamDuration ?? PositiveNumber(format, "duration") ?? 0;
        if (duration <= 0 || !double.IsFinite(duration)) throw new CompressionException("This video has no reliable duration. Repair or remux it before compression.");
        int rotation = 0;
        if (v.TryGetProperty("side_data_list", out var data))
            foreach (var side in data.EnumerateArray())
                if (Number(side, "rotation") is { } degrees) rotation = (int)Math.Round(degrees);
        if (rotation == 0 && v.TryGetProperty("tags", out var videoTags) && Number(videoTags, "rotate") is { } tagRotation)
            rotation = (int)Math.Round(tagRotation);
        rotation = ((rotation % 360) + 360) % 360;
        if (rotation % 90 != 0) throw new CompressionException("This video uses a non-right-angle display rotation, which is not supported yet.");
        double fps = Ratio(Text(v, "avg_frame_rate"), '/') ?? Ratio(Text(v, "r_frame_rate"), '/') ?? 30;
        fps = fps > 0 && double.IsFinite(fps) ? fps : 30;
        double sar = Ratio(Text(v, "sample_aspect_ratio"), ':') ?? 1;
        if (sar <= 0 || !double.IsFinite(sar)) sar = 1;
        var video = new VideoStream((int)(Number(v, "index") ?? 0), (int)(Number(v, "width") ?? 0),
            (int)(Number(v, "height") ?? 0), sar, rotation, fps,
            (long)(PositiveNumber(v, "bit_rate") ?? 0), streamDuration,
            PositiveNumber(v, "nb_frames") is { } frames ? (long)frames : null,
            Text(v, "codec_name") ?? "unknown", Text(v, "color_transfer") is "smpte2084" or "arib-std-b67");
        AudioStream? audio = audios.Length > 0 ? new((int)(Number(audios[0], "index") ?? 0),
            (int)(PositiveNumber(audios[0], "channels") ?? 2), (long)(PositiveNumber(audios[0], "bit_rate") ?? 0),
            Text(audios[0], "codec_name") ?? "unknown") : null;
        return new(path, fileSize, duration, (long)(PositiveNumber(format, "bit_rate") ?? fileSize * 8.0 / duration),
            video, audio, audios.Length, all.Count(s => Text(s, "codec_type") == "subtitle"));
    }

    private static string? Text(JsonElement obj, string property) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(property, out var value) ? value.ToString() : null;
    private static double? Number(JsonElement obj, string property) =>
        double.TryParse(Text(obj, property), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && double.IsFinite(d) ? d : null;
    private static double? PositiveNumber(JsonElement obj, string property) => Number(obj, property) is > 0 and var n ? n : null;
    private static int Flag(JsonElement stream, string key) => stream.TryGetProperty("disposition", out var d) ? (int)(Number(d, key) ?? 0) : 0;
    private static double? Ratio(string? value, char separator)
    {
        string[] parts = (value ?? "").Split(separator);
        return parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double a)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double b) && b != 0 ? a / b : null;
    }
}
