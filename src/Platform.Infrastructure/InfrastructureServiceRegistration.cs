using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Application.Abstractions.Audit;
using Platform.Application.Abstractions.CredentialTransport;
using Platform.Application.Abstractions.Notifications;
using Platform.Application.Abstractions.Observability;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Tokens;
using Platform.Application.Identity;
using Platform.Application.Organization;
using Platform.Application.Tenancy;
using Platform.Application.PlatformAdmin.Jobs;
using Platform.Infrastructure.Audit;
using Platform.Infrastructure.Jobs;
using Platform.Infrastructure.Notifications;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Persistence.Identity;
using Platform.Infrastructure.Persistence.Interceptors;
using Platform.Infrastructure.Persistence.Organization;
using Platform.Infrastructure.Persistence.Outbox;
using Platform.Infrastructure.Persistence.Tenancy;
using Platform.Infrastructure.Security;
using Platform.Infrastructure.Security.CredentialTransport;
using Platform.Infrastructure.Security.Hashing;
using Platform.Infrastructure.Security.Otp;
using Platform.Infrastructure.Security.Protection;
using Platform.Infrastructure.Security.Tokens;
using Platform.SharedKernel.Abstractions;

namespace Platform.Infrastructure;

/// <summary>Registers the adapters behind the Application ports. Each work package adds its registrations here and nowhere else.</summary>
public static class InfrastructureServiceRegistration
{
    public const string PoolResetViolation = "The connection string must not contain 'No Reset On Close': pooled connections would keep another scope's tenant (01 §4.5).";

    /// <summary>Persistence with a known connection string (tests, tools). Refuses a string that disables pool resets at once.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string postgresConnectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(postgresConnectionString);
        if (postgresConnectionString.Contains("No Reset On Close", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(PoolResetViolation);
        }

        return services.AddInfrastructure(_ => postgresConnectionString);
    }

    /// <summary>
    /// Persistence: the DbContext with its interceptors in fixed order, the unit of work and the repositories. The
    /// connection string is resolved when the context is first built, so host configuration added late (as a test
    /// host does) is honoured.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, Func<IServiceProvider, string> postgresConnectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(postgresConnectionString);

        services.AddSingleton<IClock, SystemClock>();

        // A host that configured logging keeps it; a tool or a test provider without logging gets the null logger instead of a resolution failure.
        services.TryAddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.TryAdd(ServiceDescriptor.Singleton(typeof(ILogger<>), typeof(Logger<>)));

        services.AddScoped<TenantContextAccessor>();
        services.AddScoped<ITenantContextAccessor>(provider => provider.GetRequiredService<TenantContextAccessor>());
        services.AddScoped<ICurrentTenant>(provider => provider.GetRequiredService<TenantContextAccessor>());
        services.AddScoped<ICurrentTenantSetter>(provider => provider.GetRequiredService<TenantContextAccessor>());

        services.AddScoped<CurrentUserAccessor>();
        services.AddScoped<ICurrentUser>(provider => provider.GetRequiredService<CurrentUserAccessor>());

        services.AddScoped<UserPrivilegesAccessor>();
        services.AddScoped<IUserPrivileges>(provider => provider.GetRequiredService<UserPrivilegesAccessor>());

        services.AddScoped<RequestContextAccessor>();
        services.AddScoped<IRequestContext>(provider => provider.GetRequiredService<RequestContextAccessor>());

        services.AddScoped<TenantConnectionInterceptor>();
        services.AddScoped<TenantStampInterceptor>();
        services.AddScoped<AuditStampInterceptor>();
        services.AddScoped<FieldChangeInterceptor>();
        services.AddScoped<AuditTrailInterceptor>();
        services.AddScoped<OutboxInterceptor>();

