using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Abstractions.Security;
using Platform.Domain.Identity;
using Platform.Domain.Organization;
using Platform.Domain.Tenancy;
using Platform.Domain.Treasury;
using Platform.Infrastructure.Persistence.Conventions;
using Platform.Infrastructure.Persistence.Outbox;
using Platform.Infrastructure.Security.CredentialTransport;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Infrastructure.Persistence;

/// <summary>
/// The one DbContext (01 §2.3, C-19). Tenant keys, the tenant query filter and the xmin concurrency token are
/// applied by convention to every <see cref="ITenantScoped"/> type, so a new tenant table cannot forget them.
/// Row-level security in PostgreSQL is the second, independent layer (01 §4.4); the interceptor that stamps the
/// tenant on every connection is registered first (01 §4.5).
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContextAccessor tenant) : DbContext(options)
{
    public const string IdentitySchema = "identity";
    public const string AppSchema = "app";
    public const string AuditSchema = "audit";
    public const string SaasSchema = "saas";
    public const string ReadSchema = "read";
    public const string OpsSchema = "ops";
    public const int EnumColumnLength = 40;
    public const int DisplayNameLength = 200;

    public DbSet<PlatformUser> PlatformUsers => Set<PlatformUser>();

    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();

    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();

    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

    public DbSet<EnvelopeKey> EnvelopeKeys => Set<EnvelopeKey>();

    public DbSet<CredentialNonce> CredentialNonces => Set<CredentialNonce>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<Domain.Treasury.Treasury> Treasuries => Set<Domain.Treasury.Treasury>();

    public DbSet<TreasuryAccessRequest> TreasuryAccessRequests => Set<TreasuryAccessRequest>();

    public DbSet<ReferenceCounter> ReferenceCounters => Set<ReferenceCounter>();

    // Query-filter inputs. The filter expression captures this context instance; EF Core rewrites that capture
    // per query, so every query reads the scope of the context it runs on (the documented multi-tenant pattern).
    public Guid CurrentTenantIdOrNil => tenant.CurrentTenantId ?? Guid.Empty;

    public bool IsElevated => tenant.IsElevated;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Conventions.Add(_ => new TenantKeyConvention());
        configurationBuilder.Conventions.Add(_ => new XminConcurrencyConvention());
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(EnumColumnLength);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(type => !type.IsOwned()).ToList())
        {
            if (typeof(IDomainEventSource).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType).Ignore(nameof(IDomainEventSource.DomainEvents));
            }

            if (typeof(IAuditStamped).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType).Property("CreatedByDisplayName").HasMaxLength(DisplayNameLength);
                modelBuilder.Entity(entityType.ClrType).Property("ModifiedByDisplayName").HasMaxLength(DisplayNameLength);
            }

            if (typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(BuildTenantFilter(entityType.ClrType));
            }
        }

        ApplyEnumDomainConstraints(modelBuilder);
    }

    /// <summary><c>e =&gt; context.IsElevated || e.TenantId == context.CurrentTenantIdOrNil</c>: an unresolved tenant matches nothing (fail-closed).</summary>
    private LambdaExpression BuildTenantFilter(Type clrType)
    {
        var entity = Expression.Parameter(clrType, "e");
        var context = Expression.Constant(this, typeof(AppDbContext));
        var body = Expression.OrElse(
            Expression.Property(context, nameof(IsElevated)),
            Expression.Equal(
                Expression.Property(entity, nameof(ITenantScoped.TenantId)),
                Expression.Property(context, nameof(CurrentTenantIdOrNil))));
        return Expression.Lambda(body, entity);
    }

    /// <summary>Checklist item 6 (01 §4.2): every enum column carries <c>ck_&lt;table&gt;_&lt;column&gt;_domain</c> listing the C# members, so a new member needs a migration (pitfall P-09).</summary>
    private static void ApplyEnumDomainConstraints(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(type => !type.IsOwned()).ToList())
        {
            var table = entityType.GetTableName();
            if (table is null)
            {
                continue;
            }

            foreach (var property in entityType.GetProperties().ToList())
            {
                var enumType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (!enumType.IsEnum)
                {
                    continue;
                }

                var column = property.GetColumnName();
                var members = string.Join(", ", Enum.GetNames(enumType).Select(name => $"'{name}'"));
                var constraint = $"ck_{table}_{column}_domain";
                modelBuilder.Entity(entityType.ClrType).ToTable(t => t.HasCheckConstraint(constraint, $"\"{column}\" IN ({members})"));
            }
        }
    }
}
