namespace FileHasher.Cli;

/// <summary>Which verb was requested.</summary>
public enum Verb { Hash, Verify }

/// <summary>
/// Conditions that force exit 1. Defaults reproduce the exit-code table
/// exactly, so passing nothing behaves as the table says
/// (docs/cli-design.md, "--fail-on").
/// </summary>
[Flags]
public enum FailOn
{
    None       = 0,
    Unreadable = 1 << 0,
    Mismatch   = 1 << 1,
    Missing    = 1 << 2,
    NoSidecar  = 1 << 3,
    Empty      = 1 << 4,

    HashAny   = Unreadable | Empty,
    VerifyAny = Unreadable | Mismatch | Missing | NoSidecar | Empty,
}

/// <summary>Everything the parser produces. Immutable; the commands only read it.</summary>
public sealed record ParsedArgs
{
    public required Verb    Verb        { get; init; }
    public required string  TargetPath  { get; init; }
    public string  Algorithm      { get; init; } = "SHA256";
    public bool    AllTypes       { get; init; }
    public bool    Sidecar        { get; init; }
    public string? SidecarExt     { get; init; }
    public string  SidecarFormat  { get; init; } = "sha256sum";
    public string? CsvPath        { get; init; }
    public bool    Metadata       { get; init; }
    public bool    MsiInner       { get; init; }
    public int?    MaxDepth       { get; init; }
    public bool    Json           { get; init; }
    public bool    Quiet          { get; init; }
    public FailOn  FailOn         { get; init; }
}

/// <summary>Raised for anything that should exit 2 rather than run.</summary>
public sealed class UsageException(string message) : Exception(message);

/// <summary>
/// Hand-rolled because the surface is small and System.CommandLine would be a
/// dependency in a tool whose whole selling point is a self-contained binary.
/// </summary>
public static class CommandLine
{
    public const string Usage = """
        filehasher hash   <path> [options]
        filehasher verify <path> [options]

        hash
          -a, --algorithm <md5|sha1|sha256|sha512>   default: sha256
              --all-types                            include every file type
                                                     (default: .exe and .msi only)
              --sidecar                              write sidecar hash files
              --sidecar-ext <.ext>                   default follows the algorithm
              --sidecar-format <sum|hash|extended>   default: sum
              --csv <path>                           write results as CSV
              --metadata                             include size and modified time
              --msi-inner                            also hash files inside .msi
                                                     (Windows only)

        verify
              --sidecar-ext <.ext>                   default: .sha256
              --all-types                            audit every file for a missing
                                                     sidecar, not just .exe/.msi
              --csv <path>

        shared
              --no-recurse, --no-recursive           do not descend into subdirectories
                                                     (default: recurse without limit)
              --max-depth <n>                        bound the walk; 0 is the starting
                                                     directory only
              --fail-on <list>                       conditions that force exit 1:
                                                     unreadable, mismatch, missing,
                                                     no-sidecar, empty, any, none
              --json                                 one JSON object per line
          -q, --quiet                                errors and the summary only
              --version, --help

        Exit codes: 0 nothing wrong, 1 findings, 2 usage error, 3 run failed.
        """;

    /// <summary>Default extension for an algorithm, used when --sidecar-ext is absent.</summary>
    public static string DefaultSidecarExt(string algorithm) => algorithm switch
    {
        "MD5"    => ".md5",
        "SHA1"   => ".sha1",
        "SHA512" => ".sha512",
        _        => ".sha256",
    };

