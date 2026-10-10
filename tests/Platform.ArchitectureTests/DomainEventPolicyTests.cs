using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application;
using Platform.ArchitectureTests.Support;
using Platform.Infrastructure.Persistence.Outbox;
using Platform.SharedKernel.Primitives;

namespace Platform.ArchitectureTests;

/// <summary>
/// Events and policies (03 §7, MODULES.md §1): every domain event in Platform.Domain is a sealed record that the
/// outbox can resolve by name and round-trip through JSON; every policy is a sealed class under a <c>Policies</c>
/// folder that <c>AddApplication</c> registers. The self-test in GateSelfTests proves the policy rules bite.
/// </summary>
public sealed class DomainEventPolicyTests
{
    private static readonly DomainEventRegistry Registry = new();

    [Fact]
    public void Every_domain_event_is_a_sealed_record_the_registry_resolves()
    {
        var events = SolutionAssemblies.DomainAssembly.GetTypes().Where(type => type.IsClass && !type.IsAbstract && typeof(IDomainEvent).IsAssignableFrom(type)).ToList();
        Assert.NotEmpty(events);

        foreach (var type in events)
        {
            Assert.True(type.IsSealed, $"{type.Name} must be sealed");
            Assert.True(type.GetMethod("<Clone>$") is not null, $"{type.Name} must be a record");
            Assert.Same(type, Registry.Resolve(type.FullName!));
        }
    }

    [Fact]
    public void Every_domain_event_round_trips_through_the_outbox_serializer()
    {
        // The processor rebuilds the event from its stored payload; a constructor the serializer cannot satisfy would dead-letter every row of that type.
        foreach (var type in Registry.Types)
        {
            var sample = EventSamples.Sample(type);
            var json = JsonSerializer.Serialize(sample, type, OutboxEvent.SerializerOptions);
            var restored = JsonSerializer.Deserialize(json, type, OutboxEvent.SerializerOptions);
            Assert.NotNull(restored);
            Assert.Equal(json, JsonSerializer.Serialize(restored, type, OutboxEvent.SerializerOptions));
            Assert.Equal(((IDomainEvent)sample).EventId, ((IDomainEvent)restored).EventId);
        }
    }

    [Fact]
    public void Every_policy_is_sealed_lives_under_Policies_and_is_registered_by_AddApplication()
    {
        var policies = ApplicationServiceRegistration.ClosedDomainEventHandlers(SolutionAssemblies.ApplicationAssembly);
        var violations = PolicyGate.Violations(policies);
        Assert.True(violations.Count == 0, "Policies breaking the rule: " + string.Join(", ", violations));

        var services = new ServiceCollection().AddApplication();
        foreach (var (service, implementation) in policies)
        {
            Assert.Contains(services, descriptor => descriptor.ServiceType == service && descriptor.ImplementationType == implementation && descriptor.Lifetime == ServiceLifetime.Scoped);
        }
    }
}

/// <summary>The policy rule as a pure function, so GateSelfTests can run it over deliberately wrong fixtures.</summary>
internal static class PolicyGate
{
    public static IReadOnlyList<string> Violations(IEnumerable<(Type Service, Type Implementation)> policies) =>
        policies.SelectMany(pair => Check(pair.Implementation)).Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToList();

    private static IEnumerable<string> Check(Type implementation)
    {
        if (!implementation.IsSealed)
        {
            yield return $"{implementation.FullName} is not sealed";
        }

        if (implementation.Namespace is null || !implementation.Namespace.Split('.').Contains("Policies", StringComparer.Ordinal))
        {
            yield return $"{implementation.FullName} is not under a Policies namespace";
        }
    }
}
