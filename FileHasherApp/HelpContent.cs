using System.Reflection;

namespace FileHasher;

/// <summary>One section within a help topic: an optional heading, paragraphs,
/// and an optional bullet list. Paragraph and bullet strings may use inline
/// markup which HelpForm renders: **bold** and `code`.</summary>
internal sealed class HelpSection
{
    public string?  Heading    { get; }
    public string[] Paragraphs { get; }
    public string[] Bullets    { get; }

    public HelpSection(string? heading = null,
                       string[]? paragraphs = null,
                       string[]? bullets = null)
    {
        Heading    = heading;
        Paragraphs = paragraphs ?? Array.Empty<string>();
        Bullets    = bullets ?? Array.Empty<string>();
    }
}

/// <summary>One topic in the help window's sidebar.</summary>
internal sealed record HelpTopic(string Title, HelpSection[] Sections);

/// <summary>
/// The in-app help content, plus the support links it references. Content
/// lives in code so it is versioned with the app; the support email subject
/// embeds the running version automatically.
/// </summary>
internal static class HelpContent
{
    public const string SupportUrl = "https://fabianasantiago.com/filehasher/support/";
    public const string PrivacyUrl = "https://fabianasantiago.com/privacy-policy/";
    public const string SupportEmail = "support@fabianasantiago.com";

    /// <summary>The marketing version of the running app. Prefers the
    /// informational version so prerelease suffixes ("0.3.1-beta") are
    /// visible, with any "+metadata" stripped; same rule as the About dialog.</summary>
    public static string AppVersion { get; } = ComputeAppVersion();

    /// <summary>Support mail link with a version-stamped subject, so every
    /// release updates the subject automatically.</summary>
    public static string SupportMailto =>
        $"mailto:{SupportEmail}?subject=FileHasher-Windows-{AppVersion}";

    private static string ComputeAppVersion()
    {
        var asm  = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                      ?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
        {
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }
        var ver = asm.GetName().Version;
        return ver is null ? "0.1" : $"{ver.Major}.{ver.Minor}.{ver.Build}";
    }

