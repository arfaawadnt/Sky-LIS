using Microsoft.EntityFrameworkCore;
using Platform.Application.PlatformAdmin;
using Platform.Contracts.Common;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.PlatformAdmin;

namespace Platform.Infrastructure.Persistence.PlatformAdmin;

// Repositories of the platform tables. They load and stage aggregates and project registers; they never save and
// never decide (pitfalls P-02, P-13). The saas tables have no tenant: the handlers gate every admin read (PLT-403).

/// <summary>ILIKE patterns with the wildcards of the user's text escaped.</summary>
internal static class Like
{
    public const string Escape = "\\";

    public static string Contains(string text) =>
        "%" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
}

internal sealed class TenantRepository(AppDbContext context) : Repository<Tenant>(context), ITenantRepository
{
    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken)
    {
        var value = (slug ?? string.Empty).Trim().ToLowerInvariant();
        return Set.AnyAsync(tenant => tenant.PortalSlug == value, cancellationToken);
    }

    public async Task<Paged<ClientListItem>> ListAsync(ClientFilter filter, PageRequest page, string portalDomain, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking();
        if (filter.Search is { Length: > 0 } search)
        {
            var pattern = Like.Contains(search);
            query = query.Where(tenant =>
                EF.Functions.ILike(tenant.Name.En, pattern, Like.Escape)
                || EF.Functions.ILike(tenant.Name.Ar, pattern, Like.Escape)
                || EF.Functions.ILike(tenant.PortalSlug, pattern, Like.Escape)
                || EF.Functions.ILike(tenant.ManagerName, pattern, Like.Escape)
                || EF.Functions.ILike(tenant.Mobile, pattern, Like.Escape)
                || EF.Functions.ILike(tenant.ManagerMobile, pattern, Like.Escape));
        }

        if (filter.Source is { } source)
        {
            query = query.Where(tenant => tenant.Source == source);
        }

        if (filter.PlanId is { } planId)
        {
            query = query.Where(tenant => tenant.PlanId == planId);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(tenant => tenant.Status == status);
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await (
            from tenant in query.OrderByDescending(tenant => tenant.CreatedAtUtc).ThenBy(tenant => tenant.Id).Skip(page.Skip).Take(page.PageSize)
            join country in Context.Set<Country>().AsNoTracking() on tenant.CountryId equals country.Id
            join governorate in Context.Set<Governorate>().AsNoTracking() on tenant.GovernorateId equals governorate.Id
            join plan in Context.Set<Plan>().AsNoTracking() on tenant.PlanId equals plan.Id
            from wallet in Context.Set<Wallet>().AsNoTracking().Where(wallet => wallet.TenantId == tenant.Id).DefaultIfEmpty()
            select new
            {
                tenant.Id,
                NameEn = tenant.Name.En,
                NameAr = tenant.Name.Ar,
                tenant.PortalSlug,
                tenant.CountryId,
                CountryEn = country.Name.En,
                CountryAr = country.Name.Ar,
                tenant.GovernorateId,
                GovernorateEn = governorate.Name.En,
                GovernorateAr = governorate.Name.Ar,
                tenant.ManagerName,
                tenant.ManagerMobile,
                tenant.PlanId,
                PlanCode = plan.Code,
                PlanEn = plan.Name.En,
                PlanAr = plan.Name.Ar,
                Balance = wallet == null ? 0m : wallet.Balance,
                Currency = wallet == null ? country.CurrencyCode : wallet.CurrencyCode,
                tenant.CreatedAtUtc,
                tenant.Source,
                tenant.Status,
                tenant.TrialEndsAtUtc,
            }).ToListAsync(cancellationToken).ConfigureAwait(false);

        var items = rows
            .OrderByDescending(row => row.CreatedAtUtc).ThenBy(row => row.Id)
            .Select(row => new ClientListItem(
                row.Id, row.NameEn, row.NameAr, row.PortalSlug, Tenant.BuildPortalUrl(row.PortalSlug, portalDomain), row.CountryId, row.CountryEn, row.CountryAr,
                row.GovernorateId, row.GovernorateEn, row.GovernorateAr, row.ManagerName, row.ManagerMobile, row.PlanId, row.PlanCode, row.PlanEn, row.PlanAr,
                row.Balance, row.Currency.Trim(), 0, 0, row.CreatedAtUtc, row.Source.ToString(), row.Status.ToString(), row.TrialEndsAtUtc))
            .ToList();
        return new Paged<ClientListItem>(items, page.Page, page.PageSize, total);
    }

    public async Task<ClientDetail?> GetDetailAsync(Guid id, string portalDomain, CancellationToken cancellationToken)
    {
        var row = await (
            from tenant in Set.AsNoTracking().Where(tenant => tenant.Id == id)
            join country in Context.Set<Country>().AsNoTracking() on tenant.CountryId equals country.Id
            from wallet in Context.Set<Wallet>().AsNoTracking().Where(wallet => wallet.TenantId == tenant.Id).DefaultIfEmpty()
            select new { Tenant = tenant, CountryCurrency = country.CurrencyCode, Balance = wallet == null ? 0m : wallet.Balance, WalletCurrency = wallet == null ? null : wallet.CurrencyCode })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        var t = row.Tenant;
        return new ClientDetail(
            t.Id, t.Name.En, t.Name.Ar, t.CountryId, t.GovernorateId, t.Address, t.Mobile, t.LicenceNo, t.ManagerName, t.ManagerMobile, t.Email, t.PlanId,
            t.PortalSlug, t.PortalUrl(portalDomain), t.PreferredLanguage, t.Source.ToString(), t.Status.ToString(), t.CreatedAtUtc, t.TrialEndsAtUtc, t.ActivatedAtUtc,
            t.DeactivatedAtUtc, t.DeactivationReason, t.AdministratorUserId, t.AdministratorUsername, row.Balance, (row.WalletCurrency ?? row.CountryCurrency).Trim(), 0, 0);
    }

    public async Task<(TenantStatus Status, DateTimeOffset? TrialEndsAtUtc)?> FindStatusAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await Set.AsNoTracking().Where(tenant => tenant.Id == id).Select(tenant => new { tenant.Status, tenant.TrialEndsAtUtc }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return row is null ? null : (row.Status, row.TrialEndsAtUtc);
    }
}

