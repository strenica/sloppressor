using System.Diagnostics;

namespace VideoSizeCompressor.Core;

public sealed class GifConverter
{
    private readonly IProcessRunner runner;
    private readonly IMediaProbe probe;
    private readonly Func<string?, ToolPaths> resolveTools;
    private readonly string temporaryRoot;
    public JobLog Log { get; } = new();

    public GifConverter(IProcessRunner? runner = null, IMediaProbe? probe = null,
        Func<string?, ToolPaths>? resolveTools = null, string? temporaryRoot = null)
    {
        this.runner = runner ?? new ProcessRunner();
        this.probe = probe ?? new MediaProbe(this.runner);
        this.resolveTools = resolveTools ?? (directory => DependencyLocator.Find(directory));
        this.temporaryRoot = temporaryRoot ?? Path.Combine(Path.GetTempPath(), "VideoSizeCompressor");
    }

    public async Task<GifResult> ConvertAsync(GifOptions options, IProgress<GifProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options.Validate();
        var watch = Stopwatch.StartNew();
        string? staging = null, jobDirectory = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string input = Path.GetFullPath(options.InputPath);
            if (!File.Exists(input)) throw new CompressionException("The input video does not exist or cannot be accessed.");
            string destination = OutputFiles.ValidateGifDestination(options, input);
            using var inputGuard = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read);
            long originalBytes = inputGuard.Length;
            ToolPaths tools = resolveTools(options.FFmpegDirectory);
            await DependencyLocator.VerifyGifAsync(tools, runner, Log, cancellationToken);

            progress?.Report(new(JobStage.Inspecting, "Inspecting the video…"));
            MediaInfo media = await probe.InspectAsync(input, tools, Log, cancellationToken);
            GifPlan plan = GifPlan.Create(media, options);
            Log.Add($"GIF input: {plan.Resolution.Width}x{plan.Resolution.Height}, {plan.DurationSeconds:F3} s of " +
                $"{plan.SourceDuration:F3} s from {plan.StartSeconds:F3} s, {options.FramesPerSecond} fps, {originalBytes} bytes.");
            long estimatedBytes = (long)Math.Ceiling(
                (double)plan.Resolution.Width * plan.Resolution.Height * options.FramesPerSecond * plan.DurationSeconds * 0.35) + 16_000_000;
            OutputFiles.CheckSpace(destination, estimatedBytes);
            OutputFiles.CheckSpace(temporaryRoot, 32_000_000);

            Directory.CreateDirectory(temporaryRoot);
            jobDirectory = Path.Combine(temporaryRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(jobDirectory);
            string palette = Path.Combine(jobDirectory, "palette.png");
            staging = OutputFiles.CreateStagingFile(destination, ".gif");

            progress?.Report(new(JobStage.Encoding, "Building the GIF color palette…", 1, 0, Plan: plan));
            int paletteExit = await RunPassAsync(tools.FFmpeg, GifCommandBuilder.PalettePass(options, plan, palette),
                jobDirectory, 1, plan, progress, cancellationToken);
            if (paletteExit != 0 || !File.Exists(palette) || new FileInfo(palette).Length == 0)
                throw new CompressionException("FFmpeg could not build a GIF color palette. View the detailed log for the encoder error.");

            progress?.Report(new(JobStage.Encoding, "Rendering GIF frames…", 2, 30, Plan: plan));
            int renderExit = await RunPassAsync(tools.FFmpeg, GifCommandBuilder.RenderPass(options, plan, palette, staging),
                jobDirectory, 2, plan, progress, cancellationToken);
            if (renderExit != 0)
                throw new CompressionException("FFmpeg could not finish the GIF. View the detailed log for the encoder error.");

            progress?.Report(new(JobStage.Verifying, "Checking the finished GIF…", 2, 100, Plan: plan));
            MediaInfo output = await probe.InspectAsync(staging, tools, Log, cancellationToken);
            if (output.Video.Width != plan.Resolution.Width || output.Video.Height != plan.Resolution.Height)
                throw new CompressionException($"The finished GIF is {output.Video.Width}×{output.Video.Height}, " +
                    $"not the planned {plan.Resolution}.");
            double tolerance = Math.Max(0.5, 2.0 / options.FramesPerSecond);
            if (output.Duration + tolerance < plan.DurationSeconds)
                throw new CompressionException("The finished GIF is shorter than the selected clip. " +
                    "The incomplete output was not saved.");
            long outputBytes = new FileInfo(staging).Length;
            if (outputBytes <= 0) throw new CompressionException("The finished GIF is empty. No output was saved.");

            string published = OutputFiles.CommitGif(staging, input, options);
            staging = null;
            progress?.Report(new(JobStage.Complete, "GIF complete.", 2, 100, Plan: plan));
            return new(published, outputBytes, plan.StartSeconds, plan.DurationSeconds, plan.SourceDuration,
                plan.Resolution, options.FramesPerSecond, watch.Elapsed);
        }
        finally
        {
            await OutputFiles.CleanupAsync(staging, jobDirectory, Log);
        }
    }

    private async Task<int> RunPassAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory,
        int pass, GifPlan plan, IProgress<GifProgress>? progress, CancellationToken cancellationToken)
    {
        var parser = new ProgressParser();
        void OnOutput(string line)
        {
            if (parser.Push(line) is not { } update) return;
            double fraction = plan.DurationSeconds > 0
                ? Math.Clamp(update.Seconds / plan.DurationSeconds, 0, 1) : 0;
            double percent = pass == 1 ? fraction * 30 : 30 + fraction * 70;
            progress?.Report(new(JobStage.Encoding,
                pass == 1 ? "Building the GIF color palette…" : "Rendering GIF frames…",
                pass, percent, update.Speed, plan));
        }
        ProcessResult result = await runner.RunAsync(
            new(executable, arguments, workingDirectory, Interactive: true), Log, OnOutput, cancellationToken);
        Log.Add($"GIF pass {pass} exit code: {result.ExitCode}");
        return result.ExitCode;
    }
}
