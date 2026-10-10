using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Platform.ArchitectureTests.Support;
using Platform.Domain.Identity;
using Platform.Domain.Tenancy;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Persistence.Conventions;
using Platform.Infrastructure.Persistence.Outbox;
using Platform.SharedKernel.MultiTenancy;

namespace Platform.ArchitectureTests;

/// <summary>
/// The new-tenant-table checklist of 01 §4.2, asserted on the built model: tenant-first composite keys, the tenant
/// query filter, the xmin concurrency token, tenant-carrying uniqueness and foreign keys, enum domain constraints,
/// identifier length. Pitfalls P-06, P-07, P-09, P-31.
/// </summary>
public sealed class ConventionTests
{
    private static readonly IModel Model = OfflineModel.Build();

    private static IEnumerable<IEntityType> TenantTypes => Model.GetEntityTypes().Where(type => !type.IsOwned() && typeof(ITenantScoped).IsAssignableFrom(type.ClrType));

    private static IEnumerable<IEntityType> TableTypes => Model.GetEntityTypes().Where(type => !type.IsOwned());

    [Fact]
    public void The_model_contains_the_identity_split_and_the_outbox()
    {
        Assert.Equal(("identity", "platform_users"), TableOf<PlatformUser>());
        Assert.Equal(("identity", "refresh_sessions"), TableOf<RefreshSession>());
        Assert.Equal(("app", "tenant_memberships"), TableOf<TenantMembership>());
        Assert.Equal(("ops", "outbox_events"), TableOf<OutboxEvent>());
        Assert.NotEmpty(TenantTypes);
    }

    [Fact]
    public void Every_tenant_scoped_entity_is_keyed_tenant_first()
    {
        foreach (var entityType in TenantTypes)
        {
            var key = entityType.FindPrimaryKey();
            Assert.NotNull(key);
            Assert.Equal(nameof(ITenantScoped.TenantId), key.Properties[0].Name);
            Assert.Equal("tenant_id", key.Properties[0].GetColumnName());
            if (entityType.FindProperty("Id") is not null)
            {
                Assert.Equal([nameof(ITenantScoped.TenantId), "Id"], key.Properties.Select(property => property.Name).ToArray());
                Assert.Equal(ValueGenerated.Never, key.Properties[1].ValueGenerated);
            }
        }
    }

