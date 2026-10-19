using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Pricing;
using Platform.Contracts.Common;
using Platform.Contracts.Pricing;
using Platform.Domain.Pricing;
using Platform.Domain.Settings;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.UnitTests.Pricing;

// In-memory stand-ins for the ports of the Pricing handlers, so the use-case decisions are tested without a database
// (the engine side lives in tests/Platform.IntegrationTests/PricingModuleTests.cs).

internal sealed class PricingWorld
{
    public static readonly DateTimeOffset Now = new(2026, 10, 9, 14, 0, 0, TimeSpan.Zero);

    public PricingWorld()
    {
        PayerLookup = new FakePayerLookup(Payers);
    }

    public FakePayers Payers { get; } = new();

    public FakeLists Lists { get; } = new();

    public FakeContracts Contracts { get; } = new();

    public FakeCatalog Catalog { get; } = new();

    public FakePayerLookup PayerLookup { get; }

    public RecordingPriceLookup Prices { get; } = new();

    public CountingUnitOfWork UnitOfWork { get; } = new();

    public FixedPricingClock Clock { get; } = new(Now);

    public FakePricingSettings Settings { get; } = new();

    public FakePricingUser User { get; } = new();
}

internal sealed class FixedPricingClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; } = now;
}

internal sealed class CountingUnitOfWork : IUnitOfWork
{
    public int Commits { get; private set; }

    /// <summary>Thrown once by the next commit (a concurrent writer won a unique index).</summary>
    public DomainException? FailNext { get; set; }

    /// <summary>Runs when <see cref="FailNext"/> is thrown (the winner's write becomes visible).</summary>
    public Action? OnFailure { get; set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (FailNext is { } failure)
        {
            FailNext = null;
            OnFailure?.Invoke();
            throw failure;
        }

        Commits++;
        return Task.FromResult(1);
    }
}

internal sealed class FakePayers : IPayerRepository
{
    public List<Payer> Items { get; } = [];

    public List<Payer> Staged { get; } = [];

