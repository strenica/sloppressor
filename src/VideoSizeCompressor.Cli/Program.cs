using VideoSizeCompressor.Commands;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    // Help and registration syntax remain available even with broken preferences.
    UserSettings settings = args.Length > 0 && args[0] is "compress" or "custom" ? UserSettings.Load() : new();
    var command = CommandLine.Parse(args, settings);
    return await CliRunner.RunAsync(command, cancellation.Token);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"{ex.Message}\nUse --help for examples.");
    return 2;
}
