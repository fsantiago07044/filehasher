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
/// The sidecar extension and format go with them (decided 2026-09-18). They
/// qualify a feature that is deliberately reset, so remembering them left
/// greyed controls showing values attached to a switched-off feature, and a
/// remembered "extended" format would change the CONTENT of files written the
/// moment someone ticked the box.
///
/// The test that settles all of this: if a control's ENABLED state depends on
/// the target, its value is not a standing preference and is not persisted.
/// Everything below is a control the UI never disables.
///
/// The target path is also excluded. It is a per-run input rather than a
/// preference, and reopening on a stale path invites a run against the wrong
/// folder. "Scan all file types" and the Subfolders depth follow it out for
/// the same reason (decided 2026-09-18): both describe how to treat the CURRENT
/// target, both grey out without a folder, and remembering them left controls
/// showing values attached to nothing.
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

    public bool   DescendIntoMsi   { get; init; }

    private static readonly string[] Algorithms = { "MD5", "SHA1", "SHA256", "SHA512" };

    /// <summary>
    /// Coerces anything out of range back to a usable value. The file is plain
    /// JSON in the user's profile, so it can be hand-edited or truncated by a
    /// crash; a bad value must degrade rather than throw or reach the engine.
    /// </summary>
    public AppSettings Sanitised() => this with
    {
        SchemaVersion    = CurrentSchemaVersion,
        Algorithm        = Array.Exists(Algorithms, a => a == Algorithm) ? Algorithm : "SHA256"
    };
}

/// <summary>Reads and writes <see cref="AppSettings"/> as JSON under the user's
/// roaming profile. Per-user only, no elevation, no registry.</summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>
    /// %APPDATA%\FileHasher\settings.json on Windows. Uses the same
    /// SpecialFolder as <see cref="Logger"/>, which resolves to ~/.config on
    /// Linux and macOS, so this needs no change if the CLI ever runs there.
    ///
    /// FILEHASHER_SETTINGS overrides it, mirroring the FILEHASHER_EXE variable
    /// the test job already uses. This exists so the UI suite does not read the
    /// developer's own preferences: the app loads settings on every launch, so
    /// without it a machine whose saved algorithm is MD5 would fail the tests
    /// asserting that SHA256 is selected by default, on that machine only.
    /// </summary>
    public static string DefaultPath
    {
        get
        {
            var overridePath = Environment.GetEnvironmentVariable("FILEHASHER_SETTINGS");
            return !string.IsNullOrWhiteSpace(overridePath)
                ? overridePath
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "FileHasher", "settings.json");
        }
    }

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
