using Platform.ArchitectureTests.Support;

namespace Platform.ArchitectureTests;

/// <summary>
/// The domain protects itself (00 §1.3, 01 §8): state changes only through aggregate methods. Setters are
/// private, concrete roots are sealed and created through factories. Pitfalls P-10 and P-11.
/// </summary>
public sealed class DomainHardeningTests
{
    private static readonly IReadOnlyList<Type> Entities = DomainHardeningGate.EntitiesIn(SolutionAssemblies.DomainAssembly);
    private static readonly IReadOnlyList<Type> AggregateRoots = DomainHardeningGate.AggregateRootsIn(SolutionAssemblies.DomainAssembly);

    [Fact]
    public void Aggregate_roots_expose_no_public_setters()
    {
        var violations = DomainHardeningGate.PublicSetterViolations(AggregateRoots);
        Assert.True(violations.Count == 0, "Aggregate roots with a public set or init accessor: " + string.Join(", ", violations));
    }

    [Fact]
    public void Entities_expose_no_public_setters()
    {
        var violations = DomainHardeningGate.PublicSetterViolations(Entities);
        Assert.True(violations.Count == 0, "Entities with a public set or init accessor: " + string.Join(", ", violations));
    }

    [Fact]
    public void Concrete_aggregate_roots_are_sealed()
    {
        var violations = DomainHardeningGate.UnsealedViolations(AggregateRoots);
        Assert.True(violations.Count == 0, "Unsealed aggregate roots: " + string.Join(", ", violations));
    }

    [Fact]
    public void Concrete_aggregate_roots_have_no_public_constructors()
    {
        var violations = DomainHardeningGate.PublicConstructorViolations(AggregateRoots);
        Assert.True(violations.Count == 0, "Aggregate roots creatable without a factory: " + string.Join(", ", violations));
    }
}
