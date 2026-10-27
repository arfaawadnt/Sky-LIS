using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.MultiTenancy;

namespace Platform.Infrastructure.Persistence.Conventions;

/// <summary>
/// Database team review 2026-10-26, comment 3: every table with a <c>tenant_id</c> column references the registry of tenants,
/// <c>saas.tenants (id)</c>, ON DELETE RESTRICT, named <see cref="NameFor"/> (<c>fk_&lt;table&gt;_tenant</c>). Applies to every
/// entity type that owns its table and has a <c>TenantId</c> property — tenant-scoped or not, owned types stored in their own
/// table included; owned types sharing their owner's table use the owner's row. A nullable tenant column keeps an optional key.
/// Runs at model finalization after <see cref="TenantKeyConvention"/>: the owners' keys are final then, so no owned type is
/// rebuilt after its key was added. Gate: <c>ConventionTests.Every_tenant_column_references_the_tenant_registry</c>.
/// </summary>
public sealed class TenantForeignKeyConvention : IModelFinalizingConvention
{
    public static string NameFor(string table) => $"fk_{table}_tenant";

    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        var registry = modelBuilder.Metadata.FindEntityType(typeof(Tenant));
        var registryKey = registry?.FindPrimaryKey();
        if (registry is null || registryKey is null)
        {
            return;
        }

        var tables = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in modelBuilder.Metadata.GetEntityTypes().ToList())
        {
            var table = entity.GetTableName();
            if (entity == registry || table is null || entity.FindPrimaryKey() is null)
            {
                continue;
            }

            if (entity.FindOwnership() is { } ownership
                && ownership.PrincipalEntityType.GetTableName() == table
                && ownership.PrincipalEntityType.GetSchema() == entity.GetSchema())
            {
                continue;   // table splitting: the owner's row carries the key
            }

            var tenantId = entity.FindProperty(nameof(ITenantScoped.TenantId));
            if (tenantId is null || (Nullable.GetUnderlyingType(tenantId.ClrType) ?? tenantId.ClrType) != typeof(Guid)
                || !tables.Add($"{entity.GetSchema()}.{table}"))
            {
                continue;
            }

            var foreignKey = entity.FindForeignKey(tenantId, registryKey, registry)
                ?? entity.Builder.HasRelationship(registry, [tenantId], registryKey, fromDataAnnotation: true)?.Metadata
                ?? throw new InvalidOperationException($"The tenant foreign key of {entity.DisplayName()} could not be added.");
            // Data-annotation strength, so the naming convention that runs later (snake case) keeps the name.
            foreignKey.Builder.OnDelete(DeleteBehavior.Restrict, fromDataAnnotation: true);
            foreignKey.Builder.IsRequired(!tenantId.IsNullable, fromDataAnnotation: true);
            foreignKey.SetConstraintName(NameFor(table), fromDataAnnotation: true);
        }
    }
}
