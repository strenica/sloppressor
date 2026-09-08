namespace VideoSizeCompressor.Core;

public sealed record VideoStream(
    int Index, int Width, int Height, double SampleAspectRatio,
    int Rotation, double FrameRate, long Bitrate, double? Duration,
    long? FrameCount, string Codec, bool IsHdr);

public sealed record AudioStream(int Index, int Channels, long Bitrate, string Codec);

public sealed record MediaInfo(
    string Path, long FileSize, double Duration, long Bitrate,
    VideoStream Video, AudioStream? Audio, int AudioStreamCount, int SubtitleStreamCount);

public readonly record struct OutputResolution(int Width, int Height)
{
    public override string ToString() => $"{Width} × {Height}";
}

public sealed record EncodingPlan(
    long MaximumBytes, long TargetBytes, long OverheadBytes, int VideoBitrate,
    int AudioBitrate, int AudioChannels, int AudioSampleRate,
    OutputResolution Resolution, string? Warning);

public enum JobStage { Inspecting, Encoding, Verifying, Retrying, Complete }

public sealed record CompressionProgress(
    JobStage Stage, string Message, int Attempt = 1, int MaximumAttempts = 1,
    int Pass = 0, double Percent = 0, string? Speed = null,
    EncodingPlan? Plan = null);

public sealed record CompressionResult(
    string OutputPath, long OriginalBytes, long OutputBytes, int Attempts,
    EncodingPlan Plan, TimeSpan Elapsed)
{
    public double SavedPercent => OriginalBytes > 0 ? 100.0 * (1 - (double)OutputBytes / OriginalBytes) : 0;
}

public sealed class CompressionException(string message, Exception? inner = null) : Exception(message, inner);
