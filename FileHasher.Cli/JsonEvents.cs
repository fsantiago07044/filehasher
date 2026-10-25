using System.Text.Json.Serialization;

namespace FileHasher.Cli;

/// <summary>
/// One line of <c>--json</c> output for a hashed file.
/// Concrete records rather than anonymous objects because the source generator
/// cannot see an anonymous type, and reflection-based serialisation does not
/// survive trimming.
/// </summary>
public sealed record HashEvent(
    string    Type,
    string    Path,
    string?   Hash,
    string    Algorithm,
    long?     Size,
    DateTime? Modified,
    string?   Container,
    bool      Ok,
    string?   Error);

/// <summary>One line of <c>--json</c> output for a verified file.</summary>
public sealed record VerifyEvent(
    string  Type,
    string  Path,
    string? Sidecar,
    string  Status,
    string? Algorithm,
    string? Computed,
    string? Detail);

/// <summary>The final line of a <c>--json</c> run.</summary>
public sealed record SummaryEvent(
    string Type,
    IReadOnlyDictionary<string, int> Counts);

/// <summary>
/// Source-generated serialisation for the three event shapes above.
/// camelCase matches the output the CLI emitted before the switch, so the
/// wire format is unchanged; only how it is produced differs.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(HashEvent))]
[JsonSerializable(typeof(VerifyEvent))]
[JsonSerializable(typeof(SummaryEvent))]
internal sealed partial class CliJsonContext : JsonSerializerContext;
