using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Infrastructure.Persistence.Conventions;

/// <summary>
/// Every <see cref="ITenantScoped"/> entity is keyed <c>(tenant_id, id)</c>, tenant first (01 §4.2 item 2, C-09);
/// owned children stored in their own table carry a shadow <c>TenantId</c> and the same key shape (pitfall P-31).
/// Owned types that share their owner's table reuse the owner's key and are left alone.
/// </summary>
public sealed class TenantKeyConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        foreach (var entity in modelBuilder.Metadata.GetEntityTypes().ToList())
        {
            if (entity.IsOwned())
            {
                var ownership = entity.FindOwnership();
                if (ownership is null || !typeof(ITenantScoped).IsAssignableFrom(ownership.PrincipalEntityType.ClrType))
                {
                    continue;
                }

                var owner = ownership.PrincipalEntityType;
                if (entity.GetTableName() == owner.GetTableName() && entity.GetSchema() == owner.GetSchema())
                {
                    continue; // table splitting: the owned type shares the owner's (tenant_id, id)
                }

                var shadowTenant = entity.Builder.Property(typeof(Guid), nameof(ITenantScoped.TenantId));
                var childId = entity.FindProperty(nameof(Entity.Id));
                if (shadowTenant is not null && childId is not null)
                {
                    entity.Builder.PrimaryKey([shadowTenant.Metadata, childId]);
                }

                continue;
            }

            if (!typeof(ITenantScoped).IsAssignableFrom(entity.ClrType))
            {
                continue;
            }

            var tenantId = entity.FindProperty(nameof(ITenantScoped.TenantId));
            var id = entity.FindProperty(nameof(Entity.Id));
            if (tenantId is null || id is null)
            {
                continue;
            }

            entity.Builder.PrimaryKey([tenantId, id]);
            id.Builder.ValueGenerated(ValueGenerated.Never);
        }
    }
}
