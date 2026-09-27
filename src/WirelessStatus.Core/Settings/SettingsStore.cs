using System.Text.Json;

namespace WirelessStatus.Core.Settings;

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON. A missing or unreadable file yields defaults.</summary>
public sealed class SettingsStore(string path, Action<string>? log = null)
{
    public static string DefaultPath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WirelessStatus", "settings.json");

    public string Path { get; } = path;

    public SettingsStore(Action<string>? log = null) : this(DefaultPath, log)
    {
    }

    public AppSettings Load()
    {
        try
        {
            using var stream = File.OpenRead(Path);
            return JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch (FileNotFoundException)
        {
            return new AppSettings();
        }
        catch (DirectoryNotFoundException)
        {
            return new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"Settings: could not read {Path}, using defaults: {ex.Message}");
            return new AppSettings();
        }
    }

    /// <summary>Writes to a temporary file and swaps it in, so a crash mid-write never leaves a truncated file.</summary>
    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temp = Path + ".tmp";
        using (var stream = File.Create(temp))
            JsonSerializer.Serialize(stream, settings, SettingsJsonContext.Default.AppSettings);
        File.Move(temp, Path, overwrite: true);
    }
}
