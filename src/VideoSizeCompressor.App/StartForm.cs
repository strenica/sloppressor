using System.Diagnostics;
using VideoSizeCompressor.Commands;
using VideoSizeCompressor.Core;

namespace VideoSizeCompressor.App;

internal sealed class StartForm : Form
{
    private UserSettings settings;
    private readonly Label dependency = Ui.Label("");

    public StartForm(UserSettings settings)
    {
        this.settings = settings;
        Ui.Configure(this, "VideoSizeCompressor", new Size(600, 565));
        var panel = Ui.Column();
        Ui.Add(panel, Ui.Label("A smaller video.\nA size you choose.", 22, true));
        Ui.Add(panel, Ui.Label("Compress a video to a strict maximum size, without a command prompt.\nYour original file stays untouched."), 60);
        var choose = Ui.Button("Choose Video…", true);
        choose.Click += (_, _) =>
        {
            using var picker = new OpenFileDialog { Title = "Choose a video", CheckFileExists = true,
                Filter = "Video files|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.m4v;*.mpg;*.mpeg;*.ts|All files|*.*" };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            using var size = new CustomSizeDialog();
            if (size.ShowDialog(this) != DialogResult.OK) return;
            using var progress = new ProgressForm(this.settings.OptionsFor(picker.FileName, size.MaximumMegabytes));
            Hide();
            try { progress.ShowDialog(); }
            finally { Show(); }
        };
        Ui.Add(panel, Ui.Buttons(choose));
        dependency.AutoEllipsis = true; dependency.AutoSize = false;
        dependency.Margin = new Padding(0, 20, 0, 8);
        Ui.Add(panel, dependency, 62);
        var ffmpeg = Ui.Button("Choose FFmpeg Folder…");
        ffmpeg.Click += (_, _) =>
        {
            using var picker = new FolderBrowserDialog { Description = "Select the folder containing ffmpeg.exe and ffprobe.exe", UseDescriptionForTitle = true };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                ToolPaths tools = DependencyLocator.Find(picker.SelectedPath);
                this.settings = this.settings with { FFmpegDirectory = Path.GetDirectoryName(tools.FFmpeg) };
                this.settings.Save(); RefreshDependency();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "FFmpeg setup", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        var download = new LinkLabel { Text = "FFmpeg downloads", AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 12) };
        download.LinkClicked += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("https://ffmpeg.org/download.html") { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Could not open browser"); }
        };
        Ui.Add(panel, Ui.Buttons(ffmpeg));
        Ui.Add(panel, download);
        Ui.Add(panel, Ui.Label("Explorer: right-click a video → Compress Video.\nWindows 11: first select Show more options.", 9));
        Controls.Add(panel);
        AcceptButton = choose;
        RefreshDependency();
    }

    private void RefreshDependency()
    {
        try { dependency.Text = $"FFmpeg found: {DependencyLocator.Find(settings.FFmpegDirectory).FFmpeg}"; }
        catch (CompressionException) { dependency.Text = "FFmpeg is needed. Download a Windows build, extract it, then choose its folder below."; }
    }
}
