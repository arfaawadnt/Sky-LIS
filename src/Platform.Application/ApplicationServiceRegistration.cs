using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Behaviors;
using Platform.Application.Identity;
using Platform.SharedKernel.Primitives;

namespace Platform.Application;

/// <summary>Registers the dispatcher, the pipeline behaviors in their fixed order, every validator and every domain-event policy in this assembly.</summary>
public static class ApplicationServiceRegistration
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var assembly = typeof(ApplicationServiceRegistration).Assembly;

        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(assembly);

            // Fixed order (01 §3.4): Tracing → Logging → Authorization → Validation → Idempotency.
            // Tracing, Logging and Idempotency are added by their own work packages; the order is asserted by InterceptorOrderTests.
            configuration.AddOpenBehavior(typeof(AuthorizationBehavior<,>));
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        services.AddDomainEventHandlers(assembly);
        services.AddScoped<PlatformAdministratorSeeder>();

        return services;
    }

    /// <summary>
    /// The policy registration rule (MODULES.md §1): every concrete, non-generic class in the assembly that implements
    /// a closed <see cref="IDomainEventHandler{TEvent}"/> is registered, scoped, under each closed interface it
    /// implements. The outbox processor resolves <c>IEnumerable&lt;IDomainEventHandler&lt;TEvent&gt;&gt;</c> per event.
    /// The infrastructure convention skips generic ports, so a policy is never mistaken for an adapter.
    /// </summary>
    public static IServiceCollection AddDomainEventHandlers(this IServiceCollection services, System.Reflection.Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (var (service, implementation) in ClosedDomainEventHandlers(assembly))
        {
            services.AddScoped(service, implementation);
        }

        return services;
    }

    /// <summary>Every (closed handler interface, implementation) pair the rule above registers; shared with the architecture gate.</summary>
    public static IReadOnlyList<(Type Service, Type Implementation)> ClosedDomainEventHandlers(System.Reflection.Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .SelectMany(type => type.GetInterfaces()
                .Where(contract => contract.IsGenericType
                    && contract.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>)
                    && typeof(IDomainEvent).IsAssignableFrom(contract.GetGenericArguments()[0]))
                .Select(contract => (contract, type)))
            .OrderBy(pair => pair.type.FullName, StringComparer.Ordinal)
            .ToList();
    }
}
