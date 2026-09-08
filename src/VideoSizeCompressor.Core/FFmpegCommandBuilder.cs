using System.Globalization;

namespace VideoSizeCompressor.Core;

public static class FFmpegCommandBuilder
{
    public static IReadOnlyList<string> Build(CompressionOptions options, MediaInfo media, EncodingPlan plan, int pass, string stagingPath)
    {
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "warning", "-nostats", "-stats_period", "0.25", "-progress", "pipe:1",
            "-y", "-xerror", "-protocol_whitelist", "file,pipe", "-threads", options.Threads.ToString(CultureInfo.InvariantCulture),
            "-i", media.Path, "-map", $"0:{media.Video.Index}", "-map_metadata", "-1", "-map_chapters", "-1", "-sn", "-dn",
            "-vf", $"scale={plan.Resolution.Width}:{plan.Resolution.Height}:flags=lanczos,setsar=1",
            "-pix_fmt", "yuv420p", "-fps_mode", "passthrough", "-threads", options.Threads.ToString(CultureInfo.InvariantCulture)
        };
        VideoCodecs.Get(options.Codec).AddArguments(args, options, plan, pass);
        if (pass == 1)
            args.AddRange(["-an", "-f", "null", "-"]);
        else
        {
            if (media.Audio is { } audio && plan.AudioBitrate > 0)
                args.AddRange(["-map", $"0:{audio.Index}", "-c:a", "aac", "-b:a", plan.AudioBitrate.ToString(CultureInfo.InvariantCulture),
                    "-ac", plan.AudioChannels.ToString(CultureInfo.InvariantCulture), "-ar", plan.AudioSampleRate.ToString(CultureInfo.InvariantCulture),
                    "-af", "apad", "-shortest"]);
            else args.Add("-an");
            // Audio is padded so -shortest ends at VIDEO EOF, never at a short audio
            // track. No -fs, -t, frame dropping, or size-based truncation is used.
            args.AddRange(["-metadata:s:v:0", "rotate=0", "-movflags", "+faststart", "-f", "mp4", stagingPath]);
        }
        return args;
    }
}
