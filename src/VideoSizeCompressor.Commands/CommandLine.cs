using System.Globalization;
using VideoSizeCompressor.Core;

namespace VideoSizeCompressor.Commands;

public sealed record ParsedCommand(string Command, CompressionOptions? Options = null, bool NoUi = false, bool Silent = false);

public static class CommandLine
{
    public const string Help = """
        VideoSizeCompressor 0.1.0 — a strict maximum, in decimal MB

        VideoSizeCompressor.exe compress "C:\video.mp4" --size 20
        VideoSizeCompressor.exe custom "C:\video.mp4"
        VideoSizeCompressor.Cli.exe compress "C:\video.mp4" --size 8

        compress <file> --size <MB>  Compress the complete video to MP4.
        custom <file>               Ask for the maximum size (Windows app only).
        register | unregister       Install/remove per-user Explorer entries (app only).

        --codec hevc|h264           Default: hevc / libx265.
        --output <new.mp4>          Existing files are never overwritten.
        --audio-bitrate <kbps>      Audio ceiling, 8–320; automatically reduced if needed.
        --safety-margin <percent>   Default: 2.5; valid range 0.5–25.
        --retries <0..5>            Additional attempts if oversized. Default: 3.
        --preset <name>            Default: medium (also fast, slow, etc.).
        --threads <1..64>           Default: available CPU threads minus one, capped at 16.
        --ffmpeg-dir <directory>    Folder containing ffmpeg and ffprobe (or a bin subfolder).
        --no-ui                    Run compression without dialogs; use the CLI exe for scripts.
        --help                     Show this help.

        CLI numbers use a dot for decimals: --size 7.5
        Settings: %LOCALAPPDATA%\VideoSizeCompressor\settings.json
        Exit codes: 0 success, 1 failure, 2 invalid arguments, 130 canceled.
        """;

    public static ParsedCommand Parse(string[] args, UserSettings? settings = null)
    {
        if (args.Length == 0) return new("home");
        if (args[0] is "--help" or "-h" or "help") return new("help");
        if (args[0] is "--version" or "version") return new("version");
        string command = args[0].ToLowerInvariant();
        if (command is "register" or "unregister")
        {
            if (args.Skip(1).Any(a => a != "--silent")) throw new CompressionException("Registration accepts only the optional --silent flag.");
            return new(command, Silent: args.Contains("--silent"));
        }
        if (command is not ("compress" or "custom")) throw new CompressionException("Unknown command. Use --help for examples.");
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1])) throw new CompressionException("Provide a video filename after the command.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool noUi = false;
        string[] valid = ["--size", "--codec", "--output", "--audio-bitrate", "--safety-margin", "--retries", "--preset", "--threads", "--ffmpeg-dir"];
        for (int i = 2; i < args.Length; i++)
        {
            string option = args[i];
            if (option == "--no-ui") { noUi = true; continue; }
            if (option == "--help") return new("help");
            if (!valid.Contains(option)) throw new CompressionException($"Unknown option: {option}");
            if (++i >= args.Length) throw new CompressionException($"Missing value for {option}.");
            if (!values.TryAdd(option, args[i])) throw new CompressionException($"Option {option} was specified more than once.");
        }
        if (command == "compress" && !values.ContainsKey("--size")) throw new CompressionException("Specify the maximum size, for example --size 20.");
        if (command == "custom" && (noUi || values.ContainsKey("--size"))) throw new CompressionException("Custom opens a size dialog. For automation, use compress with --size instead.");
        var defaults = settings ?? new UserSettings();
        var options = defaults.OptionsFor(args[1], values.TryGetValue("--size", out string? size) ? Decimal(size, "size") : 20m) with
        {
            Codec = values.GetValueOrDefault("--codec", defaults.Codec).ToLowerInvariant(),
            Preset = values.GetValueOrDefault("--preset", defaults.Preset).ToLowerInvariant(),
            OutputPath = values.GetValueOrDefault("--output"),
            FFmpegDirectory = values.TryGetValue("--ffmpeg-dir", out string? ffmpeg) ? ffmpeg : defaults.FFmpegDirectory,
            SafetyMarginPercent = values.TryGetValue("--safety-margin", out string? margin) ? Decimal(margin, "safety margin") : defaults.SafetyMarginPercent,
            MaxRetries = values.TryGetValue("--retries", out string? retries) ? Integer(retries, "retries") : defaults.MaxRetries,
            AudioBitrateKbps = values.TryGetValue("--audio-bitrate", out string? audio) ? Integer(audio, "audio bitrate") : defaults.AudioBitrateKbps,
            Threads = values.TryGetValue("--threads", out string? threads) ? Integer(threads, "threads") : defaults.Threads
        };
        options.Validate();
        return new(command, options, noUi);
    }

    private static decimal Decimal(string value, string name) => decimal.TryParse(value,
        NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out decimal result)
            ? result : throw new CompressionException($"Invalid {name}. Use a number with a decimal point, such as 7.5.");
    private static int Integer(string value, string name) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
        ? result : throw new CompressionException($"Invalid {name}. Use a whole number.");
}
