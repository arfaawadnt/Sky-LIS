using Platform.ArchitectureTests.Support;

namespace Platform.ArchitectureTests;

/// <summary>
/// Deny-by-default on commands and queries (D-04). A request without a policy, a policy naming an unknown
/// permission, a handler for an ungated request or a role attribute anywhere fails the build.
/// </summary>
public sealed class RequestPolicyTests
{
    private static readonly IReadOnlyList<Type> Requests = RequestPolicyGate.RequestsIn(SolutionAssemblies.ApplicationAssembly);

    [Fact]
    public void Every_command_and_query_declares_exactly_one_policy()
    {
        var violations = RequestPolicyGate.MissingOrDuplicatePolicy(Requests);
        Assert.True(
            violations.Count == 0,
            "Requests must carry exactly one of [RequirePermissionPolicy], [RequireAuthenticatedActor], [AllowUnauthenticated]: " + string.Join(", ", violations));
    }

    [Fact]
    public void Every_permission_policy_names_a_catalogued_permission()
    {
        var violations = RequestPolicyGate.UnknownPermissions(Requests);
        Assert.True(violations.Count == 0, "Policies naming permissions outside PermissionCatalog: " + string.Join(", ", violations));
    }

    [Fact]
    public void Every_handler_handles_a_gated_request()
    {
        var violations = RequestPolicyGate.UngatedHandlers(SolutionAssemblies.ApplicationAssembly);
        Assert.True(violations.Count == 0, "Handlers written against raw MediatR requests bypass the gate: " + string.Join(", ", violations));
    }

    [Fact]
    public void No_role_based_policy_attribute_exists()
    {
        var violations = RequestPolicyGate.RoleAttributes([SolutionAssemblies.ApplicationAssembly, SolutionAssemblies.WebApiAssembly, SolutionAssemblies.InfrastructureAssembly]);
        Assert.True(violations.Count == 0, "Roles are tenant-configurable and never a policy; remove: " + string.Join(", ", violations));
    }
}
