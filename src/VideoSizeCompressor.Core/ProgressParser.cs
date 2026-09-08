using System.Globalization;

namespace VideoSizeCompressor.Core;

public sealed record EncoderProgress(double Seconds, long Frame, string? Speed, bool End);

public sealed class ProgressParser
{
    private double seconds;
    private long frame;
    private string? speed;

    public EncoderProgress? Push(string line)
    {
        int separator = line.IndexOf('=');
        if (separator < 0) return null;
        string key = line[..separator].Trim();
        string value = line[(separator + 1)..].Trim();
        switch (key)
        {
            // Despite its name, FFmpeg's older out_time_ms field is also microseconds.
            case "out_time_us":
            case "out_time_ms":
                if (long.TryParse(value, CultureInfo.InvariantCulture, out long us)) seconds = Math.Max(0, us / 1_000_000.0);
                break;
            case "frame":
                if (long.TryParse(value, CultureInfo.InvariantCulture, out long f)) frame = f;
                break;
            case "speed": speed = value == "N/A" ? null : value; break;
            case "progress": return new(seconds, frame, speed, value == "end");
        }
        return null;
    }
}
