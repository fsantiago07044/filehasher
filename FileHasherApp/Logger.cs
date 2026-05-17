using System.Text;

namespace FileHasher;

/// <summary>
/// Thread-safe, append-only log writer.
/// Log files are stored in %APPDATA%\FileHasher\Logs\ and named by date.
/// </summary>
internal sealed class Logger : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object       _lock = new();

    public string LogPath { get; }

    public Logger()
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FileHasher", "Logs");

        Directory.CreateDirectory(logDir);

        LogPath  = Path.Combine(logDir, $"FileHasher_{DateTime.Now:yyyy-MM-dd}.log");
        _writer  = new StreamWriter(LogPath, append: true, new UTF8Encoding(false))
        {
            AutoFlush = true
        };

        WriteRaw($"--- Session started {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC ---");
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public void LogResult(HashResult result, string algorithm)
    {
        if (result.Success)
        {
            var sb = new StringBuilder(256);
            sb.Append($"{Ts()} | {algorithm} | OK | {result.Hash} | {result.FilePath}");
            if (result.Length.HasValue)
                sb.Append($" | {result.Length:N0} bytes");
            if (result.LastWriteUtc.HasValue)
                sb.Append($" | modified {result.LastWriteUtc.Value:yyyy-MM-ddTHH:mm:ssZ}");
            if (result.Container is not null)
                sb.Append($" | container: {result.Container}");
            if (result.MsiDirectoryId is not null)
                sb.Append($" | msi-dir: {result.MsiDirectoryId}");
            WriteRaw(sb.ToString());
        }
        else
        {
            var sb = new StringBuilder(256);
            sb.Append($"{Ts()} | {algorithm} | ERROR | {result.ErrorMessage} | {result.FilePath}");
            if (result.Container is not null)
                sb.Append($" | container: {result.Container}");
            if (result.MsiDirectoryId is not null)
                sb.Append($" | msi-dir: {result.MsiDirectoryId}");
            WriteRaw(sb.ToString());
        }
    }

    public void LogWarning(string message)  => WriteRaw($"{Ts()} | WARN | {message}");
    public void LogInfo(string message)     => WriteRaw($"{Ts()} | INFO | {message}");

    public void LogSessionEnd(int processed, int errors)
        => WriteRaw($"--- Session ended {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC | {processed} hashed, {errors} error(s) ---");

    // ── Internals ────────────────────────────────────────────────────────────

    private static string Ts() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

    private void WriteRaw(string line)
    {
        lock (_lock)
            _writer.WriteLine(line);
    }

    public void Dispose() => _writer.Dispose();
}
