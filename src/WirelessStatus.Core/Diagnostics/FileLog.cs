namespace WirelessStatus.Core.Diagnostics;

/// <summary>
/// Minimal thread-safe append-only log. When the file exceeds <see cref="MaxBytes"/> it is moved to
/// <c>*.old.txt</c> (replacing the previous one), so disk use stays bounded. Logging never throws.
/// </summary>
public sealed class FileLog(string path)
{
    public const long MaxBytes = 1024 * 1024;

    public static string DefaultPath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WirelessStatus", "log.txt");

    private readonly Lock _lock = new();

    public string Path { get; } = path;

    public void Write(string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                if (File.Exists(Path) && new FileInfo(Path).Length > MaxBytes)
                    File.Move(Path, System.IO.Path.ChangeExtension(Path, ".old.txt"), overwrite: true);
                File.AppendAllText(Path, line);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging is best effort.
            }
        }
    }
}
