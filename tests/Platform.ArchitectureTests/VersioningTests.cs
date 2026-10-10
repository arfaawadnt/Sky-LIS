using System.Text.Json;
using System.Text.RegularExpressions;
using Platform.ArchitectureTests.Support;

namespace Platform.ArchitectureTests;

/// <summary>
/// Release rules (README "Versioning and releases"): VERSION at the repository root is the single product version,
/// every assembly and the frontend package carry it, and README.md records it with a release entry and an Unreleased
/// section. scripts/release.mjs keeps them aligned; the pre-push hook refuses a push that is not a tagged release.
/// </summary>
public sealed partial class VersioningTests
{
    private static readonly string Root = SolutionAssemblies.RepositoryRoot;
    private static string Version { get; } = File.ReadAllText(Path.Combine(Root, "VERSION")).Trim();

    [Fact]
    public void The_version_file_holds_a_semantic_version()
    {
        Assert.True(SemanticVersion().IsMatch(Version), $"VERSION holds '{Version}', expected MAJOR.MINOR.PATCH");
    }

    [Theory]
    [InlineData("1.2", false)]
    [InlineData("v1.2.3", false)]
    [InlineData("1.2.3-beta", false)]
    [InlineData("01.2.3", false)]
    [InlineData("0.1.1", true)]
    [InlineData("10.20.30", true)]
    public void The_semantic_version_pattern_refuses_malformed_versions(string candidate, bool valid)
    {
        Assert.Equal(valid, SemanticVersion().IsMatch(candidate));
    }

    [Fact]
    public void Every_assembly_carries_the_version_file()
    {
        foreach (var assembly in new[] { SolutionAssemblies.SharedKernelAssembly, SolutionAssemblies.DomainAssembly, SolutionAssemblies.WebApiAssembly })
        {
            var version = assembly.GetName().Version!;
            Assert.Equal(Version, version.ToString(3));
        }
    }

    [Fact]
    public void The_frontend_package_carries_the_version_file()
    {
        using var package = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "frontend", "package.json")));
        Assert.Equal(Version, package.RootElement.GetProperty("version").GetString());

        using var lockFile = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "frontend", "package-lock.json")));
        Assert.Equal(Version, lockFile.RootElement.GetProperty("version").GetString());
        Assert.Equal(Version, lockFile.RootElement.GetProperty("packages").GetProperty("").GetProperty("version").GetString());
    }

    [Fact]
    public void The_readme_records_the_current_version()
    {
        var readme = File.ReadAllText(Path.Combine(Root, "README.md"));
        Assert.Contains($"<!-- version:start -->**Version {Version}**", readme, StringComparison.Ordinal);
        Assert.Contains("## Release history", readme, StringComparison.Ordinal);
        Assert.Contains("### Unreleased", readme, StringComparison.Ordinal);
        Assert.Contains($"### v{Version} — ", readme, StringComparison.Ordinal);
        Assert.True(readme.IndexOf("### Unreleased", StringComparison.Ordinal) < readme.IndexOf($"### v{Version} — ", StringComparison.Ordinal), "the Unreleased section comes before the latest release entry");
    }

    [Fact]
    public void The_pre_push_hook_runs_the_release_check()
    {
        var hook = File.ReadAllText(Path.Combine(Root, ".githooks", "pre-push"));
        Assert.Contains("scripts/check-release.mjs", hook, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n", hook, StringComparison.Ordinal);
    }

    [GeneratedRegex("^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$")]
    private static partial Regex SemanticVersion();
}