    public Task<Payer?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Items.FirstOrDefault(payer => payer.Id == id));

    public void Add(Payer aggregate) => Staged.Add(aggregate);

    public Task<bool> SearchKeyExistsAsync(string searchKey, Guid? exceptPayerId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Any(payer => payer.SearchKey == searchKey && payer.Id != exceptPayerId));

    public Task<bool> WebUsernameExistsAsync(string webUsername, Guid? exceptPayerId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Any(payer => payer.WebUsername == webUsername && payer.Id != exceptPayerId));

    public Task<DefaultPayerIds?> FindDefaultAsync(CancellationToken cancellationToken)
    {
        var payer = Items.FirstOrDefault(candidate => candidate.IsDefault);
        return Task.FromResult(payer is null ? null : new DefaultPayerIds(payer.Id, payer.Tiers.Single(tier => tier.IsDefault).Id, false));
    }

    public Task<Paged<PayerListItem>> ListAsync(PayerFilter filter, PageRequest page, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<PayerListItem?> GetListItemAsync(Guid payerId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<TierOption>> ListTierOptionsAsync(bool activeOnly, CancellationToken cancellationToken) => throw new NotSupportedException();

    /// <summary>What a commit would make visible.</summary>
    public void Commit()
    {
        Items.AddRange(Staged);
        Staged.Clear();
    }
}

internal sealed class FakePayerLookup(FakePayers payers) : IPayerLookup
{
    public Task<PayerSummary?> FindPayerAsync(Guid payerId, CancellationToken cancellationToken)
    {
        var payer = payers.Items.FirstOrDefault(candidate => candidate.Id == payerId);
        return Task.FromResult(payer is null ? null : new PayerSummary(payer.Id, payer.Name.En, payer.Name.Ar, payer.SearchKey, payer.Type.ToString(), payer.IsActive, payer.IsDefault));
    }

    public Task<ContractTierSummary?> FindTierAsync(Guid contractTierId, CancellationToken cancellationToken) =>
        Task.FromResult(payers.Items.SelectMany(payer => payer.Tiers.Select(tier => Summary(payer, tier))).FirstOrDefault(tier => tier.Id == contractTierId));

    public Task<IReadOnlyList<ContractTierSummary>> ListTiersAsync(Guid payerId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ContractTierSummary>>(payers.Items.Where(payer => payer.Id == payerId).SelectMany(payer => payer.Tiers.Select(tier => Summary(payer, tier))).ToList());

    public Task<DefaultPayerIds?> FindDefaultAsync(CancellationToken cancellationToken) => payers.FindDefaultAsync(cancellationToken);

    private static ContractTierSummary Summary(Payer payer, ContractTier tier) =>
        new(tier.Id, payer.Id, payer.Type.ToString(), tier.Name.En, tier.Name.Ar, tier.IsActive, payer.IsActive, tier.IsDefault);
}

internal sealed class FakeLists : IBasePriceListRepository
{
    public List<BasePriceList> Items { get; } = [];

    public List<BasePriceList> Staged { get; } = [];

    public Task<BasePriceList?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Items.FirstOrDefault(list => list.Id == id));

    public void Add(BasePriceList aggregate) => Staged.Add(aggregate);

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) => Task.FromResult(Items.Any(list => list.Code == code));

    public Task<IReadOnlySet<Guid>> ExistingIdsAsync(IReadOnlyCollection<Guid> listIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<Guid>>(Items.Where(list => listIds.Contains(list.Id)).Select(list => list.Id).ToHashSet());

    public Task<IReadOnlyDictionary<Guid, PriceListSnapshot>> SnapshotsAsync(IReadOnlyCollection<Guid> listIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, PriceListSnapshot>>(Items.Where(list => listIds.Contains(list.Id)).ToDictionary(list => list.Id, list => list.Snapshot()));

    public Task<Paged<BasePriceListItem>> ListAsync(BasePriceListFilter filter, PageRequest page, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<BasePriceListDetail?> GetDetailAsync(Guid listId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<PriceLineItem>?> GetLinesAsync(Guid listId, CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class FakeContracts : IContractPricingRepository
{
    public List<ContractPricing> Items { get; } = [];

    public List<ContractPricing> Staged { get; } = [];

    public Task<ContractPricing?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Items.FirstOrDefault(pricing => pricing.Id == id));

    public void Add(ContractPricing aggregate) => Staged.Add(aggregate);

    public Task<ContractPricing?> GetForUpdateByTierAsync(Guid contractTierId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(pricing => pricing.ContractTierId == contractTierId));

    public Task<IReadOnlyList<ContractPricing>> ListReferencingForUpdateAsync(Guid baseListId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ContractPricing>>(Items.Where(pricing => pricing.Sources.Any(source => source.BasePriceListId == baseListId)).ToList());

    public Task<ContractPricingView?> GetViewByTierAsync(Guid contractTierId, CancellationToken cancellationToken)
    {
        var pricing = Items.FirstOrDefault(candidate => candidate.ContractTierId == contractTierId);
        return Task.FromResult(pricing is null
            ? null
            : new ContractPricingView(pricing.Id, pricing.EffectiveFrom, pricing.EffectiveTo, pricing.AppliedAtUtc, [], pricing.Prices
                .Select(price => new ContractPriceItem(price.ServiceId, price.ListPrice, price.PatientShare, price.PayerShare, price.IsExcluded, price.AlertCondition, price.SourceListId, null, null, null, null))
                .ToList()));
    }

    public Task<IReadOnlyList<ContractPricingListItem>> ListAsync(Guid? payerId, CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class FakeCatalog : IPricingServiceCatalog
{
    public HashSet<Guid> Known { get; } = [];

    public List<IReadOnlyCollection<Guid>> Calls { get; } = [];

    public Task<IReadOnlySet<Guid>> ExistingServiceIdsAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken)
    {
        Calls.Add(serviceIds);
        return Task.FromResult<IReadOnlySet<Guid>>(serviceIds.Where(Known.Contains).ToHashSet());
    }

    /// <summary>Code → id of the known services (C21 import); codes compare upper-case.</summary>
    public Dictionary<string, Guid> Codes { get; } = new(StringComparer.Ordinal);

    public Task<IReadOnlyDictionary<string, Guid>> ServiceIdsByCodeAsync(IReadOnlyCollection<string> codes, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, Guid>>(codes.Select(code => code.Trim().ToUpperInvariant()).Distinct().Where(Codes.ContainsKey).ToDictionary(code => code, code => Codes[code], StringComparer.Ordinal));

    public Task<IReadOnlyDictionary<Guid, CatalogueServiceLabel>> ServiceLabelsAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, CatalogueServiceLabel>>(Codes.Where(pair => serviceIds.Contains(pair.Value)).ToDictionary(pair => pair.Value, pair => new CatalogueServiceLabel(pair.Value, pair.Key, $"{pair.Key} service", $"خدمة {pair.Key}")));
}

internal sealed class RecordingPriceLookup : IPriceLookup
{
    public List<(Guid? PayerId, Guid? TierId, IReadOnlyList<Guid> ServiceIds)> Calls { get; } = [];

    public Task<IReadOnlyList<PriceQuoteLine>> QuoteAsync(Guid? payerId, Guid? contractTierId, IReadOnlyList<Guid> serviceIds, CancellationToken cancellationToken)
    {
        Calls.Add((payerId, contractTierId, serviceIds));
        return Task.FromResult<IReadOnlyList<PriceQuoteLine>>(serviceIds.Select((id, index) => new PriceQuoteLine(id, 100m, 10m * (index + 1), 90m, null, true)).ToList());
    }
}

internal sealed class FakePricingSettings : ITenantSettings
{
    public ValueTask<T> GetAsync<T>(SettingDescriptor<T> descriptor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return ValueTask.FromResult(descriptor.Default);
    }

    public ValueTask<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
}

internal sealed class FakePricingUser : ICurrentUser
{
    public bool IsPrimaryAdministrator { get; set; }
    public bool IsAuthenticated => true;

    public Guid? UserId { get; } = Guid.CreateVersion7();

    public Guid? SessionId => null;

    public string? DisplayName => "Accounts Officer";

    public Guid? BranchId => null;
}
