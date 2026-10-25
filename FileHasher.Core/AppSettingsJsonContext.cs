using System.Text.Json.Serialization;

namespace FileHasher;

/// <summary>
/// Source-generated serialisation for <see cref="AppSettings"/>.
///
/// Reflection-based System.Text.Json cannot survive trimming: the linker has no
/// way to see which types the serialiser will reach, drops them, and the
/// failure appears at runtime on a user's machine rather than in a build. The
/// CLI ships as a trimmed self-contained binary (71 MB down to 14 MB), so this
/// is a correctness requirement there, not an optimisation. The desktop app
/// shares the same code and simply benefits.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext;
