using System.Reflection;

namespace PowerPlugin.Core.Configuration;

/// <summary>
/// Resolves where settings and history are stored.
/// <para>
/// By default everything lives in <c>%LOCALAPPDATA%\PowerPlugin</c>. If a file named
/// <c>portable.txt</c> sits next to the executable, the data folder moves next to the
/// executable instead, which makes the program usable from a USB stick.
/// </para>
/// </summary>
public static class AppPaths
{
    public const string PortableMarkerFileName = "portable.txt";

    private static readonly Lazy<string> DataDirectoryLazy = new(ResolveDataDirectory, isThreadSafe: true);

    public static string DataDirectory => DataDirectoryLazy.Value;

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string DatabaseFile => Path.Combine(DataDirectory, "history.db");

    public static string LogFile => Path.Combine(DataDirectory, "powerplugin.log");

    public static bool IsPortable =>
        File.Exists(Path.Combine(ApplicationDirectory, PortableMarkerFileName));

    public static string ApplicationDirectory =>
        Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location ?? AppContext.BaseDirectory)
        ?? AppContext.BaseDirectory;

    private static string ResolveDataDirectory()
    {
        string directory = IsPortable
            ? Path.Combine(ApplicationDirectory, "Data")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PowerPlugin");

        Directory.CreateDirectory(directory);
        return directory;
    }
}
