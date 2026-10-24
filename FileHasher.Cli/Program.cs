using System.Reflection;

namespace FileHasher.Cli;

public static class Program
{
    /// <summary>docs/cli-design.md, "Exit codes".</summary>
    public const int ExitOk = 0, ExitFindings = 1, ExitUsage = 2, ExitFailed = 3;

    public static async Task<int> Main(string[] argv)
    {
        if (argv.Length == 0 || argv[0] is "--help" or "-h" or "help")
        {
            Console.WriteLine(CommandLine.Usage);
            return argv.Length == 0 ? ExitUsage : ExitOk;
        }

        if (argv[0] is "--version")
        {
            Console.WriteLine(Version);
            return ExitOk;
        }

        ParsedArgs args;
        try
        {
            args = CommandLine.Parse(argv);
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine($"filehasher: {ex.Message}");
            Console.Error.WriteLine();
            Console.Error.WriteLine(CommandLine.Usage);
            return ExitUsage;
        }

        // Inner-MSI is the one capability Core cannot offer off Windows. Refuse
        // with a clear message rather than failing inside a P/Invoke.
        if (args.MsiInner && !OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("filehasher: --msi-inner is Windows only; the Windows Installer database API does not exist on this platform.");
            return ExitUsage;
        }

        bool isFile;
        if (File.Exists(args.TargetPath))      isFile = true;
        else if (Directory.Exists(args.TargetPath)) isFile = false;
        else
        {
            Console.Error.WriteLine($"filehasher: path does not exist: {args.TargetPath}");
            return ExitUsage;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            return args.Verb switch
            {
                Verb.Hash   => await HashCommand.RunAsync(args, isFile, cts.Token),
                Verb.Verify => await VerifyCommand.RunAsync(args, isFile, cts.Token),
                _           => ExitUsage,
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("filehasher: aborted.");
            return ExitFailed;
        }
        catch (Exception ex)
        {
            // Exit 3 means the tool did not do its job, which is deliberately
            // distinct from exit 1 (it worked, and found something wrong).
            Console.Error.WriteLine($"filehasher: {ex.Message}");
            return ExitFailed;
        }
    }

    public static string Version =>
        Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                is { } v && v.Length > 0
            ? v.Split('+')[0]
            : "unknown";
}
