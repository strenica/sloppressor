using System.ComponentModel;
using System.Runtime.InteropServices;

namespace VideoSizeCompressor.Core;

public static class OutputFiles
{
    public static string ValidateDestination(CompressionOptions options, string input)
    {
        string destination = options.OutputPath is null
            ? Path.GetDirectoryName(input)!
            : Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!;
        if (!Directory.Exists(destination)) throw new CompressionException("The output folder does not exist. Choose an existing folder.");
        if (options.OutputPath is not null)
        {
            string output = Path.GetFullPath(options.OutputPath);
            if (!Path.GetExtension(output).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
                throw new CompressionException("The output filename must end in .mp4.");
            if (PathsEqual(input, output)) throw new CompressionException("The output path is the original file. Choose a different filename.");
            if (Path.Exists(output)) throw new CompressionException("The requested output already exists. Choose a new filename; existing files are never overwritten.");
        }
        return destination;
    }

    public static bool PathsEqual(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public static string CreateStagingFile(string directory)
    {
        string staging = Path.Combine(directory, $".vsc-{Guid.NewGuid():N}.partial.mp4");
        // Fail early for read-only directories and reserve this job's unique file.
        using (new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        return staging;
    }

    public static string Candidate(string input, string sizeLabel, int sequence)
    {
        string stem = Path.GetFileNameWithoutExtension(input);
        // Leave room under NTFS's component limit for size, collision suffix and .mp4.
        // Do not split a UTF-16 surrogate pair in a long Unicode filename.
        if (stem.Length > 180)
        {
            int length = char.IsHighSurrogate(stem[179]) ? 179 : 180;
            stem = stem[..length];
        }
        string number = sequence == 1 ? "" : $"_{sequence}";
        return Path.Combine(Path.GetDirectoryName(input)!, $"{stem}_{sizeLabel}MB{number}.mp4");
    }

    public static string CommitVerified(string staging, string input, CompressionOptions options)
    {
        long length = new FileInfo(staging).Length;
        // The final write gate is intentionally independent of planner estimates.
        if (length <= 0 || length >= options.MaximumBytes)
            throw new CompressionException("Output failed the strict maximum-size check. No final file was created.");
        if (options.OutputPath is not null)
        {
            string output = Path.GetFullPath(options.OutputPath);
            MoveNoReplace(staging, output);
            return output;
        }
        for (int sequence = 1; sequence <= 100_000; sequence++)
        {
            string candidate = Candidate(input, options.SizeLabel, sequence);
            try
            {
                // Same-folder rename, no overwrite. If another job wins this name
                // between the check and move, try the next name without deleting it.
                MoveNoReplace(staging, candidate);
                return candidate;
            }
            catch (IOException) when (Path.Exists(candidate)) { }
        }
        throw new CompressionException("Too many output files use this name. Choose an explicit output filename.");
    }

    private static void MoveNoReplace(string source, string destination)
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows implements this with a no-replace native move.
            File.Move(source, destination, overwrite: false);
            return;
        }
        // The cross-platform CLI is also tested on Unix. A check followed by a
        // rename can overwrite a racing destination on some Unix filesystems.
        // link(2) creates the final directory entry atomically ONLY if absent;
        // the staging file is in the same folder/filesystem and already verified.
        // Fail safely if that filesystem cannot create hard links.
        if (Link(source, destination) != 0)
            throw new IOException("Could not publish output without replacing an existing file.", new Win32Exception(Marshal.GetLastPInvokeError()));
        File.Delete(source);
    }

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link([MarshalAs(UnmanagedType.LPUTF8Str)] string source, [MarshalAs(UnmanagedType.LPUTF8Str)] string destination);

    public static async Task CleanupAsync(string? staging, string? jobDirectory, JobLog log)
    {
        foreach (string path in new[] { staging, jobDirectory }.OfType<string>())
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                    else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (attempt == 2) log.Add($"Cleanup could not remove {path}: {ex.Message}");
                    else await Task.Delay(150 * (attempt + 1), CancellationToken.None);
                }
            }
        }
    }

    public static void CheckSpace(string directory, long needed)
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(directory))!);
            if (drive.IsReady && drive.AvailableFreeSpace < needed)
                throw new CompressionException($"Not enough free space in {directory}. Allow at least {needed / 1_000_000.0:N0} MB for this job.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Some network shares cannot report free space. Real writes still fail
            // safely and the job will remove its own partial files.
        }
    }
}
