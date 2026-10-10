using System.Reflection;
using Platform.SharedKernel.Primitives;

namespace Platform.ArchitectureTests.Support;

/// <summary>
/// The rules behind DomainHardeningTests (01 §8): setters private, concrete roots sealed, no public
/// constructors (factories instead). Written as pure functions so GateSelfTests can prove they bite.
/// </summary>
internal static class DomainHardeningGate
{
    private const BindingFlags DeclaredInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>Every class deriving (at any depth) from <see cref="Entity{TId}"/>, abstract ones included.</summary>
    public static IReadOnlyList<Type> EntitiesIn(Assembly assembly) =>
        assembly.GetTypes().Where(type => type.IsClass && DerivesFrom(type, typeof(Entity<>))).ToList();

    /// <summary>Every class deriving (at any depth) from <see cref="AggregateRoot{TId}"/>, abstract ones included.</summary>
    public static IReadOnlyList<Type> AggregateRootsIn(Assembly assembly) =>
        assembly.GetTypes().Where(type => type.IsClass && DerivesFrom(type, typeof(AggregateRoot<>))).ToList();

    /// <summary>Properties with a public <c>set</c> or <c>init</c> accessor. Illegal states become representable through them (pitfall P-11).</summary>
    public static IReadOnlyList<string> PublicSetterViolations(IEnumerable<Type> types) =>
        types.SelectMany(type => type.GetProperties(DeclaredInstance)
                .Where(property => property.SetMethod is { IsPublic: true })
                .Select(property => $"{type.FullName}.{property.Name}"))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    /// <summary>Concrete aggregate roots that are not sealed.</summary>
    public static IReadOnlyList<string> UnsealedViolations(IEnumerable<Type> roots) =>
        roots.Where(type => !type.IsAbstract && !type.IsSealed).Select(type => type.FullName ?? type.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

    /// <summary>Concrete aggregate roots exposing a public constructor; creation goes through a factory that requires every value.</summary>
    public static IReadOnlyList<string> PublicConstructorViolations(IEnumerable<Type> roots) =>
        roots.Where(type => !type.IsAbstract && type.GetConstructors(BindingFlags.Instance | BindingFlags.Public).Length > 0)
            .Select(type => type.FullName ?? type.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    public static bool DerivesFrom(Type type, Type openGenericBase)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == openGenericBase)
            {
                return true;
            }
        }

        return false;
    }
}
