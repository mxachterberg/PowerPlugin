using System.Collections.Concurrent;
using PowerPlugin.Core.Configuration;

namespace PowerPlugin.Core.Monitoring;

/// <summary>
/// Minimal rolling log. Sensor access fails in many small ways on real machines
/// (missing driver, no rights, exotic hardware) and this makes those failures visible
/// without pulling in a logging framework.
/// </summary>
public static class DiagnosticsLog
{
    private const int MaxInMemoryEntries = 200;
    private const long MaxFileSizeBytes = 512 * 1024;

    private static readonly ConcurrentQueue<string> Entries = new();
    private static readonly object FileGate = new();

    public static void Write(string message)
    {
        string line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}";

        Entries.Enqueue(line);
        while (Entries.Count > MaxInMemoryEntries && Entries.TryDequeue(out _))
        {
        }

        AppendToFile(line);
    }

    public static void Write(string context, Exception exception) =>
        Write($"{context}: {exception.GetType().Name} - {exception.Message}");

    public static IReadOnlyList<string> Recent => Entries.ToArray();

    private static void AppendToFile(string line)
    {
        lock (FileGate)
        {
            try
            {
                string path = AppPaths.LogFile;

                if (File.Exists(path) && new FileInfo(path).Length > MaxFileSizeBytes)
                {
                    File.Move(path, path + ".old", overwrite: true);
                }

                File.AppendAllLines(path, [line]);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Logging must never break the application.
            }
        }
    }
}
