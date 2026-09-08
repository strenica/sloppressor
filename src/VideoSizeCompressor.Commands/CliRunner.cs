using System.Diagnostics;
using VideoSizeCompressor.Core;

namespace VideoSizeCompressor.Commands;

public static class CliRunner
{
    public static async Task<int> RunAsync(ParsedCommand command, CancellationToken token)
    {
        if (command.Command is "help" or "home") { Console.WriteLine(CommandLine.Help); return 0; }
        if (command.Command == "version") { Console.WriteLine("VideoSizeCompressor 0.1.0"); return 0; }
        if (command.Command != "compress" || command.Options is null)
        {
            Console.Error.WriteLine("This command needs the Windows app. Use VideoSizeCompressor.exe, or compress with --size for automation.");
            return 2;
        }
        var engine = new CompressionEngine();
        var reporter = new ConsoleProgress();
        try
        {
            CompressionResult result = await engine.CompressAsync(command.Options, reporter, token);
            Console.WriteLine($"Compression complete\nOriginal: {result.OriginalBytes / 1_000_000.0:F2} MB\n" +
                $"Compressed: {result.OutputBytes / 1_000_000.0:F2} MB ({result.OutputBytes} bytes)\n" +
                $"Saved: {result.SavedPercent:F1}%\nOutput: {result.OutputPath}");
            return 0;
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("Canceled. Incomplete output has been removed."); return 130; }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Compression failed: {ex.Message}\n\nDetailed log:\n{engine.Log}");
            return 1;
        }
    }

    private sealed class ConsoleProgress : IProgress<CompressionProgress>
    {
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private TimeSpan previousTime;
        private (JobStage, int, int)? previousState;
        public void Report(CompressionProgress value)
        {
            var state = (value.Stage, value.Attempt, value.Pass);
            if (state == previousState && clock.Elapsed - previousTime < TimeSpan.FromSeconds(1)) return;
            previousState = state;
            previousTime = clock.Elapsed;
            Console.Error.WriteLine($"[{clock.Elapsed:hh\\:mm\\:ss}] {value.Message} " +
                $"| attempt {value.Attempt}/{value.MaximumAttempts} | {value.Percent:F1}% | {value.Speed ?? "—"} " +
                (value.Plan is { } plan ? $"| {plan.Resolution}" : ""));
        }
    }
}