    [Fact]
    public void Every_tenant_scoped_entity_has_a_tenant_query_filter()
    {
        foreach (var entityType in TenantTypes)
        {
            var filter = entityType.GetQueryFilter();
            Assert.NotNull(filter);
            Assert.Contains(nameof(AppDbContext.CurrentTenantIdOrNil), filter.ToString(), StringComparison.Ordinal);
            Assert.Contains(nameof(AppDbContext.IsElevated), filter.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Non_tenant_identity_tables_have_no_tenant_filter_and_a_single_column_key()
    {
        foreach (var entityType in new[] { Model.FindEntityType(typeof(PlatformUser))!, Model.FindEntityType(typeof(RefreshSession))! })
        {
            Assert.Null(entityType.GetQueryFilter());
            Assert.Equal(["id"], entityType.FindPrimaryKey()!.Properties.Select(property => property.GetColumnName()).ToArray());
        }
    }

    [Fact]
    public void Every_aggregate_table_maps_its_row_version_onto_xmin()
    {
        var withRowVersion = TableTypes.Where(type => type.FindProperty(XminConcurrencyConvention.PropertyName) is not null).ToList();
        Assert.NotEmpty(withRowVersion);

        foreach (var entityType in withRowVersion)
        {
            var property = entityType.FindProperty(XminConcurrencyConvention.PropertyName)!;
            Assert.True(property.IsConcurrencyToken, $"{entityType.ClrType.Name}.RowVersion is not a concurrency token");
            Assert.Equal(ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
            Assert.Equal(XminConcurrencyConvention.ColumnName, property.GetColumnName());
            Assert.Equal("xid", property.GetColumnType());
        }
    }

    [Fact]
    public void No_tenant_table_declares_a_single_column_unique_constraint_on_id()
    {
        foreach (var entityType in TenantTypes)
        {
            var offenders = entityType.GetKeys().Where(key => !key.IsPrimaryKey() && key.Properties.Count == 1 && key.Properties[0].Name == "Id")
                .Select(key => key.GetName() ?? "alternate key")
                .Concat(entityType.GetIndexes().Where(index => index.IsUnique && index.Properties.Count == 1 && index.Properties[0].Name == "Id").Select(index => index.GetDatabaseName()))
                .ToList();
            Assert.True(offenders.Count == 0, $"{entityType.ClrType.Name} has a single-column key on id: {string.Join(", ", offenders)}");
        }
    }

    [Fact]
    public void Every_unique_index_on_a_tenant_table_includes_the_tenant()
    {
        foreach (var entityType in TenantTypes)
        {
            foreach (var index in entityType.GetIndexes().Where(index => index.IsUnique))
            {
                Assert.Contains(index.Properties, property => property.Name == nameof(ITenantScoped.TenantId));
            }
        }
    }

    [Fact]
    public void Every_foreign_key_between_tenant_tables_carries_the_tenant()
    {
        foreach (var entityType in TenantTypes)
        {
            foreach (var foreignKey in entityType.GetForeignKeys().Where(fk => typeof(ITenantScoped).IsAssignableFrom(fk.PrincipalEntityType.ClrType)))
            {
                Assert.Contains(foreignKey.Properties, property => property.Name == nameof(ITenantScoped.TenantId));
            }
        }
    }

    [Fact]
    public void Every_enum_column_is_a_string_with_a_domain_check_constraint()
    {
        var checked_ = 0;
        foreach (var entityType in TableTypes)
        {
            var constraints = entityType.GetCheckConstraints().ToDictionary(constraint => constraint.Name ?? string.Empty, constraint => constraint.Sql, StringComparer.Ordinal);
            foreach (var property in entityType.GetProperties())
            {
                var enumType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (!enumType.IsEnum)
                {
                    continue;
                }

                checked_++;
                Assert.Equal($"character varying({AppDbContext.EnumColumnLength})", property.GetColumnType());
                var name = $"ck_{entityType.GetTableName()}_{property.GetColumnName()}_domain";
                Assert.True(constraints.TryGetValue(name, out var sql), $"Missing check constraint {name}");
                Assert.All(Enum.GetNames(enumType), member => Assert.Contains($"'{member}'", sql, StringComparison.Ordinal));
            }
        }

        Assert.True(checked_ >= 3, "expected the session enums to be checked");
    }

    [Fact]
    public void No_database_identifier_exceeds_62_characters()
    {
        var identifiers = new List<string>();
        foreach (var entityType in TableTypes)
        {
            identifiers.Add(entityType.GetTableName()!);
            identifiers.AddRange(entityType.GetProperties().Select(property => property.GetColumnName()));
            identifiers.AddRange(entityType.GetKeys().Select(key => key.GetName() ?? string.Empty));
            identifiers.AddRange(entityType.GetIndexes().Select(index => index.GetDatabaseName() ?? string.Empty));
            identifiers.AddRange(entityType.GetForeignKeys().Select(foreignKey => foreignKey.GetConstraintName() ?? string.Empty));
            identifiers.AddRange(entityType.GetCheckConstraints().Select(constraint => constraint.Name ?? string.Empty));
        }

        var tooLong = identifiers.Where(identifier => identifier.Length > 62).ToList();
        Assert.True(tooLong.Count == 0, "Identifiers PostgreSQL would truncate: " + string.Join(", ", tooLong));
    }

    [Fact]
    public void Owned_bilingual_text_is_split_into_two_columns_on_the_owner_table()
    {
        var membership = Model.FindEntityType(typeof(TenantMembership))!;
        var owned = membership.GetNavigations().Single(navigation => navigation.Name == nameof(TenantMembership.FullName)).TargetEntityType;

        Assert.True(owned.IsOwned());
        Assert.Equal(membership.GetTableName(), owned.GetTableName());
        Assert.Equal(["full_name_ar", "full_name_en"], owned.GetProperties().Where(property => !property.IsKey()).Select(property => property.GetColumnName()).OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    private static (string Schema, string Table) TableOf<TEntity>()
    {
        var entityType = Model.FindEntityType(typeof(TEntity))!;
        return (entityType.GetSchema() ?? string.Empty, entityType.GetTableName() ?? string.Empty);
    }
}
