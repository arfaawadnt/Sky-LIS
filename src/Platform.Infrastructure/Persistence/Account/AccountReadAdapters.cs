using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Application.Account;
using Platform.Application.PlatformAdmin;
using Platform.Contracts.Account;
using Platform.Domain.Identity;
using Platform.Domain.Organization;
using Platform.Domain.PlatformAdmin;
using Platform.Domain.Tenancy;

namespace Platform.Infrastructure.Persistence.Account;

// Read ports of the Account & onboarding module over tables owned by earlier modules (identity, tenancy, organization:
// foundation and wave 1; the platform's saas tables: wave 1). Registered by convention (MODULE-PATTERN §2).

/// <summary>
/// CP-65 / SA-21 and the CP-68 user filter: the actor's own user row and their membership of the current tenant (the
/// tenant query filter and RLS bound the membership and the role), and the tenant's members.
/// </summary>
internal sealed class AccountDirectory(AppDbContext context) : IAccountDirectory
{
    public async Task<OwnAccount?> GetOwnAsync(Guid userId, CancellationToken cancellationToken)
    {
        var account = await context.Set<PlatformUser>().AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new { user.Id, user.Username, user.DisplayName, user.Email, user.Mobile, user.MustChangePassword, user.PasswordChangedAtUtc, user.PasswordExpiresAtUtc })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (account is null)
        {
            return null;
        }

        var membership = await (
            from member in context.Set<TenantMembership>().AsNoTracking().Where(member => member.PlatformUserId == userId)
            from role in context.Set<Role>().AsNoTracking().Where(role => role.Id == member.RoleId).DefaultIfEmpty()
            select new
            {
                FullNameEn = member.FullName.En,
                FullNameAr = member.FullName.Ar,
                member.EmployeeCode,
                member.Designation,
                member.PreferredLanguage,
                RoleNameEn = role == null ? null : role.Name.En,
                RoleNameAr = role == null ? null : role.Name.Ar,
            }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return new OwnAccount(
            account.Id,
            account.Username,
            account.DisplayName,
            account.Email,
            account.Mobile,
            account.MustChangePassword,
            account.PasswordChangedAtUtc,
            account.PasswordExpiresAtUtc,
            membership?.FullNameEn,
            membership?.FullNameAr,
            membership?.EmployeeCode,
            membership?.Designation,
            membership?.PreferredLanguage,
            membership?.RoleNameEn,
            membership?.RoleNameAr);
    }

    public async Task<IReadOnlyList<AuditActorItem>> ListMembersAsync(CancellationToken cancellationToken) =>
        await (
            from member in context.Set<TenantMembership>().AsNoTracking()
            join user in context.Set<PlatformUser>().AsNoTracking() on member.PlatformUserId equals user.Id
            orderby user.DisplayName, user.Username
            select new AuditActorItem(user.Id, user.DisplayName, user.Username, member.IsActive && user.IsActive))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}

/// <summary>
/// The platform's client record of a laboratory (<c>saas.tenants</c> with its country and plan, wave 1) for CP-65 and
/// the ticket snapshot. <see langword="null"/> when the tenant has no client record, or while the platform tables are
/// not migrated in this database (PostgreSQL 42P01), so a profile page never fails on a missing platform table.
/// </summary>
internal sealed class LaboratoryProfileLookup(AppDbContext context, IPortalAddressOptions portal) : ILaboratoryProfileLookup
{
    public async Task<LaboratoryProfile?> FindAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        try
        {
            var row = await (
                from tenant in context.Set<Tenant>().AsNoTracking().Where(tenant => tenant.Id == tenantId)
                from country in context.Set<Country>().AsNoTracking().Where(country => country.Id == tenant.CountryId).DefaultIfEmpty()
                from plan in context.Set<Plan>().AsNoTracking().Where(plan => plan.Id == tenant.PlanId).DefaultIfEmpty()
                select new
                {
                    NameEn = tenant.Name.En,
                    NameAr = tenant.Name.Ar,
                    tenant.PortalSlug,
                    CountryEn = country == null ? null : country.Name.En,
                    CountryAr = country == null ? null : country.Name.Ar,
                    PlanCode = plan == null ? null : plan.Code,
                    PlanEn = plan == null ? null : plan.Name.En,
                    PlanAr = plan == null ? null : plan.Name.Ar,
                    tenant.ManagerName,
                    tenant.ManagerMobile,
                    tenant.Mobile,
                    tenant.Email,
                    tenant.Status,
                    tenant.TrialEndsAtUtc,
                }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            return row is null
                ? null
                : new LaboratoryProfile(
                    tenantId,
                    row.NameEn,
                    row.NameAr,
                    Tenant.BuildPortalUrl(row.PortalSlug, portal.PortalDomain),
                    row.CountryEn,
                    row.CountryAr,
                    row.PlanCode,
                    row.PlanEn,
                    row.PlanAr,
                    row.ManagerName,
                    row.ManagerMobile,
                    row.Mobile,
                    row.Email,
                    row.Status.ToString(),
                    row.TrialEndsAtUtc);
        }
        catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn)
        {
            return null;
        }
    }
}
