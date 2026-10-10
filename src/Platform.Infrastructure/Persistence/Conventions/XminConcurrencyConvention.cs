using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Platform.Infrastructure.Persistence.Conventions;

/// <summary>
/// Optimistic concurrency without a physical column (01 §4.2 item 9): a <c>uint RowVersion</c> property on an
/// entity is mapped onto PostgreSQL's <c>xmin</c> system column (type <c>xid</c>), read back after every write
/// and compared on every update. Migrations never create it; the provider knows it is a system column.
/// </summary>
public sealed class XminConcurrencyConvention : IModelFinalizingConvention
{
    public const string PropertyName = "RowVersion";
    public const string ColumnName = "xmin";

    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        foreach (var entity in modelBuilder.Metadata.GetEntityTypes())
        {
            if (entity.IsOwned())
            {
                continue;
            }

            var property = entity.FindProperty(PropertyName);
            if (property is null || property.ClrType != typeof(uint))
            {
                continue;
            }

            property.Builder.HasColumnName(ColumnName);
            property.Builder.HasColumnType("xid");
            property.Builder.ValueGenerated(ValueGenerated.OnAddOrUpdate);
            property.Builder.IsConcurrencyToken(true);
        }
    }
}
