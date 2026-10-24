using Xunit;

namespace FileHasher.Cli.Tests;

/// <summary>
/// Pins the command surface in docs/cli-design.md. Every case here is a line
/// from that document, so a change in behaviour shows up as a failing test
/// rather than as a surprise in someone's pipeline.
/// </summary>
public sealed class CommandLineTests
{
    static ParsedArgs P(params string[] a) => CommandLine.Parse(a);

    [Fact]
    public void Defaults_match_the_documented_surface()
    {
        var a = P("hash", "/tmp");
        Assert.Equal(Verb.Hash, a.Verb);
        Assert.Equal("SHA256", a.Algorithm);
        Assert.Null(a.MaxDepth);              // unlimited, as the GUI does
        Assert.False(a.AllTypes);
        Assert.False(a.Json);
        Assert.Equal("sha256sum", a.SidecarFormat);
    }

    [Theory]
    [InlineData("md5", "MD5", ".md5")]
    [InlineData("sha1", "SHA1", ".sha1")]
    [InlineData("SHA256", "SHA256", ".sha256")]
    [InlineData("sha512", "SHA512", ".sha512")]
    public void Algorithm_is_case_insensitive_and_drives_the_sidecar_extension(string input, string expected, string ext)
    {
        Assert.Equal(expected, P("hash", "/tmp", "-a", input).Algorithm);
        Assert.Equal(ext, CommandLine.DefaultSidecarExt(expected));
    }

    [Theory]
    [InlineData("-r")]
    [InlineData("--recurse")]
    [InlineData("--recursive")]
    public void Recurse_flags_are_accepted_as_explicit_no_ops(string flag)
    {
        // Decision: accepting these costs three lines and failing on them
        // would be hostile, since recursion is already the default.
        Assert.Null(P("hash", "/tmp", flag).MaxDepth);
    }

    [Theory]
    [InlineData("--no-recurse")]
    [InlineData("--no-recursive")]
    public void No_recurse_is_the_same_instruction_as_max_depth_zero(string flag)
    {
        Assert.Equal(0, P("hash", "/tmp", flag).MaxDepth);
        Assert.Equal(0, P("hash", "/tmp", "--max-depth", "0").MaxDepth);
    }

    [Fact]
    public void No_recurse_and_max_depth_zero_agree_rather_than_conflict()
    {
        Assert.Equal(0, P("hash", "/tmp", "--no-recurse", "--max-depth", "0").MaxDepth);
    }

    [Fact]
    public void No_recurse_contradicting_a_positive_depth_is_a_usage_error()
    {
        Assert.Throws<UsageException>(() => P("hash", "/tmp", "--no-recurse", "--max-depth", "3"));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("abc")]
    public void Max_depth_rejects_anything_that_is_not_a_whole_number(string v)
        => Assert.Throws<UsageException>(() => P("hash", "/tmp", "--max-depth", v));

    [Fact]
    public void Fail_on_defaults_reproduce_the_exit_code_table()
    {
        Assert.Equal(FailOn.Unreadable, P("hash", "/tmp").FailOn);
        Assert.Equal(FailOn.Mismatch | FailOn.Missing | FailOn.Unreadable, P("verify", "/tmp").FailOn);
    }

    [Fact]
    public void Fail_on_any_expands_to_what_applies_to_that_verb()
    {
        Assert.Equal(FailOn.HashAny,   P("hash", "/tmp", "--fail-on", "any").FailOn);
        Assert.Equal(FailOn.VerifyAny, P("verify", "/tmp", "--fail-on", "any").FailOn);
    }

    [Fact]
    public void Fail_on_none_never_fails_and_cannot_be_combined()
    {
        Assert.Equal(FailOn.None, P("verify", "/tmp", "--fail-on", "none").FailOn);
        Assert.Throws<UsageException>(() => P("verify", "/tmp", "--fail-on", "none,mismatch"));
    }

    [Theory]
    [InlineData("mismatch")]
    [InlineData("missing")]
    [InlineData("no-sidecar")]
    public void Verify_only_conditions_are_rejected_for_hash(string token)
    {
        // Silently ignoring an inapplicable token would hide a CI typo.
        Assert.Throws<UsageException>(() => P("hash", "/tmp", "--fail-on", token));
        Assert.True(P("verify", "/tmp", "--fail-on", token).FailOn != FailOn.None);
    }

    [Fact]
    public void Unknown_tokens_options_and_verbs_are_usage_errors()
    {
        Assert.Throws<UsageException>(() => P("hash", "/tmp", "--fail-on", "sideways"));
        Assert.Throws<UsageException>(() => P("hash", "/tmp", "--bogus"));
        Assert.Throws<UsageException>(() => P("frobnicate", "/tmp"));
        Assert.Throws<UsageException>(() => P("hash", "/tmp", "-a", "sha999"));
    }

    [Fact]
    public void A_missing_path_or_a_second_path_is_a_usage_error()
    {
        Assert.Throws<UsageException>(() => P("hash"));
        Assert.Throws<UsageException>(() => P("hash", "/tmp", "/var"));
    }

    [Fact]
    public void An_option_that_needs_a_value_says_so_rather_than_reading_past_the_end()
        => Assert.Throws<UsageException>(() => P("hash", "/tmp", "--max-depth"));

    [Fact]
    public void Sidecar_extension_gains_its_dot_and_formats_map_to_core_names()
    {
        Assert.Equal(".foo", P("hash", "/tmp", "--sidecar-ext", "foo").SidecarExt);
        Assert.Equal(".foo", P("hash", "/tmp", "--sidecar-ext", ".foo").SidecarExt);
        Assert.Equal("hashonly", P("hash", "/tmp", "--sidecar-format", "hash").SidecarFormat);
        Assert.Equal("extended", P("hash", "/tmp", "--sidecar-format", "extended").SidecarFormat);
    }

    [Fact]
    public void Follow_symlinks_is_refused_rather_than_silently_ignored()
    {
        // It is not implemented yet. Accepting it would promise cycle-safe
        // traversal the walk does not perform.
        var ex = Assert.Throws<UsageException>(() => P("hash", "/tmp", "--follow-symlinks"));
        Assert.Contains("not implemented", ex.Message);
    }
}
