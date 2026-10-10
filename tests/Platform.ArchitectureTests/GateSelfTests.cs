using System.Reflection;
using Platform.ArchitectureTests.Support;
using Platform.ArchitectureTests.Support.Fixtures;

namespace Platform.ArchitectureTests;

/// <summary>
/// Runs every gate against the deliberately wrong fixtures in this assembly. While the production assemblies
/// hold few or no requests and aggregates, these tests are the evidence that an empty result means "clean"
/// rather than "the gate found nothing to look at" (pitfall P-71).
/// </summary>
public sealed class GateSelfTests
{
    private static readonly Assembly Self = typeof(GateSelfTests).Assembly;

    [Fact]
    public void Policy_gate_flags_missing_and_duplicate_policies_and_accepts_a_correct_one()
    {
        var violations = RequestPolicyGate.MissingOrDuplicatePolicy(RequestPolicyGate.RequestsIn(Self));

        Assert.Contains(violations, v => v.StartsWith(typeof(UngatedFixtureCommand).FullName!, StringComparison.Ordinal));
        Assert.Contains(violations, v => v.StartsWith(typeof(DoublePolicyFixtureCommand).FullName!, StringComparison.Ordinal));
        Assert.DoesNotContain(violations, v => v.StartsWith(typeof(GatedFixtureCommand).FullName!, StringComparison.Ordinal));
    }

    [Fact]
    public void Policy_gate_flags_a_permission_outside_the_catalogue()
    {
        var violations = RequestPolicyGate.UnknownPermissions(RequestPolicyGate.RequestsIn(Self));

        Assert.Contains(violations, v => v.StartsWith(typeof(UnknownPermissionFixtureQuery).FullName!, StringComparison.Ordinal));
        Assert.DoesNotContain(violations, v => v.StartsWith(typeof(GatedFixtureCommand).FullName!, StringComparison.Ordinal));
    }

    [Fact]
    public void Handler_gate_flags_a_handler_for_a_raw_request_and_accepts_a_command_handler()
    {
        var violations = RequestPolicyGate.UngatedHandlers(Self);

        Assert.Contains(violations, v => v.StartsWith(typeof(RawFixtureRequestHandler).FullName!, StringComparison.Ordinal));
        Assert.DoesNotContain(violations, v => v.StartsWith(typeof(GatedFixtureCommandHandler).FullName!, StringComparison.Ordinal));
    }

    [Fact]
    public void Hardening_gate_flags_public_setters_including_init_accessors()
    {
        var roots = DomainHardeningGate.AggregateRootsIn(Self);
        var violations = DomainHardeningGate.PublicSetterViolations(roots);

        Assert.Contains($"{typeof(LeakyFixtureAggregate).FullName}.Name", violations);
        Assert.Contains($"{typeof(LeakyFixtureAggregate).FullName}.Count", violations);
        Assert.DoesNotContain(violations, v => v.StartsWith(typeof(HardenedFixtureAggregate).FullName!, StringComparison.Ordinal));
    }

    [Fact]
    public void Policy_gate_flags_an_unsealed_handler_outside_Policies_and_accepts_a_correct_one()
    {
        var policies = Platform.Application.ApplicationServiceRegistration.ClosedDomainEventHandlers(Self);
        Assert.Contains(policies, pair => pair.Implementation == typeof(LeakyFixturePolicy));
        Assert.Contains(policies, pair => pair.Implementation == typeof(Support.Fixtures.Policies.CorrectFixturePolicy));

        var violations = PolicyGate.Violations(policies);
        Assert.Contains(violations, v => v.StartsWith(typeof(LeakyFixturePolicy).FullName!, StringComparison.Ordinal) && v.EndsWith("not sealed", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.StartsWith(typeof(LeakyFixturePolicy).FullName!, StringComparison.Ordinal) && v.EndsWith("Policies namespace", StringComparison.Ordinal));
        Assert.DoesNotContain(violations, v => v.StartsWith(typeof(Support.Fixtures.Policies.CorrectFixturePolicy).FullName!, StringComparison.Ordinal));
    }

    [Fact]
    public void Hardening_gate_flags_unsealed_roots_and_public_constructors()
    {
        var roots = DomainHardeningGate.AggregateRootsIn(Self);

        Assert.Contains(typeof(LeakyFixtureAggregate).FullName!, DomainHardeningGate.UnsealedViolations(roots));
        Assert.Contains(typeof(LeakyFixtureAggregate).FullName!, DomainHardeningGate.PublicConstructorViolations(roots));
        Assert.DoesNotContain(typeof(HardenedFixtureAggregate).FullName!, DomainHardeningGate.UnsealedViolations(roots));
        Assert.DoesNotContain(typeof(HardenedFixtureAggregate).FullName!, DomainHardeningGate.PublicConstructorViolations(roots));
    }
}
