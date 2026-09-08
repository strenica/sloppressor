using System.Diagnostics;
using VideoSizeCompressor.Core;

namespace VideoSizeCompressor.App;

internal sealed class ProgressForm : Form
{
    private readonly CompressionOptions options;
    private readonly CompressionEngine engine = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly Stopwatch clock = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 500 };
    private readonly Label title = Ui.Label("Preparing your video", 20, true);
    private readonly Label status = Ui.Label("Inspecting the source…");
    private readonly Label resolution = Ui.Label("Inspecting…");
    private readonly Label pass = Ui.Label("—");
    private readonly Label elapsed = Ui.Label("00:00:00");
    private readonly Label speed = Ui.Label("—");
    private readonly Label percent = Ui.Label("0%", 12, true);
    private readonly ProgressBar bar = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 1000, Style = ProgressBarStyle.Blocks, AccessibleName = "Encoding progress" };
    private readonly TextBox message = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
        BorderStyle = BorderStyle.None, BackColor = SystemColors.Window, ScrollBars = ScrollBars.Vertical, TabStop = true };
    private readonly Button cancel = Ui.Button("Cancel");
    private readonly Button folder = Ui.Button("Open Folder", true);
    private bool finished;
    private bool closeWhenStopped;
    private string? outputPath;
    public int ExitCode { get; private set; } = 1;

    public ProgressForm(CompressionOptions options)
    {
        this.options = options;
        Ui.Configure(this, "VideoSizeCompressor", new Size(640, 690));
        MaximizeBox = false;
        var panel = Ui.Column();
        Ui.Add(panel, title);
        var file = Ui.Label(Path.GetFileName(options.InputPath), 11);
        file.AutoEllipsis = true; file.AutoSize = false;
        Ui.Add(panel, file, 34);
        Ui.Add(panel, status, 40);
        var facts = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 0, Margin = Padding.Empty };
        facts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        facts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddFact(facts, "Maximum output", Ui.Label($"{options.SizeLabel} MB · decimal"));
        AddFact(facts, "Video codec", Ui.Label(VideoCodecs.Get(options.Codec).DisplayName));
        AddFact(facts, "Output resolution", resolution);
        AddFact(facts, "Encoding pass", pass);
        AddFact(facts, "Elapsed", elapsed);
        AddFact(facts, "FFmpeg speed", speed);
        Ui.Add(panel, facts);
        Ui.Add(panel, percent, 30);
        Ui.Add(panel, bar, 18);
        message.Margin = new Padding(0, 15, 0, 0);
        Ui.Add(panel, message, 130);
        var details = Ui.Button("View Log");
        details.Click += (_, _) => { using var log = new LogDialog(engine.Log.ToString()); log.ShowDialog(this); };
        cancel.Click += (_, _) => { if (finished) Close(); else BeginCancel(); };
        folder.Visible = false;
        folder.Click += (_, _) => { if (outputPath is not null) Ui.OpenFolder(outputPath); };
        Ui.Add(panel, Ui.Buttons(cancel, folder, details));
        Controls.Add(panel);
        timer.Tick += (_, _) => elapsed.Text = clock.Elapsed.ToString(@"hh\:mm\:ss");
        Shown += Run;
        FormClosing += (_, e) =>
        {
            if (!finished) { e.Cancel = true; closeWhenStopped = true; BeginCancel(); }
        };
        FormClosed += (_, _) => { timer.Dispose(); cancellation.Dispose(); };
    }

    private static void AddFact(TableLayoutPanel table, string name, Label value)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = Ui.Label(name);
        label.ForeColor = SystemColors.GrayText;
        table.Controls.Add(label, 0, row);
        table.Controls.Add(value, 1, row);
    }

    private void BeginCancel()
    {
        if (cancellation.IsCancellationRequested) return;
        status.Text = "Canceling and removing incomplete files…";
        cancel.Enabled = false;
        cancellation.Cancel();
    }

    private async void Run(object? sender, EventArgs args)
    {
        clock.Start(); timer.Start();
        var progress = new Progress<CompressionProgress>(update =>
        {
            if (IsDisposed || finished || cancellation.IsCancellationRequested) return;
            title.Text = update.Stage == JobStage.Retrying ? "Adjusting the bitrate" : "Compressing your video";
            status.Text = update.Message;
            pass.Text = update.Pass > 0 ? $"{update.Pass} of 2 · attempt {update.Attempt}/{update.MaximumAttempts}" : "—";
            bar.Value = (int)Math.Clamp(update.Percent * 10, 0, 1000);
            percent.Text = $"{update.Percent:F1}%";
            speed.Text = update.Speed ?? "—";
            if (update.Plan is { } plan)
            {
                resolution.Text = plan.Resolution.ToString();
                message.Text = plan.Warning ?? "The complete video will be checked against your size limit before it is saved.";
            }
        });
        try
        {
            CompressionResult result = await engine.CompressAsync(options, progress, cancellation.Token);
            outputPath = result.OutputPath;
            ExitCode = 0;
            title.Text = "Compression complete";
            status.Text = "Saved beside your video.";
            if (options.OutputPath is not null) status.Text = "Saved to your chosen output folder.";
            bar.Value = 1000; percent.Text = "100%";
            message.Text = $"Original: {result.OriginalBytes / 1_000_000.0:F2} MB\r\n" +
                $"Compressed: {result.OutputBytes / 1_000_000.0:F2} MB · {result.OutputBytes:N0} bytes\r\n" +
                $"Saved: {result.SavedPercent:F1}%\r\n\r\n{result.OutputPath}";
            folder.Visible = true;
        }
        catch (OperationCanceledException)
        {
            ExitCode = 130;
            title.Text = "Compression canceled";
            status.Text = "Your original video is safe.";
            message.Text = "Incomplete output and encoding statistics have been removed. Check View Log if Windows prevented cleanup.";
        }
        catch (Exception ex)
        {
            ExitCode = 1;
            title.Text = "Could not compress this video";
            status.Text = "Your original video is safe.";
            message.Text = ex.Message + "\r\n\r\nUse View Log to inspect or copy the detailed FFmpeg output.";
        }
        finally
        {
            finished = true;
            clock.Stop(); timer.Stop();
            cancel.Text = "Close"; cancel.Enabled = true;
            AcceptButton = folder.Visible ? folder : cancel;
            if (closeWhenStopped) Close();
        }
    }
}
