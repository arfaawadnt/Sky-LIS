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

    /// <summary>
    /// Database team review (2026-10-26, comment 5): every identifier is shorter than 62 characters (PostgreSQL truncates at 63;
    /// the team keeps a margin). Owned types count: their table, keys and owner foreign key are real identifiers too.
    /// </summary>
    [Fact]
    public void Every_database_identifier_is_shorter_than_62_characters()
    {
        var identifiers = new List<string>();
        foreach (var entityType in Model.GetEntityTypes())
        {
            identifiers.Add(entityType.GetTableName()!);
            identifiers.AddRange(entityType.GetProperties().Select(property => property.GetColumnName()));
            identifiers.AddRange(entityType.GetKeys().Select(key => key.GetName() ?? string.Empty));
            identifiers.AddRange(entityType.GetIndexes().Select(index => index.GetDatabaseName() ?? string.Empty));
            // an owned type stored in its owner's table (table splitting) has an ownership key that is never created in the database
            identifiers.AddRange(entityType.GetForeignKeys()
                .Where(foreignKey => !(foreignKey.IsOwnership && foreignKey.PrincipalEntityType.GetTableName() == entityType.GetTableName() && foreignKey.PrincipalEntityType.GetSchema() == entityType.GetSchema()))
                .Select(foreignKey => foreignKey.GetConstraintName() ?? string.Empty));
            identifiers.AddRange(entityType.GetCheckConstraints().Select(constraint => constraint.Name ?? string.Empty));
        }

        var tooLong = identifiers.Where(identifier => identifier.Length >= 62).Distinct().ToList();
        Assert.True(tooLong.Count == 0, "Identifiers of 62 characters or more: " + string.Join(", ", tooLong));
    }

    /// <summary>Database team review (2026-10-26, comment 2): a string column of 1000 characters or more is <c>text</c>, never <c>varchar(n)</c>.</summary>
    [Fact]
    public void Long_text_columns_are_text_not_varchar()
    {
        var wrong = Model.GetEntityTypes()
            .SelectMany(type => type.GetProperties().Select(property => (Type: type, Property: property)))
            .Where(entry => entry.Property.ClrType == typeof(string) && entry.Property.GetMaxLength() >= AppDbContext.LongTextThreshold)
            .Where(entry => !string.Equals(entry.Property.GetColumnType(), "text", StringComparison.Ordinal))
            .Select(entry => $"{entry.Type.GetTableName()}.{entry.Property.GetColumnName()} ({entry.Property.GetColumnType()})")
            .ToList();
        Assert.True(wrong.Count == 0, "Long text columns that are not text: " + string.Join(", ", wrong));
    }

    /// <summary>
    /// Database team review (2026-10-26, comment 3): every table of the model with a <c>tenant_id</c> column has the foreign key
    /// <c>fk_&lt;table&gt;_tenant</c> (tenant_id) → <c>saas.tenants (id)</c>, ON DELETE RESTRICT; owned types stored in their own
    /// table count, owned types sharing their owner's table use the owner's. A nullable tenant column keeps an optional key.
    /// </summary>
    [Fact]
    public void Every_tenant_column_references_the_tenant_registry()
    {
        var registry = Model.FindEntityType(typeof(Platform.Domain.PlatformAdmin.Tenant))!;
        var missing = new List<string>();
        var checkedTables = 0;
        foreach (var entityType in Model.GetEntityTypes().Where(type => type != registry && type.GetTableName() is not null))
        {
            var tenantId = entityType.FindProperty(nameof(ITenantScoped.TenantId));
            if (tenantId is null || tenantId.GetColumnName() != "tenant_id"
                || (entityType.FindOwnership() is { } ownership && ownership.PrincipalEntityType.GetTableName() == entityType.GetTableName() && ownership.PrincipalEntityType.GetSchema() == entityType.GetSchema()))
            {
                continue;
            }

            checkedTables++;
            var table = entityType.GetTableName()!;
            var foreignKey = entityType.GetForeignKeys().SingleOrDefault(key => key.PrincipalEntityType == registry && key.Properties.Count == 1 && key.Properties[0] == tenantId);
            if (foreignKey is null)
            {
                missing.Add($"{entityType.GetSchema()}.{table}: no foreign key to saas.tenants");
                continue;
            }

            if (foreignKey.GetConstraintName() != AppDbContext.TenantForeignKeyName(table) || foreignKey.DeleteBehavior != DeleteBehavior.Restrict)
            {
                missing.Add($"{entityType.GetSchema()}.{table}: {foreignKey.GetConstraintName()} ({foreignKey.DeleteBehavior})");
            }

            if (foreignKey.IsRequired == tenantId.IsNullable)
            {
                missing.Add($"{entityType.GetSchema()}.{table}: the key's optionality differs from the column's nullability");
            }
        }

        Assert.True(missing.Count == 0, "Tenant columns without the registry foreign key: " + string.Join("; ", missing));
        Assert.True(checkedTables >= 80, $"expected the tenant tables of the model, checked {checkedTables}");
    }

    /// <summary>
    /// Database team review (2026-10-26, comment 4): a table with a NOT NULL <c>tenant_id</c> and an <c>id</c> is keyed
    /// <c>(tenant_id, id)</c>, whether or not its entity is tenant-scoped (platform-side tables such as wallets, tickets and
    /// machine credentials included). Tables with a nullable tenant keep <c>(id)</c>; natural tenant-first keys
    /// (<c>reference_counters</c>, <c>tenant_settings</c>, <c>audit_trail</c>) have no <c>id</c>.
    /// </summary>
    [Fact]
    public void Every_table_with_a_required_tenant_is_keyed_tenant_then_id()
    {
        var wrong = new List<string>();
        foreach (var entityType in Model.GetEntityTypes().Where(type => type.GetTableName() is not null && type.FindPrimaryKey() is not null))
        {
            var tenantId = entityType.FindProperty(nameof(ITenantScoped.TenantId));
            var id = entityType.FindProperty("Id");
            if (tenantId is null || tenantId.IsNullable || id is null || entityType.ClrType == typeof(Platform.Domain.PlatformAdmin.Tenant)
                || (entityType.FindOwnership() is { } ownership && ownership.PrincipalEntityType.GetTableName() == entityType.GetTableName() && ownership.PrincipalEntityType.GetSchema() == entityType.GetSchema()))
            {
                continue;
            }

            var key = entityType.FindPrimaryKey()!.Properties.Select(property => property.GetColumnName()).ToArray();
            if (!key.SequenceEqual(["tenant_id", "id"]))
            {
                wrong.Add($"{entityType.GetSchema()}.{entityType.GetTableName()} ({string.Join(", ", key)})");
            }
        }

        Assert.True(wrong.Count == 0, "Tables with a tenant whose key is not (tenant_id, id): " + string.Join("; ", wrong));
    }

    /// <summary>
    /// Database team review (2026-10-26, comments 3–4): saas.tenants holds the platform tenant's row (kind Platform) as well as
    /// the laboratories; the model filters it on kind Laboratory, so no reader of the model ever returns the platform row.
    /// </summary>
    [Fact]
    public void The_tenant_registry_is_read_as_laboratories_only()
    {
        var registry = Model.FindEntityType(typeof(Platform.Domain.PlatformAdmin.Tenant))!;
        var filter = registry.GetQueryFilter();
        Assert.NotNull(filter);
        var admits = (Func<Platform.Domain.PlatformAdmin.Tenant, bool>)filter.Compile();
        Assert.True(admits(TenantOfKind(Platform.Domain.PlatformAdmin.TenantKind.Laboratory)));
        Assert.False(admits(TenantOfKind(Platform.Domain.PlatformAdmin.TenantKind.Platform)));
        Assert.Equal(ValueGenerated.Never, registry.FindPrimaryKey()!.Properties.Single().ValueGenerated);   // a generated key would be propagated into unset tenant ids
        Assert.Contains(registry.GetCheckConstraints(), constraint => constraint.Name == "ck_tenants_kind_domain");
    }

    private static Platform.Domain.PlatformAdmin.Tenant TenantOfKind(Platform.Domain.PlatformAdmin.TenantKind kind)
    {
        var tenant = (Platform.Domain.PlatformAdmin.Tenant)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Platform.Domain.PlatformAdmin.Tenant));
        typeof(Platform.Domain.PlatformAdmin.Tenant).GetProperty(nameof(Platform.Domain.PlatformAdmin.Tenant.Kind))!.SetValue(tenant, kind);
        return tenant;
    }

    /// <summary>Database team review (2026-10-26, comment 1): a column that holds an IP address is <c>inet</c>.</summary>
    [Fact]
    public void Ip_address_columns_are_inet()
    {
        var ipColumns = Model.GetEntityTypes()
            .SelectMany(type => type.GetProperties().Select(property => (Type: type, Property: property)))
            .Where(entry => System.Text.RegularExpressions.Regex.IsMatch(entry.Property.GetColumnName(), @"(^|_)ip(_|$)|ip_address"))
            .ToList();
        Assert.NotEmpty(ipColumns);
        foreach (var (type, property) in ipColumns)
        {
            Assert.True(string.Equals(property.GetColumnType(), "inet", StringComparison.Ordinal), $"{type.GetTableName()}.{property.GetColumnName()} is {property.GetColumnType()}, not inet");
        }
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
