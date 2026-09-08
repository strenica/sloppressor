using System.Text;

namespace VideoSizeCompressor.Core;

public sealed class JobLog
{
    private const int Capacity = 512 * 1024;
    private readonly Queue<string> lines = new();
    private readonly object gate = new();
    private int length;
    private bool truncated;

    public void Add(string text)
    {
        text = text.Length > 8_192 ? text[..8_192] + " [line shortened]" : text;
        lock (gate)
        {
            lines.Enqueue(text);
            length += text.Length + 1;
            while (length > Capacity && lines.TryDequeue(out string? old))
            {
                length -= old.Length + 1;
                truncated = true;
            }
        }
    }

    public override string ToString()
    {
        lock (gate)
        {
            var result = new StringBuilder();
            if (truncated) result.AppendLine("[Earlier log lines omitted; most recent 512 KiB retained.]");
            foreach (string line in lines) result.AppendLine(line);
            return result.ToString();
        }
    }
}
