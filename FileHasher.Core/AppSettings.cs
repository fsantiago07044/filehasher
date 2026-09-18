using System.Text.Json;

namespace FileHasher;

/// <summary>
/// User preferences that survive between runs.
///
/// Deliberately excludes every option that causes the app to WRITE files:
/// "Write sidecar hash files" and "Export results to CSV" always start off
/// (decided 2026-09-18). Remembering those would mean a later run could create
/// files on disk that the user never re-enabled in that session, which is a
/// different risk class from remembering an algorithm or a scan depth.
///
/// The sidecar extension and format ARE remembered, because neither writes
/// anything on its own: they only describe how a sidecar would be written, and
/// the checkbox that actually gates writing is always off at startup.
///
/// The target path is also excluded. It is a per-run input rather than a
/// preference, and reopening on a stale path invites a run against the wrong
/// folder.
/// </summary>
public sealed record AppSettings
{
    /// <summary>Bumped only when a field's MEANING changes. Adding a field with
    /// a sensible default does not need a bump, since a missing property just
    /// deserialises to that default.</summary>
    public const int CurrentSchemaVersion = 1;

    public int    SchemaVersion    { get; init; } = CurrentSchemaVersion;
    public string Algorithm        { get; init; } = "SHA256";
    public bool   IncludeMetadata  { get; init; }
    public bool   AllFileTypes     { get; init; }

    /// <summary>Subfolders combo index: 0 all, 1 this folder only, 2 limit to
    /// <see cref="DepthLevels"/>.</summary>
    public int    DepthMode        { get; init; }
    public int    DepthLevels      { get; init; } = 1;

    public string SidecarExtension { get; init; } = ".sha256";
    public string SidecarFormat    { get; init; } = "sha256sum";
    public bool   DescendIntoMsi   { get; init; }

    private static readonly string[] Algorithms = { "MD5", "SHA1", "SHA256", "SHA512" };
    private static readonly string[] Formats    = { "sha256sum", "hashonly", "extended" };

    /// <summary>
    /// Coerces anything out of range back to a usable value. The file is plain
    /// JSON in the user's profile, so it can be hand-edited or truncated by a
    /// crash; a bad value must degrade rather than throw or reach the engine.
    /// </summary>
    public AppSettings Sanitised() => this with
    {
        SchemaVersion    = CurrentSchemaVersion,
        Algorithm        = Array.Exists(Algorithms, a => a == Algorithm) ? Algorithm : "SHA256",
        DepthMode        = DepthMode is >= 0 and <= 2 ? DepthMode : 0,
        DepthLevels      = Math.Clamp(DepthLevels, 1, 64),
        SidecarExtension = string.IsNullOrWhiteSpace(SidecarExtension) ? ".sha256" : SidecarExtension,
        SidecarFormat    = Array.Exists(Formats, f => f == SidecarFormat) ? SidecarFormat : "sha256sum"
    };
}

/// <summary>Reads and writes <see cref="AppSettings"/> as JSON under the user's
/// roaming profile. Per-user only, no elevation, no registry.</summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>%APPDATA%\FileHasher\settings.json on Windows. Uses the same
    /// SpecialFolder as <see cref="Logger"/>, which resolves to ~/.config on
    /// Linux and macOS, so this needs no change if the CLI ever runs there.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FileHasher", "settings.json");

    /// <summary>Never throws. A missing, unreadable, corrupt, or
    /// future-versioned file yields defaults, because failing to read a
    /// preference must never stop the app from starting.</summary>
    public static AppSettings Load(string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;
            if (!File.Exists(file)) return new AppSettings();

            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file));
            if (loaded is null) return new AppSettings();

            // A file written by a NEWER app may give existing fields new
            // meaning, so fall back rather than misinterpret it.
            if (loaded.SchemaVersion > AppSettings.CurrentSchemaVersion) return new AppSettings();

            return loaded.Sanitised();
        }
        catch
        {
            return new AppSettings();
        }
    }

    /// <summary>Never throws. Writes to a temporary file in the same directory
    /// and moves it into place, so an interrupted save cannot leave a
    /// half-written settings file behind.</summary>
    public static bool Save(AppSettings settings, string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;
            var dir  = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var tmp = file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings.Sanitised(), Json));
            File.Move(tmp, file, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
