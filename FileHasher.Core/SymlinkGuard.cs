namespace FileHasher;

/// <summary>
/// Stops a directory symlink that points back up its own tree from sending the
/// walk round forever.
///
/// Deliberately ancestor-only. A full visited-set keyed on device and inode
/// would also deduplicate two different symlinks that happen to resolve to the
/// same directory, and that changes results for trees containing no loop at
/// all. This only refuses to descend where the alternative is never
/// terminating, so any tree that finishes today produces identical output.
///
/// Mutual loops between sibling trees are out of scope by the same reasoning:
/// catching them needs the visited-set that was ruled out.
/// </summary>
public static class SymlinkGuard
{
    /// <summary>
    /// True when <paramref name="subdirectory"/> is a symlink whose final
    /// target is <paramref name="currentDirectory"/> or one of its ancestors,
    /// which is exactly the shape that loops.
    /// </summary>
    public static bool WouldLoop(string subdirectory, string currentDirectory)
    {
        try
        {
            // Null means it is an ordinary directory, which cannot loop.
            var target = Directory.ResolveLinkTarget(subdirectory, returnFinalTarget: true);
            if (target is null) return false;

            var here = Normalise(Path.GetFullPath(currentDirectory));
            var to   = Normalise(target.FullName);

            // Equal: /data/link -> /data while enumerating /data.
            // Prefix: /data/a/b/link -> /data while enumerating /data/a/b.
            return string.Equals(here, to, Cmp)
                || here.StartsWith(to + Path.DirectorySeparatorChar, Cmp);
        }
        catch
        {
            // A broken link, a permission error, or a path we cannot resolve is
            // not a loop. Let the caller's own error handling report it rather
            // than silently pruning a directory for the wrong reason.
            return false;
        }
    }

    static StringComparison Cmp =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    static string Normalise(string p) =>
        p.Length > 1 ? p.TrimEnd(Path.DirectorySeparatorChar) : p;
}
