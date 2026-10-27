using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Platform.Domain.PlatformAdmin;
using Platform.Infrastructure.Persistence.PlatformAdmin;
using Platform.SharedKernel.MultiTenancy;

namespace Platform.IntegrationTests.Support;

/// <summary>
/// Database team review 2026-10-26 (comments 3–4, migration <c>DbReviewTenantKeys</c>): every <c>tenant_id</c> references the
/// registry <c>saas.tenants</c>, so a tenant must be registered before any row is written for it. The tests use arbitrary tenant
/// ids; <see cref="EnsureLaboratory"/> registers one idempotently as a minimal, active laboratory on placeholder reference data
/// (one inactive country, governorate and plan with fixed ids, created once after each reset) and the platform tenant through
/// <see cref="PlatformTenantRecord.EnsureSql"/>. <see cref="DatabaseFixture.AppScope"/> calls it for the scope's tenant, so a test
/// that writes through a tenant scope needs nothing more; a test that inserts by SQL for another tenant calls it first.
/// The placeholder country has no time zone, so the laboratory's day still follows the tenant parameter <c>timeZone</c> (then the
/// platform default), as it did before every tenant had a record.
/// </summary>
internal static class TestTenants
{
    public static readonly Guid CountryId = new("0199d000-0000-7000-8000-0000000ff001");
    public static readonly Guid GovernorateId = new("0199d000-0000-7000-8000-0000000ff002");
    public static readonly Guid PlanId = new("0199d000-0000-7000-8000-0000000ff003");

    /// <summary>Reserved in <c>TestCountryCodes</c>, so no test creates it by hand.</summary>
    public const string CountryCode = "ZT";

    public const string LaboratoryNamePrefix = "Test laboratory ";

    /// <summary>The placeholder country, governorate and plan (idempotent); valid on every schema since the saas tables exist.</summary>
    public const string ReferenceDataSql = """
        INSERT INTO saas.countries (id, name_en, name_ar, iso_code, dial_code, currency, currency_code, language, time_zone, mobile_length, is_active, created_at_utc, created_by_display_name)
        VALUES (@country, 'Test registry country', 'دولة سجل الاختبار', 'ZT', '+20', 'Pound', 'EGP', 'en', '', 10, false, now(), 'tests')
        ON CONFLICT DO NOTHING;
        INSERT INTO saas.governorates (id, country_id, name_en, name_ar, is_active, created_at_utc, created_by_display_name)
        VALUES (@governorate, @country, 'Test registry governorate', 'محافظة سجل الاختبار', false, now(), 'tests')
        ON CONFLICT DO NOTHING;
        INSERT INTO saas.plans (id, code, name_en, name_ar, monthly_fee, currency_code, max_branches, max_users, sms_quota, max_analysers, is_active, created_at_utc, created_by_display_name)
        VALUES (@plan, 'TESTREGISTRY', 'Test registry plan', 'باقة سجل الاختبار', 1, 'EGP', 1, 1, 0, 0, false, now(), 'tests')
        ON CONFLICT DO NOTHING;
        """;

    private const string EnsureLaboratorySql = ReferenceDataSql + """

        INSERT INTO saas.tenants (id, kind, name_en, name_ar, country_id, governorate_id, plan_id, address, mobile, manager_name, manager_mobile,
                                  portal_slug, source, status, activated_at_utc, preferred_language, created_at_utc, created_by_display_name)
        VALUES (@tenant, 'Laboratory', @name, @name, @country, @governorate, @plan, 'Test address', '+201000000000', 'Test manager', '+201000000000',
                @slug, 'Manual', 'Active', now(), 'en', now(), 'tests')
        ON CONFLICT (id) DO NOTHING;
        """;

    // Tenants registered since the last reset (DatabaseFixture.TruncateAsync clears it): saves a round trip per write.
    private static readonly ConcurrentDictionary<Guid, byte> Registered = new();

    /// <summary>Forgets what was registered: the tables were emptied (or dropped and created again).</summary>
    public static void Forget() => Registered.Clear();

    /// <summary>Registers <paramref name="tenantId"/> (a laboratory, or the platform row for <see cref="TenantIds.Platform"/>); a no-op when it is registered.</summary>
    public static void EnsureLaboratory(Guid tenantId)
    {
        if (Registered.ContainsKey(tenantId))
        {
            return;
        }

        using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        connection.Open();
        using var command = Command(connection, tenantId);
        command.ExecuteNonQuery();
        Registered.TryAdd(tenantId, 0);
    }

    public static async Task EnsureLaboratoryAsync(Guid tenantId)
    {
        if (Registered.ContainsKey(tenantId))
        {
            return;
        }

        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();
        await using var command = Command(connection, tenantId);
        await command.ExecuteNonQueryAsync();
        Registered.TryAdd(tenantId, 0);
    }

    /// <summary>Writes only the placeholder reference data (a migration test on an older schema registers its tenants through the migration).</summary>
    public static async Task EnsureReferenceDataAsync()
    {
        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(ReferenceDataSql, connection);
        command.Parameters.AddWithValue("country", CountryId);
        command.Parameters.AddWithValue("governorate", GovernorateId);
        command.Parameters.AddWithValue("plan", PlanId);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>The address <see cref="EnsureLaboratory"/> gives a tenant: <c>t</c> + the tenant id (33 characters).</summary>
    public static string SlugOf(Guid tenantId) => "t" + tenantId.ToString("N");

    private static NpgsqlCommand Command(NpgsqlConnection connection, Guid tenantId)
    {
        if (TenantIds.IsPlatform(tenantId))
        {
            return new NpgsqlCommand(PlatformTenantRecord.EnsureSql, connection);
        }

        var command = new NpgsqlCommand(EnsureLaboratorySql, connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("country", CountryId);
        command.Parameters.AddWithValue("governorate", GovernorateId);
        command.Parameters.AddWithValue("plan", PlanId);
        command.Parameters.AddWithValue("name", LaboratoryNamePrefix + tenantId.ToString("N")[..8]);
        command.Parameters.AddWithValue("slug", SlugOf(tenantId));
        return command;
    }
}

/// <summary>
/// Registers, before a save, every tenant the added rows belong to (the tenant stamp has run: this interceptor is added after the
/// infrastructure's), except the tenants the same save creates (a provisioning flush adds the <see cref="Tenant"/> with its rows).
/// Covers the writes of a scope for another tenant (elevated or <c>ForTenant</c>); the scope's own tenant is registered by
/// <see cref="DatabaseFixture.AppScope"/>.
/// </summary>
internal sealed class TestTenantRegistrationInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        foreach (var tenantId in TenantsToRegister(eventData.Context))
        {
            TestTenants.EnsureLaboratory(tenantId);
        }

        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        foreach (var tenantId in TenantsToRegister(eventData.Context))
        {
            await TestTenants.EnsureLaboratoryAsync(tenantId);
        }

        return result;
    }

    private static List<Guid> TenantsToRegister(DbContext? context)
    {
        if (context is null)
        {
            return [];
        }

        var created = new HashSet<Guid>();
        var used = new HashSet<Guid>();
        foreach (var entry in context.ChangeTracker.Entries().Where(entry => entry.State == EntityState.Added))
        {
            if (entry.Entity is Tenant tenant)
            {
                created.Add(tenant.Id);
            }
            else if (entry.Metadata.FindProperty(nameof(ITenantScoped.TenantId)) is { } property && entry.Property(property.Name).CurrentValue is Guid id && id != Guid.Empty)
            {
                used.Add(id);
            }
        }

        used.ExceptWith(created);
        return [.. used];
    }
}
