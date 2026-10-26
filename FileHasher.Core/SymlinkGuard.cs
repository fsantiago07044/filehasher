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
///
/// The comparison is between canonical paths, meaning paths with no symlink
/// left anywhere in them. That matters more than it sounds. The first version
/// compared the link's resolved target against <c>Path.GetFullPath</c> of the
/// directory being walked, and those two resolve different amounts: GetFullPath
/// makes a path absolute but leaves symlinked ancestors alone, while resolving
/// a link resolves the whole thing. Scanning a folder reached through any
/// symlinked ancestor therefore compared "/tmp/x/data" against
/// "/private/tmp/x/data", matched nothing, and descended the loop until the
/// operating system refused at around sixteen levels. That is the default on
/// macOS and a one-symlink accident on Linux.
/// </summary>
public static class SymlinkGuard
{
    /// <summary>
    /// The path with every symlink in it resolved. .NET has no realpath and
    /// <c>ResolveLinkTarget</c> only resolves the last component, so this
    /// walks down from the root resolving each segment in turn. Call it once
    /// for the root of a walk and use <see cref="Descend"/> after that; doing
    /// it per directory would make a deep tree quadratic in syscalls.
    /// </summary>
    public static string Canonical(string directory)
    {
        var full = Path.GetFullPath(directory);
        try
        {
            var root    = Path.GetPathRoot(full) ?? "";
            var acc     = root;
            var pending = new Queue<string>(Segments(full, root));

            // A link's target can itself sit behind links, so landing on one
            // means starting again from the target's own root with the
            // segments still to come appended. The cap is for a chain of links
            // that never settles; the operating system refuses first in
            // practice, and this is the belt to that braces.
            for (var hops = 0; pending.Count > 0; )
            {
                acc = Path.Combine(acc, pending.Dequeue());

                if (Directory.ResolveLinkTarget(acc, returnFinalTarget: true) is not { } link) continue;
                if (++hops > 64) break;

                var target     = Path.GetFullPath(link.FullName);
                var targetRoot = Path.GetPathRoot(target) ?? "";
                pending = new Queue<string>(Segments(target, targetRoot).Concat(pending));
                acc     = targetRoot;
            }
            return Normalise(acc);
        }
        catch
        {
            // Unresolvable is not a loop; the caller's own error handling
            // reports the directory rather than this pruning it for the wrong
            // reason.
            return Normalise(full);
        }
    }

    static IEnumerable<string> Segments(string path, string root) =>
        path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// True when <paramref name="subdirectory"/> is a symlink whose final
    /// target is the directory being walked or one of its ancestors, which is
    /// exactly the shape that loops. <paramref name="canonicalCurrent"/> is
    /// the canonical path of that directory, and on a false return
    /// <paramref name="canonicalSub"/> is the canonical path of the
    /// subdirectory, ready to be carried down as the next level's current.
    /// </summary>
    public static bool WouldLoop(string subdirectory, string canonicalCurrent, out string canonicalSub)
    {
        canonicalSub = Path.Combine(canonicalCurrent, Path.GetFileName(subdirectory));
        try
        {
            // Null means an ordinary directory, which cannot loop. Its
            // canonical path is its parent's plus its own name, since the
            // parent is already canonical.
            if (Directory.ResolveLinkTarget(subdirectory, returnFinalTarget: true) is not { } target)
                return false;

            // Canonical on the target too, not just Normalise: resolving a
            // link follows the chain at the last component but leaves the
            // ancestors of what it lands on alone, so the two sides can still
            // disagree about /var versus /private/var. A symlinked directory
            // is rare enough that the extra path walk costs nothing.
            var here = Normalise(canonicalCurrent);
            var to   = Canonical(target.FullName);

            // Equal: /data/link -> /data while enumerating /data.
            // Prefix: /data/a/b/link -> /data while enumerating /data/a/b.
            if (string.Equals(here, to, Cmp) || here.StartsWith(to + Path.DirectorySeparatorChar, Cmp))
                return true;

            canonicalSub = to;
            return false;
        }
        catch
        {
            // A broken link, a permission error, or a path we cannot resolve
            // is not a loop. Let the caller's own error handling report it.
            return false;
        }
    }

    static StringComparison Cmp =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    static string Normalise(string p) =>
        p.Length > 1 ? p.TrimEnd(Path.DirectorySeparatorChar) : p;
}
