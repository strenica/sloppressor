using System.Globalization;

namespace VideoSizeCompressor.Core;

public static class GifCommandBuilder
{
    public static IReadOnlyList<string> PalettePass(GifOptions options, GifPlan plan, string palettePath)
    {
        var args = Common(options, plan);
        AddClipLimit(args, plan);
        args.AddRange(["-map_metadata", "-1", "-map_chapters", "-1",
            "-vf", Filter(options, plan, palette: true), "-an", "-sn", "-dn",
            "-frames:v", "1", "-update", "1", "-f", "image2", palettePath]);
        return args;
    }

    public static IReadOnlyList<string> RenderPass(GifOptions options, GifPlan plan, string palettePath, string stagingPath)
    {
        var args = Common(options, plan);
        args.AddRange(["-i", palettePath]);
        AddClipLimit(args, plan);
        args.AddRange(["-filter_complex", Filter(options, plan, palette: false), "-map", "[out]",
            "-loop", "0", "-an", "-sn", "-dn", "-map_metadata", "-1", "-map_chapters", "-1",
            "-f", "gif", stagingPath]);
        return args;
    }

    private static string Filter(GifOptions options, GifPlan plan, bool palette)
    {
        string chain = $"fps={options.FramesPerSecond},scale={plan.Resolution.Width}:{plan.Resolution.Height}:flags=lanczos,setsar=1";
        // palettegen and paletteuse run in separate invocations. The palette is
        // built from the exact clip and geometry that the render pass uses.
        return palette
            ? $"{chain},palettegen=stats_mode=diff"
            : $"[0:v]{chain}[v];[v][1:v]paletteuse=dither=sierra2_4a:diff_mode=rectangle[out]";
    }

    private static List<string> Common(GifOptions options, GifPlan plan)
    {
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "warning", "-nostats", "-stats_period", "0.25", "-progress", "pipe:1",
            "-y", "-xerror", "-protocol_whitelist", "file,pipe"
        };
        if (plan.StartSeconds > 0) args.AddRange(["-ss", Seconds(plan.StartSeconds)]);
        args.AddRange(["-i", options.InputPath]);
        return args;
    }

    private static void AddClipLimit(List<string> args, GifPlan plan)
    {
        if (plan.DurationSeconds < plan.SourceDuration - 0.001)
            args.AddRange(["-t", Seconds(plan.DurationSeconds)]);
    }

    private static string Seconds(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
