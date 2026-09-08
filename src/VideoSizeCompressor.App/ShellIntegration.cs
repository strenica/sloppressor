using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace VideoSizeCompressor.App;

internal static class ShellIntegration
{
    public static readonly string[] Extensions = [".mp4", ".mkv", ".mov", ".avi", ".webm", ".m4v", ".mpg", ".mpeg", ".ts"];
    private static string KeyFor(string extension) => $@"Software\Classes\SystemFileAssociations\{extension}\shell\VideoSizeCompressor";

    public static void Register(string executable)
    {
        executable = Path.GetFullPath(executable);
        if (!File.Exists(executable) || executable.Contains('"')) throw new IOException("The application executable path is invalid.");
        using RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        foreach (string extension in Extensions)
        {
            using RegistryKey menu = root.CreateSubKey(KeyFor(extension));
            menu.SetValue("MUIVerb", "Compress Video");
            menu.SetValue("Icon", $"\"{executable}\",0");
            menu.SetValue("SubCommands", "");
            menu.SetValue("MultiSelectModel", "Single");
            AddVerb(menu, "01_20MB", "Compress to 20 MB", $"\"{executable}\" compress \"%1\" --size 20");
            AddVerb(menu, "02_8MB", "Compress to 8 MB", $"\"{executable}\" compress \"%1\" --size 8");
            AddVerb(menu, "03_Custom", "Custom Size...", $"\"{executable}\" custom \"%1\"");
        }
        NotifyExplorer();
    }

    private static void AddVerb(RegistryKey menu, string key, string label, string command)
    {
        using RegistryKey verb = menu.CreateSubKey(@"shell\" + key);
        verb.SetValue("MUIVerb", label);
        verb.SetValue("MultiSelectModel", "Single");
        using RegistryKey action = verb.CreateSubKey("command");
        action.SetValue("", command, RegistryValueKind.String);
    }

    public static void Unregister()
    {
        using RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        foreach (string extension in Extensions) root.DeleteSubKeyTree(KeyFor(extension), throwOnMissingSubKey: false);
        NotifyExplorer();
    }

    private static void NotifyExplorer() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
