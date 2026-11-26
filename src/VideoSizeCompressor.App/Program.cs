using System.Runtime.InteropServices;
using VideoSizeCompressor.Commands;

namespace VideoSizeCompressor.App;

internal static class Program
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);

    [STAThread]
    private static int Main(string[] args)
    {
        bool console = args.Contains("--no-ui") || args.FirstOrDefault() is "--help" or "-h" or "help" or "--version" or "version";
        try
        {
            if (console)
            {
                _ = AttachConsole(unchecked((uint)-1));
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
                Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            }
            // Unregistration and help work even if the preference file is damaged.
            UserSettings settings = args.Length == 0 || args[0] is "compress" or "custom" or "gif" ? UserSettings.Load() : new();
            ParsedCommand command = CommandLine.Parse(args, settings);
            if (command.NoUi || command.Command is "help" or "version")
            {
                using var cancellation = new CancellationTokenSource();
                Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
                return CliRunner.RunAsync(command, cancellation.Token).GetAwaiter().GetResult();
            }
            if (command.Command is "register" or "unregister")
            {
                if (command.Command == "register") ShellIntegration.Register(Environment.ProcessPath!);
                else ShellIntegration.Unregister();
                if (!command.Silent) MessageBox.Show(command.Command == "register"
                    ? "Explorer menu installed. On Windows 11, look under Show more options."
                    : "Explorer menu removed.", "VideoSizeCompressor", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "VideoSizeCompressor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            if (command.Command == "home") Application.Run(new StartForm(settings));
            else if (command.Command == "gif" && command.Gif is { } gif)
            {
                using var dialog = new GifOptionsDialog(gif);
                if (dialog.ShowDialog() != DialogResult.OK) return 130;
                using var form = new GifProgressForm(dialog.Options);
                Application.Run(form);
                return form.ExitCode;
            }
            else if (command.Options is { } options)
            {
                if (command.Command == "custom")
                {
                    using var dialog = new CustomSizeDialog(options.MaximumMegabytes);
                    if (dialog.ShowDialog() != DialogResult.OK) return 130;
                    options = options with { MaximumMegabytes = dialog.MaximumMegabytes };
                }
                using var form = new ProgressForm(options);
                Application.Run(form);
                return form.ExitCode;
            }
            return 0;
        }
        catch (Exception ex)
        {
            if (console) Console.Error.WriteLine(ex.Message);
            else if (!args.Contains("--silent")) MessageBox.Show(ex.Message, "VideoSizeCompressor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }
    }
}
