using System.Text.Json;

namespace FileHasher.Cli;

public static class Output
{
    /// <summary>
    /// The summary is the one thing --quiet keeps, because a pipeline that
    /// suppresses per-file noise still needs to see what happened.
    /// </summary>
    public static void Summary(ParsedArgs a, IReadOnlyDictionary<string, int> counts)
    {
        if (a.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new SummaryEvent("summary", counts), CliJsonContext.Default.SummaryEvent));
            return;
        }

        var parts = counts.Where(kv => kv.Value > 0 || kv.Key is "hashed" or "ok")
                          .Select(kv => $"{kv.Value} {kv.Key.Replace('_', ' ')}");
        Console.WriteLine(string.Join(", ", parts));
    }
}
