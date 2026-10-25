namespace FileHasher;

/// <summary>
/// Turns a user-typed list of file types into the set the walks compare
/// against.
///
/// Lives in Core rather than in a front end because the Windows app, the CLI
/// and the Linux app must agree about what "pkg, .dmg , TAR.GZ" means; a
/// filter parsed two slightly different ways is exactly the drift that
/// FileHasher.Core exists to prevent.
/// </summary>
public static class FileTypeFilter
{
    /// <summary>
    /// Accepts commas, semicolons, spaces and newlines as separators, with or
    /// without a leading dot, in any case. Returns null when nothing usable
    /// was typed, which the walks read as "no filter".
    /// </summary>
    public static IReadOnlySet<string>? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split([',', ';', ' ', '\t', '\r', '\n'],
                                       StringSplitOptions.RemoveEmptyEntries |
                                       StringSplitOptions.TrimEntries))
        {
            var t = raw.TrimStart('*');          // "*.deb" is a natural thing to type
            if (t.Length == 0) continue;
            set.Add(t.StartsWith('.') ? t : "." + t);
        }
        return set.Count == 0 ? null : set;
    }

    /// <summary>
    /// Whether a path matches. A multi-part suffix such as ".tar.gz" is
    /// checked against the end of the name, because Path.GetExtension only
    /// ever returns ".gz" and a Linux user typing "tar.gz" means the pair.
    /// </summary>
    public static bool Matches(string path, IReadOnlySet<string> types)
    {
        var name = Path.GetFileName(path);
        foreach (var t in types)
            if (name.EndsWith(t, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
