namespace VideoSizeCompressor.Core;

public static class EncodingPlanner
{
    public const int MinimumVideoBitrate = 8_000;
    private static readonly int[] AudioRates = [128_000, 96_000, 64_000, 48_000, 32_000, 24_000, 16_000, 8_000];

    public static EncodingPlan Create(MediaInfo media, CompressionOptions options)
    {
        options.Validate();
        if (!double.IsFinite(media.Duration) || media.Duration <= 0)
            throw new CompressionException("This file has no usable video duration. Repair or remux it first.");
        if (media.Video.Width < 16 || media.Video.Height < 16)
            throw new CompressionException("The video is too narrow for the supported encoders (minimum 16 pixels per side).");
        if (media.Video.IsHdr)
            throw new CompressionException("This video is HDR. This version supports SDR video; tone-map it to SDR first to avoid incorrect colors.");

        long target = (long)decimal.Floor(options.MaximumBytes * (1m - options.SafetyMarginPercent / 100m));
        double gross = target * 8.0 / media.Duration;
        int audio = 0, channels = 0, sampleRate = 0;
        long overhead = 0;
        // Re-evaluate audio against the payload budget, including its sample-table cost.
        for (int i = 0; i < 3; i++)
        {
            if (media.Audio is not null)
            {
                double allowance = Math.Max(8_000, (gross - overhead * 8.0 / media.Duration) * 0.20);
                int ceiling = (options.AudioBitrateKbps ?? 128) * 1000;
                audio = AudioRates.First(rate => rate <= Math.Min(ceiling, allowance));
                channels = audio <= 48_000 ? 1 : Math.Min(2, Math.Max(1, media.Audio.Channels));
                sampleRate = audio <= 24_000 ? 16_000 : audio <= 48_000 ? 24_000 : 48_000;
            }
            // MP4 headers + sample tables: deliberately budget per second as well as
            // per byte. A percentage alone badly underestimates overhead for long,
            // tiny encodes. VFR timestamps can require an entry per video frame.
            double frames = media.Duration * Math.Max(1, media.Video.FrameRate);
            double audioPackets = media.Duration * sampleRate / 1024.0;
            overhead = checked((long)Math.Ceiling(Math.Max(24_576, target * 0.003) + frames * 10 + audioPackets * 6));
        }
        double available = (target - overhead) * 8.0 / media.Duration - audio;
        if (available < MinimumVideoBitrate) throw TooSmall();
        // Protect encoder integer ranges when somebody asks for a huge size on a tiny clip.
        int video = (int)Math.Min(250_000_000, Math.Floor(available));
        var resolution = SelectResolution(media.Video, video, VideoCodecs.Get(options.Codec).BitsPerPixelFrame);
        return new(options.MaximumBytes, target, overhead, video, audio, channels, sampleRate,
            resolution, MakeWarning(media, video, audio, resolution));
    }

    public static EncodingPlan Retry(EncodingPlan previous, MediaInfo input, MediaInfo output, CompressionOptions options)
    {
        // MP4 normally exposes the actual video bitrate. Subtract actual non-video
        // bytes before scaling, rather than assuming audio shrinks with video.
        double videoBytes = output.Video.Bitrate > 0
            ? output.Video.Bitrate * output.Duration / 8.0
            : previous.VideoBitrate * input.Duration / 8.0;
        double fixedBytes = Math.Clamp(output.FileSize - videoBytes, 0, output.FileSize * 0.99);
        double ratio = (previous.TargetBytes - fixedBytes) / Math.Max(1, output.FileSize - fixedBytes);
        double reduced = previous.VideoBitrate * Math.Min(0.95, ratio * 0.97);
        if (!double.IsFinite(reduced) || reduced < MinimumVideoBitrate) throw TooSmall();
        int bitrate = (int)Math.Floor(reduced);
        var resolution = SelectResolution(input.Video, bitrate, VideoCodecs.Get(options.Codec).BitsPerPixelFrame);
        return previous with { VideoBitrate = bitrate, Resolution = resolution,
            Warning = MakeWarning(input, bitrate, previous.AudioBitrate, resolution) };
    }

    public static OutputResolution SelectResolution(VideoStream video, int bitrate, double bitsPerPixelFrame)
    {
        // 0.040 bits/pixel/frame for HEVC, 0.060 for AVC are conservative starting
        // heuristics, not perceptual guarantees. The ladder uses DISPLAY orientation.
        // Convert anamorphic content to square pixels within the original sample
        // bounds, never enlarge either coded dimension, and round DOWN to even sizes.
        bool rotate = Math.Abs(video.Rotation) % 180 == 90;
        double aspect = video.Width * video.SampleAspectRatio / video.Height;
        int sourceWidth = rotate ? video.Height : video.Width;
        int sourceHeight = rotate ? video.Width : video.Height;
        if (rotate) aspect = 1.0 / aspect;
        var original = Fit(sourceWidth, sourceHeight, aspect);
        bool portrait = original.Height > original.Width;
        var candidates = new List<OutputResolution> { original };
        foreach (var (longEdge, shortEdge) in new[] { (1920, 1080), (1280, 720), (854, 480), (640, 360), (426, 240), (256, 144) })
        {
            int boundWidth = Math.Min(original.Width, portrait ? shortEdge : longEdge);
            int boundHeight = Math.Min(original.Height, portrait ? longEdge : shortEdge);
            var candidate = Fit(boundWidth, boundHeight, aspect);
            if (candidate.Width >= 16 && candidate.Height >= 16 && !candidates.Contains(candidate)) candidates.Add(candidate);
        }
        if (original.Width < 16 || original.Height < 16)
            throw new CompressionException("The display aspect ratio is too extreme for these encoders.");
        double frameRate = double.IsFinite(video.FrameRate) && video.FrameRate > 0 ? video.FrameRate : 30;
        return candidates.FirstOrDefault(r => bitrate >= r.Width * (double)r.Height * frameRate * bitsPerPixelFrame, candidates[^1]);
    }

    private static OutputResolution Fit(int width, int height, double aspect)
    {
        double w = Math.Min(width, height * aspect);
        double h = w / aspect;
        return new((int)Math.Floor(w / 2) * 2, (int)Math.Floor(h / 2) * 2);
    }

    private static string? MakeWarning(MediaInfo media, int video, int audio, OutputResolution resolution)
    {
        var warnings = new List<string>();
        if (video < 150_000) warnings.Add("This size is very restrictive; fine detail and motion quality will be limited.");
        if (audio is > 0 and <= 32_000) warnings.Add("Low-bitrate mono audio is used to preserve the video budget.");
        if (media.AudioStreamCount > 1) warnings.Add("Only the default audio track (or the first available track) is included.");
        if (media.SubtitleStreamCount > 0) warnings.Add("Subtitle tracks are omitted; subtitles already burned into the picture remain.");
        return warnings.Count == 0 ? null : string.Join(" ", warnings);
    }

    private static CompressionException TooSmall() => new(
        "This size is too small for the full video, its audio, and MP4 overhead. Increase the maximum size; the video will never be truncated to fit.");
}
