using System.Text.RegularExpressions;
using Platform.ArchitectureTests.Support;

namespace Platform.ArchitectureTests;

/// <summary>
/// Gates of intake LIS-INT-ORG-001 (MODULE-PATTERN §5 "add a gate when your module introduces a new rule"):
/// SOD-ORG-001 in every access-administration handler, cache eviction after every privilege change (BR-17), and the
/// provisioning adapters that only stage rows for the caller's single commit.
/// </summary>
public sealed partial class OrganizationGateTests
{
    private static string Source(params string[] relative) =>
        File.ReadAllText(Path.Combine([SolutionAssemblies.RepositoryRoot, .. relative]));

    private static IReadOnlyList<string> HandlerBodies(string source) =>
        HandlerStart().Split(source).Skip(1).ToList();

    [Fact]
    public void Every_access_administration_handler_refuses_the_actor_as_target()
    {
        var bodies = HandlerBodies(Source("src", "Platform.Application", "Organization", "Commands", "UserAccessCommands.cs"));

        Assert.Equal(8, bodies.Count);
        Assert.All(bodies, body => Assert.Contains("OrganizationGuards.EnsureNotSelf(", body, StringComparison.Ordinal));
    }

    [Fact]
    public void Every_privilege_change_evicts_the_principals_it_affects_after_the_commit()
    {
        var roles = HandlerBodies(Source("src", "Platform.Application", "Organization", "Commands", "RoleCommands.cs"))
            .Where(body => body.Contains("SetPermissions(", StringComparison.Ordinal) || body.Contains("SetSpecialRights(", StringComparison.Ordinal) || body.Contains("SetResultRights(", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(3, roles.Count);
        Assert.All(roles, body => Assert.True(
            body.IndexOf("SaveChangesAsync(", StringComparison.Ordinal) < body.IndexOf("EvictRoleMembersAsync(", StringComparison.Ordinal),
            "a role privilege handler evicts the members' principals after its commit"));

        var members = HandlerBodies(Source("src", "Platform.Application", "Organization", "Commands", "UserAccessCommands.cs"))
            .Where(body => body.Contains("AssignRole(", StringComparison.Ordinal) || body.Contains("SetOverrides(", StringComparison.Ordinal) || body.Contains("RestoreToRole(", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(3, members.Count);
        Assert.All(members, body => Assert.Contains("EvictUserAsync(", body, StringComparison.Ordinal));
    }

    [Fact]
    public void The_provisioning_adapters_stage_rows_and_never_commit()
    {
        var source = Source("src", "Platform.Infrastructure", "Persistence", "Organization", "TenantProvisioningAdapters.cs");

        Assert.DoesNotContain("SaveChanges", source, StringComparison.Ordinal);
        Assert.Contains("ProvisioningScope.Ensure(", source, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"public sealed class \w+Handler\(")]
    private static partial Regex HandlerStart();
}