internal sealed class PlanRepository(AppDbContext context) : Repository<Plan>(context), IPlanRepository
{
    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken)
    {
        var value = (code ?? string.Empty).Trim().ToUpperInvariant();
        return Set.AnyAsync(plan => plan.Code == value, cancellationToken);
    }

    public async Task<Paged<PlanListItem>> ListAsync(bool? isActive, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Set.AsNoTracking();
        if (isActive is { } active)
        {
            query = query.Where(plan => plan.IsActive == active);
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query.OrderBy(plan => plan.MonthlyFee).ThenBy(plan => plan.Code).Skip(page.Skip).Take(page.PageSize)
            .Select(plan => new PlanListItem(
                plan.Id, plan.Code, plan.Name.En, plan.Name.Ar, plan.MonthlyFee, plan.CurrencyCode, plan.MaxBranches, plan.MaxUsers, plan.SmsQuota, plan.MaxAnalysers, plan.IsActive,
                Context.Set<Tenant>().Count(tenant => tenant.PlanId == plan.Id)))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Paged<PlanListItem>(items.Select(item => item with { CurrencyCode = item.CurrencyCode.Trim() }).ToList(), page.Page, page.PageSize, total);
    }

    public Task<Plan?> FindDefaultForSubscriptionAsync(CancellationToken cancellationToken) =>
        Set.Where(plan => plan.IsActive).OrderBy(plan => plan.MonthlyFee).ThenBy(plan => plan.Code).FirstOrDefaultAsync(cancellationToken);
}

internal sealed class WalletRepository(AppDbContext context) : Repository<Wallet>(context), IWalletRepository
{
    public Task<Wallet?> GetByTenantForUpdateAsync(Guid tenantId, CancellationToken cancellationToken) =>
        Set.FirstOrDefaultAsync(wallet => wallet.TenantId == tenantId, cancellationToken);

    public Task<WalletTopUpRequest?> FindTopUpRequestAsync(Guid walletId, Guid requestId, CancellationToken cancellationToken) =>
        Context.Set<WalletTopUpRequest>().FirstOrDefaultAsync(request => request.WalletId == walletId && request.Id == requestId, cancellationToken);