        services.AddDbContext<AppDbContext>((provider, options) =>
        {
            var connectionString = postgresConnectionString(provider);
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
            if (connectionString.Contains("No Reset On Close", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(PoolResetViolation);
            }

            AppDbContextOptions.Apply(options, connectionString);

            // Fixed order (InterceptorOrderTests): connection stamp first; on save tenant stamp → audit stamp → field changes
            // → audit trail (reads the events while they are still on the aggregates) → outbox (drains them).
            options.AddInterceptors(
                provider.GetRequiredService<TenantConnectionInterceptor>(),
                provider.GetRequiredService<TenantStampInterceptor>(),
                provider.GetRequiredService<AuditStampInterceptor>(),
                provider.GetRequiredService<FieldChangeInterceptor>(),
                provider.GetRequiredService<AuditTrailInterceptor>(),
                provider.GetRequiredService<OutboxInterceptor>());
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IPlatformUserRepository, PlatformUserRepository>();
        services.AddScoped<IRefreshSessionRepository, RefreshSessionRepository>();
        services.AddScoped<ITenantMembershipRepository, TenantMembershipRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IReferenceNumbers, ReferenceNumbers>();

        services.AddScoped<LoggerSecurityEventLog>();
        services.AddScoped<ISecurityEventLog, SecurityEventLedger>();

        // Outbox worker (01 §7): the host decides whether to run the loop; a tool or test can drive RunOnceAsync directly.
        services.TryAddSingleton(new OutboxOptions());
        services.AddSingleton<DomainEventRegistry>();
        services.AddSingleton<OutboxProcessor>();

        // Monthly wallet charge (W2, Lis:Jobs:WalletCharge): the same singleton serves the hosted loop and SA-05 "Run charges now".
        services.TryAddSingleton(new WalletChargeOptions());
        services.AddSingleton<WalletChargeJob>();
        services.TryAddSingleton<IWalletChargeRun>(provider => provider.GetRequiredService<WalletChargeJob>());
        services.TryAddSingleton(new SubscriptionReminderOptions());
        services.AddSingleton<SubscriptionReminderJob>();
        services.TryAddSingleton<ISubscriptionReminderRun>(provider => provider.GetRequiredService<SubscriptionReminderJob>());

        // Business modules register by convention (01 §3.1): every concrete class under Platform.Infrastructure.Persistence
        // or Platform.Infrastructure.Services that implements a port declared in Platform.Application is a scoped adapter.
        // Explicit registrations above win (TryAdd), and a port with two candidate adapters fails fast at start-up.
        services.AddPortAdaptersByConvention();

        // Foundation defaults for ports a later module may own (platform notification templates, an SMS gateway):
        // registered after the convention with TryAdd, so a module adapter found by convention wins.
        services.TryAddScoped<INotificationTemplateSource, DefaultNotificationTemplateSource>();

        // Delivery (Lis:Notifications): the host registers the options; a tool or test without them keeps the logging sender.
        // Pickup directory, SMTP or the HTTP SMS gateway when configured; otherwise the logging sender stays the default.
        services.TryAddSingleton(new NotificationOptions());
        services.TryAddSingleton<NotificationTransports>();
        services.TryAdd(ServiceDescriptor.Scoped<INotificationSender>(provider => provider.GetRequiredService<NotificationTransports>().Any
            ? ActivatorUtilities.CreateInstance<DeliveringNotificationSender>(provider)
            : ActivatorUtilities.CreateInstance<LoggingNotificationSender>(provider)));

        return services;
    }

    private static readonly Type[] ConventionExcludedPorts = [typeof(IRepository<>)];

    private static void AddPortAdaptersByConvention(this IServiceCollection services)
    {
        var applicationAssembly = typeof(IUnitOfWork).Assembly;
        var candidates = typeof(InfrastructureServiceRegistration).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
                && type.Namespace is { } ns
                && (ns.StartsWith("Platform.Infrastructure.Persistence", StringComparison.Ordinal) || ns.StartsWith("Platform.Infrastructure.Services", StringComparison.Ordinal)));

        // Ordinary adapters first; [FallbackAdapter] placeholders only fill ports nobody else implements.
        var ordered = candidates
            .OrderBy(type => type.IsDefined(typeof(FallbackAdapterAttribute), inherit: false))
            .ToList();

        var seen = new Dictionary<Type, Type>();
        foreach (var implementation in ordered)
        {
            var isFallback = implementation.IsDefined(typeof(FallbackAdapterAttribute), inherit: false);
            foreach (var port in implementation.GetInterfaces().Where(port => port.Assembly == applicationAssembly && !port.IsGenericType))
            {
                if (seen.TryGetValue(port, out var other) && other != implementation)
                {
                    if (isFallback)
                    {
                        continue;
                    }

                    throw new InvalidOperationException($"Port {port.Name} has two adapters by convention: {other.Name} and {implementation.Name}. Register one explicitly or mark the placeholder [FallbackAdapter].");
                }

                seen[port] = implementation;
                services.TryAddScoped(port, implementation);
            }
        }
    }

    /// <summary>
    /// The security edge adapters (02 §3–§6): envelope keys and nonces (shared store), subject protection, ES256 tokens,
    /// refresh secrets, Argon2id hashing, principal resolution, keyed digests and one-time codes. The host registers
    /// <see cref="SecurityOptions"/> and <see cref="SessionPolicy"/>; <see cref="ConfigGuard"/> runs against them at start-up, in every environment.
    /// </summary>
    public static IServiceCollection AddSecurity(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddMemoryCache();

        services.AddSingleton(provider => new MasterKeyProtector(provider.GetRequiredService<SecurityOptions>().MasterKey));
        services.AddSingleton<MasterKeyedHash>();
        services.AddSingleton<SigningKeys>();
        services.AddSingleton<ISubjectProtector, SubjectProtector>();
        services.AddSingleton<IAccessTokenService, AccessTokenService>();
        services.AddSingleton<IRefreshTokenService, RefreshTokenService>();
        services.AddSingleton<IPasswordHasher, Argon2idHasher>();

        services.AddSingleton<EnvelopeKeyStore>();
        services.AddScoped<NonceStore>();
        services.AddScoped<ICredentialChallengeIssuer, CredentialChallengeIssuer>();
        services.AddScoped<ICredentialEnvelopeOpener, CredentialEnvelopeOpener>();

        services.AddScoped<ISessionPrincipalQuery, SessionPrincipalQuery>();
        services.AddScoped<IMembershipDirectory, MembershipDirectory>();
        services.AddScoped<IOtpService, OtpService>();

        return services;
    }
}
