namespace VideoSizeCompressor.Core;

public sealed record GifOptions
{
    public required string InputPath { get; init; }
    public string? OutputPath { get; init; }
    public int Width { get; init; } = 480;
    public int FramesPerSecond { get; init; } = 12;
    public decimal StartSeconds { get; init; }
    public decimal? DurationSeconds { get; init; } = 10m;
    public string? FFmpegDirectory { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(InputPath)) throw new CompressionException("Choose a video file first.");
        if (Width is < 64 or > 1920) throw new CompressionException("GIF width must be between 64 and 1920 pixels.");
        if (FramesPerSecond is < 1 or > 30) throw new CompressionException("GIF frame rate must be between 1 and 30 frames per second.");
        if (StartSeconds < 0 || StartSeconds > 86_400m)
            throw new CompressionException("GIF start time must be between 0 and 86,400 seconds.");
        if (DurationSeconds is { } duration && (duration <= 0 || duration > 86_400m))
            throw new CompressionException("GIF duration must be greater than 0 and at most 86,400 seconds.");
        if (OutputPath is not null && !Path.GetExtension(OutputPath).Equals(".gif", StringComparison.OrdinalIgnoreCase))
            throw new CompressionException("The GIF output filename must end in .gif.");
    }
}

public sealed record GifPlan(OutputResolution Resolution, double StartSeconds, double DurationSeconds, double SourceDuration)
{
    public static GifPlan Create(MediaInfo media, GifOptions options)
    {
        options.Validate();
        if (!double.IsFinite(media.Duration) || media.Duration <= 0)
            throw new CompressionException("This file has no usable video duration. Repair or remux it first.");
        if (media.Video.IsHdr)
            throw new CompressionException("This video is HDR. Convert it to SDR first so the GIF colors stay accurate.");
        if (media.Video.Width < 2 || media.Video.Height < 2)
            throw new CompressionException("The video is too small to convert to a GIF.");
        double start = (double)options.StartSeconds;
        if (start >= media.Duration)
            throw new CompressionException("The GIF start time is at or beyond the end of the video.");
        double remaining = media.Duration - start;
        double duration = options.DurationSeconds is { } requested ? Math.Min((double)requested, remaining) : remaining;
        if (duration <= 0 || !double.IsFinite(duration))
            throw new CompressionException("The selected GIF clip has no usable duration.");
        return new(SelectResolution(media.Video, options.Width), start, duration, media.Duration);
    }

    public static OutputResolution SelectResolution(VideoStream video, int maximumWidth)
    {
        // Display orientation, square pixels, and no upscaling. GIF sizes do not
        // need to be even, but rounding down to even dimensions is predictable.
        bool rotate = Math.Abs(video.Rotation) % 180 == 90;
        double aspect = video.Width * video.SampleAspectRatio / video.Height;
        int sourceWidth = rotate ? video.Height : video.Width;
        int sourceHeight = rotate ? video.Width : video.Height;
        if (rotate) aspect = 1.0 / aspect;
        if (!(aspect > 0) || !double.IsFinite(aspect))
            throw new CompressionException("The video's display aspect ratio is not usable.");
        double width = Math.Min(maximumWidth, sourceWidth);
        double height = width / aspect;
        if (height > sourceHeight)
        {
            height = sourceHeight;
            width = height * aspect;
        }
        int outputWidth = Math.Max(2, (int)Math.Floor(width) & ~1);
        int outputHeight = Math.Max(2, (int)Math.Floor(height) & ~1);
        if (outputWidth < 2 || outputHeight < 2)
            throw new CompressionException("The video's display aspect ratio is too extreme for a GIF.");
        return new(outputWidth, outputHeight);
    }
}

public sealed record GifProgress(JobStage Stage, string Message, int Pass = 0, double Percent = 0,
    string? Speed = null, GifPlan? Plan = null);

public sealed record GifResult(string OutputPath, long OutputBytes, double StartSeconds, double DurationSeconds,
    double SourceDuration, OutputResolution Resolution, int FramesPerSecond, TimeSpan Elapsed);