    public async Task<WalletDetail?> GetDetailAsync(Guid tenantId, DateTimeOffset monthStartUtc, CancellationToken cancellationToken)
    {
        var row = await (
            from wallet in Set.AsNoTracking().Where(wallet => wallet.TenantId == tenantId)
            join tenant in Context.Set<Tenant>().AsNoTracking() on wallet.TenantId equals tenant.Id
            join plan in Context.Set<Plan>().AsNoTracking() on tenant.PlanId equals plan.Id
            select new
            {
                wallet.Id,
                wallet.TenantId,
                NameEn = tenant.Name.En,
                NameAr = tenant.Name.Ar,
                wallet.Balance,
                wallet.CurrencyCode,
                wallet.NextChargeAtUtc,
                tenant.PlanId,
                plan.Code,
                PlanEn = plan.Name.En,
                PlanAr = plan.Name.Ar,
                plan.MonthlyFee,
                PlanCurrency = plan.CurrencyCode,
                plan.SmsQuota,
                SmsUsed = Context.Set<WalletTransaction>().Count(transaction => transaction.WalletId == wallet.Id && transaction.Kind == WalletTransactionKind.Usage && transaction.AtUtc >= monthStartUtc),
            }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        var pending = await (
            from request in Context.Set<WalletTopUpRequest>().AsNoTracking().Where(request => request.WalletId == row.Id && request.Status == TopUpRequestStatus.Pending)
            from user in Context.PlatformUsers.AsNoTracking().Where(user => user.Id == request.RequestedByUserId).DefaultIfEmpty()
            orderby request.RequestedAtUtc
            select new TopUpRequestItem(request.Id, request.Amount, request.Note, request.Status.ToString(), request.RequestedAtUtc, request.RequestedByUserId, user == null ? null : user.DisplayName, request.DecidedAtUtc))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new WalletDetail(
            row.Id, row.TenantId, row.NameEn, row.NameAr, row.Balance, row.CurrencyCode.Trim(), row.NextChargeAtUtc, row.PlanId, row.Code, row.PlanEn, row.PlanAr,
            row.MonthlyFee, row.PlanCurrency.Trim(), row.SmsQuota, row.SmsUsed, pending);
    }

    public async Task<Paged<WalletTransactionItem>> ListTransactionsAsync(Guid tenantId, WalletTransactionKind? kind, PageRequest page, CancellationToken cancellationToken)
    {
        var walletId = await Set.AsNoTracking().Where(wallet => wallet.TenantId == tenantId).Select(wallet => (Guid?)wallet.Id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (walletId is null)
        {
            return Paged<WalletTransactionItem>.Empty(page);
        }

        var query = Context.Set<WalletTransaction>().AsNoTracking().Where(transaction => transaction.WalletId == walletId.Value);
        if (kind is { } wanted)
        {
            query = query.Where(transaction => transaction.Kind == wanted);
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await (
            from transaction in query.OrderByDescending(transaction => transaction.AtUtc).ThenByDescending(transaction => transaction.Id).Skip(page.Skip).Take(page.PageSize)
            from user in Context.PlatformUsers.AsNoTracking().Where(user => user.Id == transaction.ByUserId).DefaultIfEmpty()
            select new { transaction, ByName = user == null ? null : user.DisplayName })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var items = rows
            .OrderByDescending(row => row.transaction.AtUtc).ThenByDescending(row => row.transaction.Id)
            .Select(row => new WalletTransactionItem(row.transaction.Id, row.transaction.Kind.ToString(), row.transaction.Amount, row.transaction.BalanceAfter, row.transaction.Reference, row.transaction.Note, row.transaction.AtUtc, row.transaction.ByUserId, row.ByName))
            .ToList();
        return new Paged<WalletTransactionItem>(items, page.Page, page.PageSize, total);
    }
}

internal sealed class CountryRepository(AppDbContext context) : Repository<Country>(context), ICountryRepository
{
    public Task<bool> IsoCodeExistsAsync(string isoCode, CancellationToken cancellationToken)
    {
        var value = (isoCode ?? string.Empty).Trim().ToUpperInvariant();
        return Set.AnyAsync(country => country.IsoCode == value, cancellationToken);
    }

    public async Task<bool> IsReferencedAsync(Guid countryId, CancellationToken cancellationToken) =>
        await Context.Set<Governorate>().AnyAsync(governorate => governorate.CountryId == countryId, cancellationToken).ConfigureAwait(false)
        || await Context.Set<Tenant>().AnyAsync(tenant => tenant.CountryId == countryId, cancellationToken).ConfigureAwait(false);

    public async Task<Paged<CountryListItem>> ListAsync(string? search, bool? isActive, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Set.AsNoTracking();
        if (search is { Length: > 0 })
        {
            var pattern = Like.Contains(search);
            query = query.Where(country => EF.Functions.ILike(country.Name.En, pattern, Like.Escape) || EF.Functions.ILike(country.Name.Ar, pattern, Like.Escape) || EF.Functions.ILike(country.IsoCode, pattern, Like.Escape) || EF.Functions.ILike(country.DialCode, pattern, Like.Escape));
        }

        if (isActive is { } active)
        {
            query = query.Where(country => country.IsActive == active);
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query.OrderBy(country => country.Name.En).Skip(page.Skip).Take(page.PageSize)
            .Select(country => new CountryListItem(
                country.Id, country.Name.En, country.Name.Ar, country.IsoCode, country.DialCode, country.Currency, country.CurrencyCode, country.Language, country.TimeZone,
                country.MobileLength, country.Flag, country.Remarks, country.IsActive, Context.Set<Governorate>().Count(governorate => governorate.CountryId == country.Id)))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Paged<CountryListItem>(items.Select(item => item with { IsoCode = item.IsoCode.Trim(), CurrencyCode = item.CurrencyCode.Trim() }).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<IReadOnlyList<PublicCountry>> ListPublicAsync(CancellationToken cancellationToken)
    {
        var items = await Set.AsNoTracking().Where(country => country.IsActive).OrderBy(country => country.Name.En)
            .Select(country => new PublicCountry(country.Id, country.Name.En, country.Name.Ar, country.IsoCode, country.DialCode, country.MobileLength, country.Flag, country.CurrencyCode, country.Language))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return items.Select(item => item with { IsoCode = item.IsoCode.Trim(), CurrencyCode = item.CurrencyCode.Trim() }).ToList();
    }

    public void Remove(Country country)
    {
        ArgumentNullException.ThrowIfNull(country);
        Set.Remove(country);
    }
}

internal sealed class GovernorateRepository(AppDbContext context) : Repository<Governorate>(context), IGovernorateRepository
{
    public Task<bool> NameExistsAsync(Guid countryId, string nameEn, Guid? exceptId, CancellationToken cancellationToken)
    {
        var value = (nameEn ?? string.Empty).Trim().ToLower(System.Globalization.CultureInfo.InvariantCulture);
        return Set.AnyAsync(governorate => governorate.CountryId == countryId && governorate.Name.En.ToLower() == value && (exceptId == null || governorate.Id != exceptId), cancellationToken);
    }

    public async Task<Paged<GovernorateListItem>> ListAsync(Guid? countryId, string? search, bool? isActive, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Set.AsNoTracking();
        if (countryId is { } country)
        {
            query = query.Where(governorate => governorate.CountryId == country);
        }

        if (search is { Length: > 0 })
        {
            var pattern = Like.Contains(search);
            query = query.Where(governorate => EF.Functions.ILike(governorate.Name.En, pattern, Like.Escape) || EF.Functions.ILike(governorate.Name.Ar, pattern, Like.Escape) || (governorate.District != null && EF.Functions.ILike(governorate.District, pattern, Like.Escape)));
        }

        if (isActive is { } active)
        {
            query = query.Where(governorate => governorate.IsActive == active);
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var items = await (
            from governorate in query.OrderBy(governorate => governorate.Name.En).Skip(page.Skip).Take(page.PageSize)
            join owner in Context.Set<Country>().AsNoTracking() on governorate.CountryId equals owner.Id
            select new GovernorateListItem(governorate.Id, governorate.CountryId, owner.Name.En, owner.Name.Ar, governorate.Name.En, governorate.Name.Ar, governorate.District, governorate.Remarks, governorate.IsActive))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Paged<GovernorateListItem>(items.OrderBy(item => item.NameEn, StringComparer.OrdinalIgnoreCase).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<IReadOnlyList<PublicGovernorate>> ListPublicAsync(Guid? countryId, CancellationToken cancellationToken) =>
        await Set.AsNoTracking().Where(governorate => (countryId == null || governorate.CountryId == countryId) && governorate.IsActive).OrderBy(governorate => governorate.Name.En)
            .Select(governorate => new PublicGovernorate(governorate.Id, governorate.CountryId, governorate.Name.En, governorate.Name.Ar, governorate.District))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
}

internal sealed class NotificationTemplateRepository(AppDbContext context) : Repository<NotificationTemplate>(context), INotificationTemplateRepository
{
    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken)
    {
        var value = (code ?? string.Empty).Trim().ToLowerInvariant();
        return Set.AnyAsync(template => template.Code == value, cancellationToken);
    }

    public async Task<Paged<NotificationTemplateListItem>> ListAsync(string? search, bool? isActive, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Set.AsNoTracking();
        if (search is { Length: > 0 })
        {
            var pattern = Like.Contains(search);
            query = query.Where(template => EF.Functions.ILike(template.Code, pattern, Like.Escape) || EF.Functions.ILike(template.TextEn, pattern, Like.Escape) || EF.Functions.ILike(template.TextAr, pattern, Like.Escape));
        }

        if (isActive is { } active)
        {
            query = query.Where(template => template.IsActive == active);
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query.OrderBy(template => template.Code).Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        var items = rows.Select(template => new NotificationTemplateListItem(template.Id, template.Code, template.Channel.ToString(), template.SubjectEn, template.SubjectAr, template.TextEn, template.TextAr, template.IsActive)).ToList();
        return new Paged<NotificationTemplateListItem>(items, page.Page, page.PageSize, total);
    }
}

internal sealed class AnalyserModelRepository(AppDbContext context) : Repository<AnalyserModel>(context), IAnalyserModelRepository
{
    public Task<bool> ExistsAsync(string vendor, string model, Guid? exceptId, CancellationToken cancellationToken) =>
        Set.AnyAsync(row => row.Vendor.ToLower() == vendor.ToLower() && row.Model.ToLower() == model.ToLower() && (exceptId == null || row.Id != exceptId), cancellationToken);

    public async Task<Paged<AnalyserModelListItem>> ListAsync(string? search, bool? isActive, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Set.AsNoTracking();
        if (search is { Length: > 0 })
        {
            var pattern = Like.Contains(search);
            query = query.Where(row => EF.Functions.ILike(row.Vendor, pattern, Like.Escape) || EF.Functions.ILike(row.Model, pattern, Like.Escape) || EF.Functions.ILike(row.Discipline, pattern, Like.Escape));
        }

        if (isActive is { } active)
        {
            query = query.Where(row => row.IsActive == active);
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query.OrderBy(row => row.Vendor).ThenBy(row => row.Model).Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        var items = rows.Select(row => new AnalyserModelListItem(row.Id, row.Vendor, row.Model, row.Discipline, row.Protocol.ToString(), row.PhysicalLink.ToString(), row.Modes, row.DriverVersion, row.IsActive)).ToList();
        return new Paged<AnalyserModelListItem>(items, page.Page, page.PageSize, total);
    }
}

internal sealed class PcrKitTemplateRepository(AppDbContext context) : Repository<PcrKitTemplate>(context), IPcrKitTemplateRepository
{
    public Task<bool> NameExistsAsync(string name, Guid? exceptId, CancellationToken cancellationToken) =>
        Set.AnyAsync(kit => kit.Name.ToLower() == name.ToLower() && (exceptId == null || kit.Id != exceptId), cancellationToken);

    public async Task<Paged<PcrKitListItem>> ListAsync(string? search, bool? isActive, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Set.AsNoTracking();
        if (search is { Length: > 0 })
        {
            var pattern = Like.Contains(search);
            query = query.Where(kit => EF.Functions.ILike(kit.Name, pattern, Like.Escape));
        }

        if (isActive is { } active)
        {
            query = query.Where(kit => kit.IsActive == active);
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await (
            from kit in query.OrderBy(kit => kit.Name).Skip(page.Skip).Take(page.PageSize)
            from model in Context.Set<AnalyserModel>().AsNoTracking().Where(model => model.Id == kit.AnalyserModelId).DefaultIfEmpty()
            select new { kit, ModelName = model == null ? null : model.Vendor + " " + model.Model })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var items = rows.OrderBy(row => row.kit.Name, StringComparer.OrdinalIgnoreCase)
            .Select(row => new PcrKitListItem(row.kit.Id, row.kit.Name, row.kit.AnalyserModelId, row.ModelName, row.kit.LayoutJson, row.kit.ThresholdsJson, row.kit.VerdictRulesJson, row.kit.IsActive))
            .ToList();
        return new Paged<PcrKitListItem>(items, page.Page, page.PageSize, total);
    }
}

internal sealed class CodeSystemRepository(AppDbContext context) : Repository<CodeSystem>(context), ICodeSystemRepository
{
    public Task<bool> ExistsAsync(CodeSystemName name, string version, Guid? exceptId, CancellationToken cancellationToken) =>
        Set.AnyAsync(row => row.Name == name && row.Version.ToLower() == version.ToLower() && (exceptId == null || row.Id != exceptId), cancellationToken);

    public async Task<Paged<CodeSystemListItem>> ListAsync(string? search, bool? isActive, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Set.AsNoTracking();
        if (search is { Length: > 0 })
        {
            var pattern = Like.Contains(search);
            query = query.Where(row => EF.Functions.ILike(row.Version, pattern, Like.Escape) || (row.Use != null && EF.Functions.ILike(row.Use, pattern, Like.Escape)));
        }

        if (isActive is { } active)
        {
            query = query.Where(row => row.IsActive == active);
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query.OrderBy(row => row.Name).ThenByDescending(row => row.Version).Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        var items = rows.Select(row => new CodeSystemListItem(row.Id, row.Name.ToString(), row.Version, row.Use, row.ItemCount, row.LastImportedAtUtc, row.IsActive)).ToList();
        return new Paged<CodeSystemListItem>(items, page.Page, page.PageSize, total);
    }

    public async Task<IReadOnlyDictionary<string, CodeSystemItem>> LoadItemsAsync(Guid codeSystemId, CancellationToken cancellationToken) =>
        await Context.Set<CodeSystemItem>().Where(item => item.CodeSystemId == codeSystemId).ToDictionaryAsync(item => item.Code, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);

    public void AddItems(IEnumerable<CodeSystemItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Context.Set<CodeSystemItem>().AddRange(items);
    }

    public async Task<Paged<CodeSystemItemView>> ListItemsAsync(Guid codeSystemId, string? search, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Context.Set<CodeSystemItem>().AsNoTracking().Where(item => item.CodeSystemId == codeSystemId);
        if (search is { Length: > 0 })
        {
            var pattern = Like.Contains(search);
            query = query.Where(item => EF.Functions.ILike(item.Code, pattern, Like.Escape) || EF.Functions.ILike(item.Display, pattern, Like.Escape));
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query.OrderBy(item => item.Code).Skip(page.Skip).Take(page.PageSize).Select(item => new CodeSystemItemView(item.Code, item.Display)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Paged<CodeSystemItemView>(items, page.Page, page.PageSize, total);
    }
}

internal sealed class RuleTemplateRepository(AppDbContext context) : Repository<RuleTemplate>(context), IRuleTemplateRepository
{
    public Task<bool> NameExistsAsync(string name, Guid? exceptId, CancellationToken cancellationToken) =>
        Set.AnyAsync(rule => rule.Name.ToLower() == name.ToLower() && (exceptId == null || rule.Id != exceptId), cancellationToken);

    public async Task<Paged<RuleTemplateListItem>> ListAsync(string? search, bool? isActive, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Set.AsNoTracking();
        if (search is { Length: > 0 })
        {
            var pattern = Like.Contains(search);
            query = query.Where(rule => EF.Functions.ILike(rule.Name, pattern, Like.Escape) || (rule.ClinicalGroupName != null && EF.Functions.ILike(rule.ClinicalGroupName, pattern, Like.Escape)) || EF.Functions.ILike(rule.Formula, pattern, Like.Escape));
        }

        if (isActive is { } active)
        {
            query = query.Where(rule => rule.IsActive == active);
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query.OrderBy(rule => rule.Name).Skip(page.Skip).Take(page.PageSize)
            .Select(rule => new RuleTemplateListItem(rule.Id, rule.Name, rule.ClinicalGroupName, rule.Formula, rule.Description, rule.IsActive))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Paged<RuleTemplateListItem>(items, page.Page, page.PageSize, total);
    }
}

internal sealed class SubscriptionRequestRepository(AppDbContext context) : Repository<SubscriptionRequest>(context), ISubscriptionRequestRepository;
