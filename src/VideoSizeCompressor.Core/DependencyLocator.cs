using System.Text.RegularExpressions;

namespace VideoSizeCompressor.Core;

public sealed record ToolPaths(string FFmpeg, string FFprobe);

public static class DependencyLocator
{
    public static ToolPaths Find(string? explicitDirectory = null, string? baseDirectory = null)
    {
        string exe = OperatingSystem.IsWindows() ? ".exe" : "";
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
        {
            string directory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(explicitDirectory));
            return TryDirectory(directory, exe) ?? TryDirectory(Path.Combine(directory, "bin"), exe)
                ?? throw new CompressionException("The configured FFmpeg folder must contain both ffmpeg and ffprobe executables (directly or in bin). Fix --ffmpeg-dir or settings.json.");
        }
        string root = baseDirectory ?? AppContext.BaseDirectory;
        foreach (string dir in new[] { Path.Combine(root, "ffmpeg"), Path.Combine(root, "ffmpeg", "bin") })
            if (TryDirectory(dir, exe) is { } pair) return pair;

        // Do not search the working directory or empty/relative PATH entries. Opening
        // a video in an untrusted folder must not execute its adjacent ffmpeg.exe.
        foreach (string entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            string directory = Environment.ExpandEnvironmentVariables(entry.Trim().Trim('"'));
            if (Path.IsPathFullyQualified(directory) && TryDirectory(directory, exe) is { } pair) return pair;
        }
        throw new CompressionException("FFmpeg and FFprobe were not found. Put both executables in the application's ffmpeg folder, choose their folder in the app, or install them on PATH. A build with libx265 and AAC is required.");
    }

    private static ToolPaths? TryDirectory(string directory, string extension)
    {
        string ffmpeg = Path.Combine(directory, "ffmpeg" + extension);
        string ffprobe = Path.Combine(directory, "ffprobe" + extension);
        return File.Exists(ffmpeg) && File.Exists(ffprobe) ? new(Path.GetFullPath(ffmpeg), Path.GetFullPath(ffprobe)) : null;
    }

    public static async Task VerifyAsync(ToolPaths paths, IVideoCodec codec, IProcessRunner runner, JobLog log, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var result = await runner.RunAsync(new(paths.FFmpeg, ["-hide_banner", "-encoders"], Path.GetTempPath(), CaptureOutput: true), log, null, timeout.Token);
            if (result.ExitCode != 0) throw new CompressionException("FFmpeg could not list its encoders. Check that the FFmpeg installation is complete.");
            foreach (string encoder in new[] { codec.Encoder, "aac" })
                if (!Regex.IsMatch(result.StandardOutput, @"(?m)^\s*[A-Z.]{6}\s+" + Regex.Escape(encoder) + @"\s"))
                    throw new CompressionException($"This FFmpeg build does not include {encoder}. Install a compatible GPL-enabled FFmpeg build, or choose another supported codec.");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new CompressionException("FFmpeg took too long to start. Check the installation and antivirus activity."); }
    }
}
