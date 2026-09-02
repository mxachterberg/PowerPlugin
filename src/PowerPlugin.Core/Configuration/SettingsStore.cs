using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerPlugin.Core.Configuration;

/// <summary>
/// Loads and saves <see cref="AppSettings"/>. A damaged settings file never stops the program:
/// it falls back to the defaults and keeps the broken file as <c>settings.json.bak</c>.
/// </summary>
public sealed class SettingsStore(string? filePath = null)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath = filePath ?? AppPaths.SettingsFile;
    private readonly object _gate = new();

    public string FilePath => _filePath;

    public AppSettings Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return new AppSettings();
                }

                string json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                TryBackupBrokenFile();
                return new AppSettings();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_gate)
        {
            string? directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write to a temporary file first so a crash mid-write cannot truncate the settings.
            string temporary = _filePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, SerializerOptions));
            File.Move(temporary, _filePath, overwrite: true);
        }
    }

    private void TryBackupBrokenFile()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                File.Move(_filePath, _filePath + ".bak", overwrite: true);
            }
        }
        catch (IOException)
        {
            // Nothing else to do - the defaults are used either way.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
