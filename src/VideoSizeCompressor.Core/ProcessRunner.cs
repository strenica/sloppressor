using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace VideoSizeCompressor.Core;

public sealed record ProcessRequest(
    string Executable, IReadOnlyList<string> Arguments, string WorkingDirectory,
    bool Interactive = false, bool CaptureOutput = false);

public sealed record ProcessResult(int ExitCode, string StandardOutput);

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, JobLog log, Action<string>? onOutput, CancellationToken cancellationToken);
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessRequest request, JobLog log, Action<string>? onOutput, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo
        {
            FileName = request.Executable,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = request.Interactive,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string argument in request.Arguments) info.ArgumentList.Add(argument);
        // JSON is solely a diagnostic representation, NEVER a shell command.
        log.Add($"Run: {request.Executable}\nArguments: {System.Text.Json.JsonSerializer.Serialize(request.Arguments)}");
        using var process = new Process { StartInfo = info };
        try { process.Start(); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            throw new CompressionException($"Could not start {Path.GetFileName(request.Executable)}. Check its location, Windows architecture, and permissions.", ex);
        }

        const int captureLimit = 8 * 1024 * 1024;
        var captured = new StringBuilder();
        bool captureOverflow = false;
        Exception? callbackError = null;
        Task output = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                if (request.CaptureOutput)
                {
                    if (captured.Length + line.Length < captureLimit) captured.AppendLine(line);
                    else captureOverflow = true; // Keep draining to avoid pipe deadlock.
                }
                try { onOutput?.Invoke(line); }
                catch (Exception ex) { callbackError ??= ex; }
            }
        }, CancellationToken.None);
        Task error = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync() is { } line) log.Add(line);
        }, CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            log.Add("Cancellation requested; asking FFmpeg to quit.");
            if (request.Interactive && !process.HasExited)
            {
                try
                {
                    await process.StandardInput.WriteLineAsync("q");
                    await process.StandardInput.FlushAsync();
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException) { log.Add(ex.Message); }
            }
            Task exited = process.WaitForExitAsync(CancellationToken.None);
            if (await Task.WhenAny(exited, Task.Delay(TimeSpan.FromSeconds(3), CancellationToken.None)) != exited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(output, error);
            throw;
        }
        await Task.WhenAll(output, error);
        cancellationToken.ThrowIfCancellationRequested();
        if (captureOverflow) throw new CompressionException("FFprobe returned an unusually large response. This file's metadata cannot be processed safely.");
        if (callbackError is not null) throw new CompressionException("Could not process encoder progress.", callbackError);
        log.Add($"Exit code: {process.ExitCode}");
        return new(process.ExitCode, captured.ToString());
    }
}
