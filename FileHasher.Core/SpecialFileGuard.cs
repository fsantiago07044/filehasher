using System.Runtime.InteropServices;
using System.Text;

namespace FileHasher;

/// <summary>
/// Keeps the walk away from files that are not really files.
///
/// A named pipe with no writer blocks forever the moment anything opens it, so
/// hashing a directory containing one hangs the whole run with no error, no
/// progress and nothing to explain it. That is not exotic: /tmp on both test
/// machines holds a dozen of them, left by .NET's own diagnostics, and
/// pointing the app at /tmp was enough to wedge it indefinitely.
///
/// .NET offers no way to ask. <c>FileAttributes</c> reports a FIFO, a socket
/// and an ordinary file all as <c>Normal</c> on Linux and macOS alike, and
/// <c>UnixFileMode</c> carries only permission bits, so this takes the one
/// syscall route that needs no struct layouts: open without blocking, then ask
/// whether the descriptor can seek. Regular files can; pipes and sockets
/// cannot.
/// </summary>
public static class SpecialFileGuard
{
    /// <summary>
    /// False only when the path is positively known to be something that
    /// cannot be hashed. Anything uncertain returns true, so the normal read
    /// path reports a real error rather than this quietly dropping a file.
    /// </summary>
    /// <param name="length">The size already known from enumeration, which
    /// avoids a syscall per file: every special file reports zero, so only
    /// empty files are ever probed and a tree of real files pays nothing.</param>
    public static bool IsRegularFile(string path, long length)
    {
        if (length != 0) return true;
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return true;

        var fd = Open(path, O_RDONLY | NonBlockFlag);
        if (fd < 0) return true;        // unreadable, which the hash step will report

        try   { return LSeek(fd, 0, SEEK_END) >= 0; }   // ESPIPE on a pipe or socket
        catch { return true; }
        finally { Close(fd); }
    }

    /// <summary>
    /// The files in a directory, minus the ones that are not really files.
    ///
    /// Enumerated as <see cref="FileInfo"/> rather than as paths because the
    /// length then comes from the directory scan that already happened, so the
    /// zero-length gate above costs nothing on a tree of ordinary files.
    /// </summary>
    public static IEnumerable<string> RegularFilesIn(string directory, Action<string>? onSkipped = null)
    {
        foreach (var fi in new DirectoryInfo(directory).EnumerateFiles())
        {
            // A length that cannot be read is not a reason to drop the file;
            // let the read report whatever is actually wrong with it.
            long length = 0;
            var measured = false;
            try { length = fi.Length; measured = true; } catch { }

            if (!measured || IsRegularFile(fi.FullName, length))
            {
                yield return fi.FullName;
            }
            else
            {
                onSkipped?.Invoke(fi.FullName);
            }
        }
    }

    const int O_RDONLY = 0;
    const int SEEK_END = 2;

    // O_NONBLOCK is one of the few open flags whose value differs between the
    // two Unixes, so it cannot be a constant.
    static int NonBlockFlag => OperatingSystem.IsLinux() ? 0x800 : 0x0004;

    // DllImport rather than LibraryImport: the source generator requires
    // AllowUnsafeBlocks across the whole project, which is a large thing to
    // turn on for three calls. The path is marshalled by hand as a
    // null-terminated UTF-8 byte array so no character-set assumption is
    // involved either.
    static int Open(string path, int flags)
    {
        var bytes = new byte[Encoding.UTF8.GetByteCount(path) + 1];
        Encoding.UTF8.GetBytes(path, bytes);
        return OpenNative(bytes, flags);
    }

    [DllImport("libc", EntryPoint = "open")]
    private static extern int OpenNative(byte[] path, int flags);

    [DllImport("libc", EntryPoint = "lseek")]
    private static extern long LSeek(int fd, long offset, int whence);

    [DllImport("libc", EntryPoint = "close")]
    private static extern int Close(int fd);
}
