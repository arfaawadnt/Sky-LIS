using Microsoft.EntityFrameworkCore;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Organization;
using Platform.Contracts.Common;
using Platform.Contracts.Organization;
using Platform.Domain.Organization;

namespace Platform.Infrastructure.Persistence.Organization;

/// <summary>Search helpers of the module's registers.</summary>
internal static class OrganizationReads
{
    /// <summary>An <c>ILIKE</c> pattern matching <paramref name="search"/> anywhere, with the wildcards of the input escaped.</summary>
    /// <summary>The escape character of <see cref="ContainsPattern"/>; passed to every <c>ILike</c> (Npgsql sends <c>ESCAPE ''</c> without it, so an escaped "_" or "%" would never match).</summary>
    public const string Escape = "\\";

    public static string ContainsPattern(string search)
    {
        var escaped = search.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
        return "%" + escaped + "%";
    }

    public static SpecialRightsDto ToDto(SpecialRights rights) =>
        new(rights.DiscountCapPercent, rights.CanPay, rights.CanRefund, rights.CanTransfer, rights.CanCancel, rights.CanModifyVerifiedResult, rights.CanPreviewWithoutVerify, rights.CanPrintInvoice, rights.CanSeeVip);

    public static ResultRightDto ToDto(ResultRight right) =>
        new(right.ClinicalGroupId, right.BranchId, right.CanSee, right.CanReview, right.CanVerify, right.CanEdit, right.CanPrint);
}

internal sealed class RoleRepository(AppDbContext context) : Repository<Role>(context), IRoleRepository
{
    private static readonly SortColumns<Role> Sorts = new SortColumns<Role>(OrganizationSortKeys.Roles)
        .Column("nameEn", role => role.Name.En)
        .Column("nameAr", role => role.Name.Ar)
        .ThenBy(role => role.Id);

    public Task<Role?> FindByNameAsync(string nameEn, CancellationToken cancellationToken)
    {
        var trimmed = (nameEn ?? string.Empty).Trim();
        return Set.FirstOrDefaultAsync(role => role.Name.En == trimmed, cancellationToken);
    }

    public Task<bool> NameExistsAsync(string nameEn, Guid? excludingRoleId, CancellationToken cancellationToken)
    {
        var lowered = (nameEn ?? string.Empty).Trim().ToLowerInvariant();
        return Set.AnyAsync(role => role.Name.En.ToLower() == lowered && (excludingRoleId == null || role.Id != excludingRoleId), cancellationToken);
    }

    public async Task<Paged<RoleListItem>> ListAsync(RoleFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking();
        if (filter.IsActive is { } active)
        {
            query = query.Where(role => role.IsActive == active);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = OrganizationReads.ContainsPattern(filter.Search);
            query = query.Where(role => EF.Functions.ILike(role.Name.En, pattern, OrganizationReads.Escape) || EF.Functions.ILike(role.Name.Ar, pattern, OrganizationReads.Escape));
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var items = await Sorts.Apply(query, filter.Sort, rows => rows.OrderBy(role => role.Name.En).ThenBy(role => role.Id))
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(role => new RoleListItem(
                role.Id,
                role.Name.En,
                role.Name.Ar,
                role.Description,
                role.PermissionKeys.Length,
                Context.TenantMemberships.Count(membership => membership.RoleId == role.Id),
                role.IsActive))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new Paged<RoleListItem>(items, page.Page, page.PageSize, total);
    }

    public async Task<IReadOnlyList<Role>> ListActiveForUpdateAsync(CancellationToken cancellationToken) =>
        await Set.Where(role => role.IsActive).OrderBy(role => role.Id).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<RoleDetail?> GetDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var role = await Set.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken).ConfigureAwait(false);
        if (role is null)
        {
            return null;
        }

        var members = await Context.TenantMemberships.CountAsync(membership => membership.RoleId == id, cancellationToken).ConfigureAwait(false);
        var holdsPrimary = await Context.TenantMemberships.AnyAsync(membership => membership.RoleId == id && membership.IsPrimaryAdministrator, cancellationToken).ConfigureAwait(false);
        return new RoleDetail(
            role.Id,
            role.Name.En,
            role.Name.Ar,
            role.Description,
            role.IsActive,
            role.PermissionKeys,
            OrganizationReads.ToDto(role.SpecialRights),
            role.ResultRightValues().Select(OrganizationReads.ToDto).ToList(),
            members,
            holdsPrimary);
    }
}

internal sealed class BranchRepository(AppDbContext context) : Repository<Branch>(context), IBranchRepository
{
    private static readonly SortColumns<Branch> Sorts = new SortColumns<Branch>(OrganizationSortKeys.Branches)
        .Column("code", branch => branch.Code)
        .Column("nameEn", branch => branch.Name.En)
        .Column("nameAr", branch => branch.Name.Ar)
        .ThenBy(branch => branch.Code);

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken)
    {
        var cleaned = (code ?? string.Empty).Trim().ToUpperInvariant();
        return Set.AnyAsync(branch => branch.Code == cleaned, cancellationToken);
    }

    public Task<Branch?> FindByCodeAsync(string code, CancellationToken cancellationToken)
    {
        var cleaned = (code ?? string.Empty).Trim().ToUpperInvariant();
        return Set.FirstOrDefaultAsync(branch => branch.Code == cleaned, cancellationToken);
    }

    public Task<int> CountActiveAsync(CancellationToken cancellationToken) => Set.CountAsync(branch => branch.IsActive, cancellationToken);

    public async Task<Paged<BranchListItem>> ListAsync(BranchFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking();
        if (filter.IsActive is { } active)
        {
            query = query.Where(branch => branch.IsActive == active);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = OrganizationReads.ContainsPattern(filter.Search);
            query = query.Where(branch => EF.Functions.ILike(branch.Code, pattern, OrganizationReads.Escape) || EF.Functions.ILike(branch.Name.En, pattern, OrganizationReads.Escape) || EF.Functions.ILike(branch.Name.Ar, pattern, OrganizationReads.Escape));
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var items = await Sorts.Apply(query, filter.Sort, rows => rows.OrderBy(branch => branch.Code))
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(branch => new BranchListItem(branch.Id, branch.Code, branch.Name.En, branch.Name.Ar, branch.CountryId, branch.GovernorateId, branch.Phone, branch.Email, branch.Currency, branch.IsActive, branch.CityId, branch.DistrictId, branch.AreaId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new Paged<BranchListItem>(items, page.Page, page.PageSize, total);
    }

    public Task<BranchListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken) =>
        Set.AsNoTracking()
            .Where(branch => branch.Id == id)
            .Select(branch => new BranchListItem(branch.Id, branch.Code, branch.Name.En, branch.Name.Ar, branch.CountryId, branch.GovernorateId, branch.Phone, branch.Email, branch.Currency, branch.IsActive, branch.CityId, branch.DistrictId, branch.AreaId))
            .FirstOrDefaultAsync(cancellationToken);
}
