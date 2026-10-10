using Platform.Application.Abstractions.Persistence;
using Platform.Contracts.Common;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.Identity;
using Platform.Domain.PlatformAdmin;

namespace Platform.Application.PlatformAdmin;

// Write sides and projections of platform administration. The saas tables carry no tenant: every read here is
// platform-wide, which is why every admin handler first checks the platform tenant (PlatformAdminScope, PLT-403).

public sealed record ClientFilter(string? Search, TenantSource? Source, Guid? PlanId, TenantStatus? Status);

public interface ITenantRepository : IRepository<Tenant>
{
    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken);

    Task<Paged<ClientListItem>> ListAsync(ClientFilter filter, PageRequest page, string portalDomain, CancellationToken cancellationToken);

    Task<ClientDetail?> GetDetailAsync(Guid id, string portalDomain, CancellationToken cancellationToken);

    /// <summary>Read-only status for the sign-in port; <see langword="null"/> when the tenant has no client record.</summary>
    Task<(TenantStatus Status, DateTimeOffset? TrialEndsAtUtc)?> FindStatusAsync(Guid id, CancellationToken cancellationToken);
}

public interface IPlanRepository : IRepository<Plan>
{
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    Task<Paged<PlanListItem>> ListAsync(bool? isActive, PageRequest page, CancellationToken cancellationToken);

    /// <summary>The wizard's plan (assumption Q2): the active plan with the lowest monthly fee, ties by code.</summary>
    Task<Plan?> FindDefaultForSubscriptionAsync(CancellationToken cancellationToken);
}

public interface IWalletRepository : IRepository<Wallet>
{
    Task<Wallet?> GetByTenantForUpdateAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Loads a top-up request of the wallet (tracked) so a decision can go through the root.</summary>
    Task<WalletTopUpRequest?> FindTopUpRequestAsync(Guid walletId, Guid requestId, CancellationToken cancellationToken);

    Task<WalletDetail?> GetDetailAsync(Guid tenantId, DateTimeOffset monthStartUtc, CancellationToken cancellationToken);

    Task<Paged<WalletTransactionItem>> ListTransactionsAsync(Guid tenantId, WalletTransactionKind? kind, PageRequest page, CancellationToken cancellationToken);
}

public interface ICountryRepository : IRepository<Country>
{
    Task<bool> IsoCodeExistsAsync(string isoCode, CancellationToken cancellationToken);

    /// <summary>INV-PLT-010: a governorate, a client or a wizard request references the country.</summary>
    Task<bool> IsReferencedAsync(Guid countryId, CancellationToken cancellationToken);

    Task<Paged<CountryListItem>> ListAsync(string? search, bool? isActive, PageRequest page, CancellationToken cancellationToken);

    Task<IReadOnlyList<PublicCountry>> ListPublicAsync(CancellationToken cancellationToken);

    /// <summary>Stages the deletion of an unreferenced country; the handler commits.</summary>
    void Remove(Country country);
}

public interface IGovernorateRepository : IRepository<Governorate>
{
    Task<bool> NameExistsAsync(Guid countryId, string nameEn, Guid? exceptId, CancellationToken cancellationToken);

    Task<Paged<GovernorateListItem>> ListAsync(Guid? countryId, string? search, bool? isActive, PageRequest page, CancellationToken cancellationToken);

    Task<IReadOnlyList<PublicGovernorate>> ListPublicAsync(Guid? countryId, CancellationToken cancellationToken);
}

public interface INotificationTemplateRepository : IRepository<NotificationTemplate>
{
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    Task<Paged<NotificationTemplateListItem>> ListAsync(string? search, bool? isActive, PageRequest page, CancellationToken cancellationToken);
}

public interface IAnalyserModelRepository : IRepository<AnalyserModel>
{
    Task<bool> ExistsAsync(string vendor, string model, Guid? exceptId, CancellationToken cancellationToken);

    Task<Paged<AnalyserModelListItem>> ListAsync(string? search, bool? isActive, PageRequest page, CancellationToken cancellationToken);
}

public interface IPcrKitTemplateRepository : IRepository<PcrKitTemplate>
{
    Task<bool> NameExistsAsync(string name, Guid? exceptId, CancellationToken cancellationToken);