    public static readonly HelpTopic[] Topics =
    {
        new("Getting Started", new[]
        {
            new HelpSection(paragraphs: new[]
            {
                "FileHasher computes cryptographic hashes of files and folders you choose, writes standard sidecar checksum files next to them, and verifies those sidecars later. Use it to prove that a download, installer, backup, or archive has not changed since you hashed it, or that it matches a checksum published by a vendor.",
            }),
            new HelpSection("Quick start", bullets: new[]
            {
                "Click **Browse Folder…** (or **Browse File…**) and choose what to hash. You can also drag a file or folder onto the path box.",
                "Pick an algorithm. **SHA256** is the default and the right choice for almost everything.",
                "Click **Run**. Each file appears in the results list with its hash.",
                "To create verifiable records, check **Write sidecar hash files** before running, then use **Verify Sidecars** any time later.",
            }),
        }),

        new("Choosing a Target", new[]
        {
            new HelpSection(paragraphs: new[]
            {
                "The target can be a single file or a folder. Use the browse buttons, drag and drop onto the path box, or type or paste a path directly.",
                "Folders are scanned **recursively**, including every subdirectory. Reading files in protected locations (for example `C:\\Windows\\System32`) may require running as Administrator; see the Administrator Mode topic.",
            }),
        }),

        new("Folder Scanning and File Types", new[]
        {
            new HelpSection(paragraphs: new[]
            {
                "When the target is a folder, the **Scan all file types** checkbox controls which files are included:",
            }),
            new HelpSection(bullets: new[]
            {
                "**Unchecked** (default): only `.exe` and `.msi` files are hashed, matching FileHasher's installer-verification roots.",
                "**Checked**: every file in the folder tree is hashed.",
            }),
            new HelpSection(paragraphs: new[]
            {
                "The same setting drives the sidecar verification audit (which files are reported as **NO SIDECAR**) and, when the experimental MSI scan is on, which files inside an MSI are hashed.",
            }),
        }),

        new("Hash Algorithms", new[]
        {
            new HelpSection(bullets: new[]
            {
                "**MD5**: fast, but not collision-resistant. Use only to match older published checksums.",
                "**SHA1**: legacy; deprecated for most security purposes.",
                "**SHA256**: the default. Recommended for general use.",
                "**SHA512**: the strongest option; produces a longer digest.",
            }),
            new HelpSection(paragraphs: new[]
            {
                "The hash column header, the CSV export header, and the suggested sidecar extension all follow the selected algorithm.",
            }),
        }),

        new("Sidecar Hash Files", new[]
        {
            new HelpSection(paragraphs: new[]
            {
                "A sidecar is a small text file written next to each hashed file; hashing `setup.exe` with SHA256 produces `setup.exe.sha256` alongside it. Sidecars are how FileHasher remembers hashes for later verification.",
            }),
            new HelpSection("Extension", paragraphs: new[]
            {
                "The suffix appended to the original filename. The suggestion follows the selected algorithm (`.md5`, `.sha1`, `.sha256`, `.sha512`); a custom extension you type is never overwritten.",
            }),
            new HelpSection("Format", bullets: new[]
            {
                "**{algo}sum format** (default): `HASH *filename`, compatible with the standard md5sum, sha1sum, sha256sum, and sha512sum command-line tools.",
                "**Hash only**: the raw hash string with no filename.",
                "**Extended**: `HASH *filename *lastModified *sizeBytes`, with the timestamp in ISO-8601 UTC.",
            }),
            new HelpSection("When a sidecar already exists", paragraphs: new[]
            {
                "FileHasher pauses before hashing begins and asks, file by file: **Overwrite**, **Overwrite All**, **Skip**, or **Skip All**. Skipped files are excluded from the run. All decisions are collected before any file is touched.",
                "Sidecar files themselves are never treated as hash targets while sidecar writing is on, so repeated runs never produce chains like `.sha256.sha256`. Writing sidecars into protected locations (for example `C:\\Program Files`) requires Administrator rights.",
            }),
        }),

        new("Verifying Sidecars", new[]
        {
            new HelpSection(paragraphs: new[]
            {
                "**Verify Sidecars** re-hashes files and compares the result against their sidecars, using the current target path and the Extension configured under the sidecar options (the write checkbox does not need to be on). Folder targets are scanned recursively; targeting a single file verifies that file's sidecar, and targeting a sidecar directly verifies it against its base file.",
                "The algorithm is detected automatically per sidecar from the length of the stored hash, so a folder with mixed-algorithm sidecars verifies in one pass. All three sidecar formats are recognized.",
            }),
            new HelpSection("Verdicts", bullets: new[]
            {
                "**OK** (green): the re-computed hash matches the sidecar.",
                "**MISMATCH** (red): the hash differs; the row shows the expected and computed values.",
                "**MISSING FILE** (red): a sidecar exists but the file it attests to is gone.",
                "**NO SIDECAR** (orange): the file matches the current scan filter but has no sidecar; a completeness audit.",
                "**PARSE ERROR** (red): the sidecar's content is not a recognized format.",
                "**READ ERROR** (red): the file or its sidecar could not be read.",
            }),
            new HelpSection(paragraphs: new[]
            {
                "The hash alone decides pass or fail. For extended-format sidecars, a differing embedded filename, date, or size on an otherwise matching row is shown as an informational note; a file's modified date often changes legitimately on copy or restore.",
            }),
        }),

        new("MSI Inner Scan (experimental)", new[]
        {
            new HelpSection(paragraphs: new[]
            {
                "With **Hash files inside MSI installers** checked, every `.msi` file the run encounters is also opened read-only via the Windows Installer database API, and the files contained inside it are hashed individually in addition to the MSI itself. Inner files appear as their own rows prefixed with the parent MSI's name, with the MSI's internal install path shown and the raw Directory-table identifier in the MSI Dir column.",
                "The **Scan all file types** checkbox filters inner files the same way it filters folder contents. The MSI is treated as data: contents are extracted to a sandboxed temporary directory that is deleted as soon as hashing finishes, and the installer is never executed.",
            }),
        }),

        new("CSV Export", new[]
        {
            new HelpSection(paragraphs: new[]
            {
                "Check **Export results to CSV** and choose an output path before running. The CSV is written after hashing completes: UTF-8 with BOM (opens correctly in Excel), sorted by file path, successful rows only.",
                "Columns are `Path` and the algorithm; with **Include file metadata** on, `LengthBytes` and `LastWriteUtc` are added, and with the MSI scan on, `Container` and `MsiDirectoryId`. CSV export applies to hashing runs, not verification runs.",
            }),
        }),

        new("Results List", new[]
        {
            new HelpSection(paragraphs: new[]
            {
                "Each hashed or verified file is one row. Errors appear in red, warnings in orange, inner-MSI files in steel blue. With **Include file metadata** on, size and modified (UTC) columns are filled in.",
            }),
            new HelpSection("Row actions (right-click)", bullets: new[]
            {
                "**Open in File Explorer**: opens the containing folder with the file selected (also triggered by double-click or Enter).",
                "**Open PowerShell here** and **Open Command Prompt here**: open a shell in the row's folder.",
                "**Copy Hash** and **Copy File Path**: copy the row's values to the clipboard.",
            }),
            new HelpSection(paragraphs: new[]
            {
                "**Clear Results** empties the list and resets the progress bar; **Stop** cancels a run cleanly, keeping the rows already produced.",
            }),
        }),

        new("Logs", new[]
        {
            new HelpSection(paragraphs: new[]
            {
                "Every run is logged automatically, one file per day, under `%APPDATA%\\FileHasher\\Logs`. Each entry records the timestamp (UTC), algorithm, result, hash, and file path, plus size and modified date when metadata is on. A session header and footer mark the start and end of each run.",
                "Click the log path in the status bar at the bottom of the window to open the log folder in Explorer.",
            }),
        }),

        new("Administrator Mode", new[]
        {
            new HelpSection(paragraphs: new[]
            {
                "FileHasher starts as a standard user. Elevation is needed to read files in protected locations (for example `C:\\Windows\\System32`) or to write sidecars next to installers in protected locations (for example `C:\\Program Files`).",
                "Click **Run as Administrator…** to relaunch elevated; the title bar and status bar confirm elevated status. If a run hits an access-denied error midway, a dialog offers the relaunch automatically.",
            }),
        }),

        new("Support", new[]
        {
            new HelpSection(paragraphs: new[]
            {
                $"Questions, bug reports, or feature ideas are welcome at **{SupportEmail}**. The **Email Support** link below opens a message with the subject pre-filled with your app version (FileHasher-Windows-{AppVersion}); adding your Windows version and what you expected to happen makes fixes faster.",
                "The support website and privacy policy are also linked below and in the Help menu.",
            }),
        }),
    };
}
