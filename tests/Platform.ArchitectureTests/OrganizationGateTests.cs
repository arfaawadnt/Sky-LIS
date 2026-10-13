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

    /// <summary>
    /// Validation F-07: a laboratory role holds only the keys a laboratory may grant (ORG-033 refuses the platform-only modules on
    /// every later save). The development seeds build laboratory roles from <c>EffectivePrivileges.TenantGrantableKeys</c>; the
    /// whole catalogue is the platform administrator's alone (<c>PlatformAdministratorSeeder</c>).
    /// </summary>
    [Fact]
    public void Development_seeds_never_give_a_laboratory_role_the_whole_catalogue()
    {
        var devTools = Source("tools", "Platform.DevTools", "Program.cs");
        var roleCreations = RoleCreation().Matches(devTools).Select(match => match.Value).ToList();

        Assert.NotEmpty(roleCreations);
        Assert.All(roleCreations, call => Assert.DoesNotContain("PermissionCatalog.Keys", call, StringComparison.Ordinal));
        Assert.Contains(roleCreations, call => call.Contains("EffectivePrivileges.TenantGrantableKeys", StringComparison.Ordinal));
    }

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

    [GeneratedRegex(@"Role\.Create(ForTenant)?\([^;]*;", RegexOptions.Singleline)]
    private static partial Regex RoleCreation();
}