    Task<Paged<PcrKitListItem>> ListAsync(string? search, bool? isActive, PageRequest page, CancellationToken cancellationToken);
}

public interface ICodeSystemRepository : IRepository<CodeSystem>
{
    Task<bool> ExistsAsync(CodeSystemName name, string version, Guid? exceptId, CancellationToken cancellationToken);

    Task<Paged<CodeSystemListItem>> ListAsync(string? search, bool? isActive, PageRequest page, CancellationToken cancellationToken);

    /// <summary>Every item of the code system, tracked and keyed by code, for an import (upsert by code).</summary>
    Task<IReadOnlyDictionary<string, CodeSystemItem>> LoadItemsAsync(Guid codeSystemId, CancellationToken cancellationToken);

    void AddItems(IEnumerable<CodeSystemItem> items);

    Task<Paged<CodeSystemItemView>> ListItemsAsync(Guid codeSystemId, string? search, PageRequest page, CancellationToken cancellationToken);
}

public interface IRuleTemplateRepository : IRepository<RuleTemplate>
{
    Task<bool> NameExistsAsync(string name, Guid? exceptId, CancellationToken cancellationToken);

    Task<Paged<RuleTemplateListItem>> ListAsync(string? search, bool? isActive, PageRequest page, CancellationToken cancellationToken);
}

public interface ISubscriptionRequestRepository : IRepository<SubscriptionRequest>;

/// <summary>Branches and users of each client (SA-04 columns), read across tenants without elevation (assumption Q8).</summary>
public interface IClientUsageCounts
{
    Task<IReadOnlyDictionary<Guid, ClientUsage>> GetAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken cancellationToken);
}

public sealed record ClientUsage(int Branches, int Users);

/// <summary>
/// Per-period activity of the clients (SA-03): served patients, tests, registrations, registrations per client.
/// The placeholder adapter answers zeros; the registration/reporting module replaces it (MODULES.md: rep, wave 3).
/// </summary>
public interface IClientActivityReader
{
    Task<ClientActivitySnapshot> ReadAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);
}

public sealed record ClientActivitySnapshot(int ServedPatients, int Tests, int Registrations, IReadOnlyList<ClientRegistrations> ByClient)
{
    public static ClientActivitySnapshot Empty { get; } = new(0, 0, 0, []);
}

public sealed record ClientRegistrations(Guid TenantId, int Registrations);

/// <summary>The aggregate figures of the dashboard (SA-03) that live in the platform tables.</summary>
public interface IPlatformDashboardReader
{
    Task<ClientCounts> CountClientsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>Active memberships of the platform tenant (National Technology staff).</summary>
    Task<int> CountAdminUsersAsync(CancellationToken cancellationToken);

    Task<int> CountWalletsAtOrBelowZeroAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PlanShare>> PlanDistributionAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<RecentClient>> RecentClientsAsync(int take, string portalDomain, CancellationToken cancellationToken);

    /// <summary>Names of the given clients, for the per-client activity list.</summary>
    Task<IReadOnlyDictionary<Guid, (string NameEn, string NameAr)>> ClientNamesAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken cancellationToken);
}

/// <summary>Live refresh sessions (SA-08 and client deactivation). Sessions are global rows (identity), read here by tenant.</summary>
public interface IPlatformSessionDirectory
{
    /// <summary>Live sessions of a tenant with the user's names, newest first.</summary>
    Task<IReadOnlyList<AdminSessionRow>> ListLiveAsync(Guid tenantId, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>Live sessions of a tenant, tracked, for revocation in the caller's unit of work.</summary>
    Task<IReadOnlyList<RefreshSession>> ListLiveForUpdateAsync(Guid tenantId, DateTimeOffset nowUtc, CancellationToken cancellationToken);
}

public sealed record AdminSessionRow(
    Guid SessionId,
    Guid FamilyId,
    Guid UserId,
    string DisplayName,
    string Username,
    string? IpAddress,
    DateTimeOffset SinceUtc,
    DateTimeOffset LastSeenAtUtc,
    DateTimeOffset ExpiresAtUtc);

/// <summary>Where the laboratories' portals live: <c>Lis:Platform:PortalDomain</c>, default <c>skylis.app</c>.</summary>
public interface IPortalAddressOptions
{
    string PortalDomain { get; }
}