    public static ParsedArgs Parse(string[] argv)
    {
        if (argv.Length == 0) throw new UsageException("No command given.");

        var verb = argv[0] switch
        {
            "hash"   => Verb.Hash,
            "verify" => Verb.Verify,
            _        => throw new UsageException($"Unknown command '{argv[0]}'. Expected 'hash' or 'verify'."),
        };

        string?  target = null, algorithm = null, sidecarExt = null, sidecarFormat = null, csv = null, failOn = null;
        bool     allTypes = false, sidecar = false, metadata = false, msiInner = false;
        bool     json = false, quiet = false, noRecurse = false;
        int?     maxDepth = null;

        for (var i = 1; i < argv.Length; i++)
        {
            var a = argv[i];
            switch (a)
            {
                // Accepted as an explicit no-op: recursion is already the default,
                // and a POSIX user will reach for it. Deliberately absent from Usage.
                case "-r" or "--recurse" or "--recursive":
                    break;

                case "--no-recurse" or "--no-recursive": noRecurse = true; break;
                case "--all-types":  allTypes = true; break;
                case "--sidecar":    sidecar  = true; break;
                case "--metadata":   metadata = true; break;
                case "--msi-inner":  msiInner = true; break;
                case "--json":       json     = true; break;
                case "-q" or "--quiet": quiet  = true; break;

                case "-a" or "--algorithm":   algorithm     = Next(argv, ref i, a); break;
                case "--sidecar-ext":         sidecarExt    = Next(argv, ref i, a); break;
                case "--sidecar-format":      sidecarFormat = Next(argv, ref i, a); break;
                case "--csv":                 csv           = Next(argv, ref i, a); break;
                case "--fail-on":             failOn        = Next(argv, ref i, a); break;

                case "--max-depth":
                    var raw = Next(argv, ref i, a);
                    if (!int.TryParse(raw, out var d) || d < 0)
                        throw new UsageException($"--max-depth needs a whole number of 0 or more, got '{raw}'.");
                    maxDepth = d;
                    break;

                // Not yet implemented, and silently ignoring it would be worse
                // than refusing: the caller asked for cycle-safe traversal and
                // would not get it. Tracked in docs/cli-design.md, "Symlinks".
                case "--follow-symlinks":
                    throw new UsageException(
                        "--follow-symlinks is not implemented yet. Directory symlinks are " +
                        "currently followed, as the desktop apps do; cycle-safe opt-in " +
                        "traversal is still to come.");

                default:
                    if (a.StartsWith('-')) throw new UsageException($"Unknown option '{a}'.");
                    if (target is not null) throw new UsageException($"More than one path given: '{target}' and '{a}'.");
                    target = a;
                    break;
            }
        }

        if (target is null) throw new UsageException($"No path given for '{argv[0]}'.");

        // --no-recurse and --max-depth 0 are the same instruction at different
        // resolutions, so they agree rather than conflict.
        if (noRecurse)
        {
            if (maxDepth is > 0)
                throw new UsageException($"--no-recurse and --max-depth {maxDepth} contradict each other.");
            maxDepth = 0;
        }

        var algo = NormalizeAlgorithm(algorithm);

        return new ParsedArgs
        {
            Verb          = verb,
            TargetPath    = target,
            Algorithm     = algo,
            AllTypes      = allTypes,
            Sidecar       = sidecar,
            SidecarExt    = sidecarExt is null ? null : NormalizeExt(sidecarExt),
            SidecarFormat = NormalizeFormat(sidecarFormat),
            CsvPath       = csv,
            Metadata      = metadata,
            MsiInner      = msiInner,
            MaxDepth      = maxDepth,
            Json          = json,
            Quiet         = quiet,
            FailOn        = ParseFailOn(failOn, verb),
        };
    }

    static string Next(string[] argv, ref int i, string option)
    {
        if (i + 1 >= argv.Length) throw new UsageException($"{option} needs a value.");
        return argv[++i];
    }

    static string NormalizeAlgorithm(string? a) => (a ?? "sha256").ToLowerInvariant() switch
    {
        "md5"    => "MD5",
        "sha1"   => "SHA1",
        "sha256" => "SHA256",
        "sha512" => "SHA512",
        _        => throw new UsageException($"Unknown algorithm '{a}'. Expected md5, sha1, sha256 or sha512."),
    };

    static string NormalizeExt(string e) => e.StartsWith('.') ? e : "." + e;

    static string NormalizeFormat(string? f) => (f ?? "sum").ToLowerInvariant() switch
    {
        "sum"      => "sha256sum",
        "hash"     => "hashonly",
        "extended" => "extended",
        _          => throw new UsageException($"Unknown sidecar format '{f}'. Expected sum, hash or extended."),
    };

    /// <summary>
    /// Absent means the documented default for the verb, which reproduces the
    /// exit-code table. Tokens not applicable to the verb are a usage error
    /// rather than silently ignored, so a typo in CI surfaces immediately.
    /// </summary>
    public static FailOn ParseFailOn(string? list, Verb verb)
    {
        if (list is null)
            return verb == Verb.Hash ? FailOn.Unreadable : FailOn.Mismatch | FailOn.Missing | FailOn.Unreadable;

        var result = FailOn.None;
        foreach (var token in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var t = token.ToLowerInvariant();
            if (t == "none")
            {
                if (list.Split(',', StringSplitOptions.RemoveEmptyEntries).Length > 1)
                    throw new UsageException("--fail-on none cannot be combined with other conditions.");
                return FailOn.None;
            }

            result |= t switch
            {
                "unreadable" => FailOn.Unreadable,
                "empty"      => FailOn.Empty,
                "any"        => verb == Verb.Hash ? FailOn.HashAny : FailOn.VerifyAny,
                "mismatch"   => Only(verb, t, FailOn.Mismatch),
                "missing"    => Only(verb, t, FailOn.Missing),
                "no-sidecar" => Only(verb, t, FailOn.NoSidecar),
                _            => throw new UsageException(
                                    $"Unknown --fail-on condition '{token}'. Expected unreadable, " +
                                    "mismatch, missing, no-sidecar, empty, any or none."),
            };
        }
        return result;
    }

    static FailOn Only(Verb verb, string token, FailOn value) =>
        verb == Verb.Verify
            ? value
            : throw new UsageException($"--fail-on {token} applies to 'verify', not 'hash'.");
}
