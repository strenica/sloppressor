using System.Diagnostics;

namespace VideoSizeCompressor.Core;

public sealed class CompressionEngine
{
    private readonly IProcessRunner runner;
    private readonly IMediaProbe probe;
    private readonly Func<string?, ToolPaths> resolveTools;
    private readonly string temporaryRoot;
    public JobLog Log { get; } = new();

    public CompressionEngine(IProcessRunner? runner = null, IMediaProbe? probe = null,
        Func<string?, ToolPaths>? resolveTools = null, string? temporaryRoot = null)
    {
        this.runner = runner ?? new ProcessRunner();
        this.probe = probe ?? new MediaProbe(this.runner);
        this.resolveTools = resolveTools ?? (directory => DependencyLocator.Find(directory));
        this.temporaryRoot = temporaryRoot ?? Path.Combine(Path.GetTempPath(), "VideoSizeCompressor");
    }

    public async Task<CompressionResult> CompressAsync(CompressionOptions options,
        IProgress<CompressionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        options.Validate();
        var watch = Stopwatch.StartNew();
        string? staging = null, jobDirectory = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string input = Path.GetFullPath(options.InputPath);
            if (!File.Exists(input)) throw new CompressionException("The input video does not exist or cannot be accessed.");
            string destination = OutputFiles.ValidateDestination(options, input);
            using var inputGuard = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read);
            DateTime modified = File.GetLastWriteTimeUtc(input);
            long originalSize = inputGuard.Length;
            var codec = VideoCodecs.Get(options.Codec);
            progress?.Report(new(JobStage.Inspecting, "Inspecting video and checking encoders…"));
            ToolPaths tools = resolveTools(options.FFmpegDirectory);
            await DependencyLocator.VerifyAsync(tools, codec, runner, Log, cancellationToken);
            MediaInfo media = await probe.InspectAsync(input, tools, Log, cancellationToken);
            EncodingPlan plan = EncodingPlanner.Create(media, options);
            Log.Add($"Input: {media.Video.Width}x{media.Video.Height}, {media.Duration:F3} s, {media.Video.FrameRate:F3} fps, " +
                $"source bitrate {media.Bitrate}, {originalSize} bytes; {media.AudioStreamCount} audio, {media.SubtitleStreamCount} subtitle streams.");
            long estimatedOutput = (long)Math.Ceiling(Math.Min(plan.TargetBytes,
                (plan.VideoBitrate + (double)plan.AudioBitrate) * media.Duration / 8 + plan.OverheadBytes));
            OutputFiles.CheckSpace(destination, checked(estimatedOutput * 2 + 32_000_000));
            Directory.CreateDirectory(temporaryRoot);
            OutputFiles.CheckSpace(temporaryRoot, checked((long)(media.Duration * media.Video.FrameRate * 256) + 32_000_000));
            jobDirectory = Path.Combine(temporaryRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(jobDirectory);
            staging = OutputFiles.CreateStagingFile(destination);
            int maximumAttempts = options.MaxRetries + 1;

            for (int attempt = 1; attempt <= maximumAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string attemptDirectory = Path.Combine(jobDirectory, $"attempt-{attempt}");
                Directory.CreateDirectory(attemptDirectory);
                Log.Add($"Attempt {attempt}/{maximumAttempts}: maximum {plan.MaximumBytes} bytes, target {plan.TargetBytes} bytes, " +
                    $"video {plan.VideoBitrate} bps, audio {plan.AudioBitrate} bps, resolution {plan.Resolution}.");
                if (plan.Warning is not null) Log.Add(plan.Warning);
                long[] frameCounts = new long[2];
                for (int pass = 1; pass <= 2; pass++)
                {
                    int activePass = pass;
                    var parser = new ProgressParser();
                    progress?.Report(new(JobStage.Encoding, $"Encoding pass {pass} of 2", attempt, maximumAttempts,
                        pass, (pass - 1) * 50, Plan: plan));
                    var request = new ProcessRequest(tools.FFmpeg,
                        FFmpegCommandBuilder.Build(options, media, plan, pass, staging), attemptDirectory, Interactive: true);
                    ProcessResult encoded = await runner.RunAsync(request, Log, line =>
                    {
                        if (parser.Push(line) is not { } update) return;
                        frameCounts[activePass - 1] = update.Frame;
                        double fraction = Math.Clamp(update.Seconds / media.Duration, 0, 1);
                        // A completed pass is known, but the job is never 100% until
                        // validation and the final rename have both succeeded.
                        double percent = Math.Min(99.5, ((activePass - 1) + fraction) * 50);
                        progress?.Report(new(JobStage.Encoding, $"Encoding pass {activePass} of 2", attempt, maximumAttempts,
                            activePass, percent, update.Speed, plan));
                    }, cancellationToken);
                    if (encoded.ExitCode != 0)
                        throw new CompressionException($"FFmpeg failed during pass {pass}. Check free space and view the detailed log for the decoder or encoder error.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new(JobStage.Verifying, "Verifying file size and video completeness…", attempt, maximumAttempts, 2, 99.5, Plan: plan));
                if (!File.Exists(staging) || new FileInfo(staging).Length == 0)
                    throw new CompressionException("FFmpeg did not produce a usable output file.");
                MediaInfo output = await probe.InspectAsync(staging, tools, Log, cancellationToken);
                ValidateCompletedVideo(media, output, frameCounts, plan);
                if (File.GetLastWriteTimeUtc(input) != modified || inputGuard.Length != originalSize)
                    throw new CompressionException("The source file changed during compression. No output was committed; retry with a stable copy.");
                if (output.FileSize < options.MaximumBytes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string final = OutputFiles.CommitVerified(staging, input, options);
                    staging = null;
                    Log.Add($"Complete: {final} ({output.FileSize} bytes, strictly below {options.MaximumBytes}).");
                    progress?.Report(new(JobStage.Complete, "Compression complete", attempt, maximumAttempts, 2, 100, Plan: plan));
                    return new(final, originalSize, output.FileSize, attempt, plan, watch.Elapsed);
                }
                if (attempt == maximumAttempts)
                    throw new CompressionException($"The encoder could not meet the maximum after {maximumAttempts} attempts. Try a larger size or a slower preset. No oversized final file was created.");
                plan = EncodingPlanner.Retry(plan, media, output, options);
                progress?.Report(new(JobStage.Retrying, "Output exceeded the maximum; retrying with a lower bitrate…",
                    attempt + 1, maximumAttempts, Plan: plan));
                // Remove the oversized attempt now; pass 2 will create a fresh file.
                File.Delete(staging);
            }
            throw new InvalidOperationException("Encoding attempts ended unexpectedly.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Add(ex.ToString());
            throw new CompressionException("The file could not be read or written. Check folder permissions, available disk space, filename length, and whether another app has locked the file.", ex);
        }
        catch (Exception ex)
        {
            Log.Add(ex.ToString());
            throw;
        }
        finally
        {
            await OutputFiles.CleanupAsync(staging, jobDirectory, Log);
        }
    }

    private static void ValidateCompletedVideo(MediaInfo input, MediaInfo output, long[] frames, EncodingPlan plan)
    {
        if (frames[0] <= 0 || frames[0] != frames[1])
            throw new CompressionException("The encoding passes produced different video frame counts. The output was rejected to prevent incomplete video.");
        if (output.Video.FrameCount is { } count && count != frames[1])
            throw new CompressionException("The completed MP4 does not contain all encoded video frames.");
        // Stream duration, when available, describes the selected video. Format
        // duration can include a longer alternate audio stream, so do not use that
        // fallback as a strict completeness assertion. Both passes must still agree.
        double tolerance = Math.Max(0.5, 3.0 / Math.Max(1, input.Video.FrameRate));
        if (input.Video.Duration is { } duration && output.Duration + tolerance < duration)
            throw new CompressionException("The completed video is shorter than the source video. It was rejected rather than saved as a truncated result.");
        if (output.Video.Width != plan.Resolution.Width || output.Video.Height != plan.Resolution.Height)
            throw new CompressionException("The output resolution differs from the encoding plan.");
        if (input.Audio is not null && output.Audio is null)
            throw new CompressionException("The output is missing the selected audio track.");
    }
}
