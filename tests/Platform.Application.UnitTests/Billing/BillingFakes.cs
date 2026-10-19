using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Billing;
using Platform.Application.Patients;
using Platform.Application.Registration;
using Platform.Contracts.Billing;
using Platform.Contracts.Common;
using Platform.Domain.Billing;
using Platform.Domain.Organization;
using Platform.Domain.Settings;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.UnitTests.Billing;

// In-memory stand-ins for the ports of the Billing handlers, so the use-case logic is tested without a database (the
// engine side lives in tests/Platform.IntegrationTests/BillingModuleTests.cs).

internal sealed class BillingWorld
{
    public static readonly DateTimeOffset Now = new(2026, 10, 9, 14, 0, 0, TimeSpan.Zero);

    public Guid Cashier { get; } = Guid.CreateVersion7();

    public Guid Branch { get; } = Guid.CreateVersion7();

    public Guid TreasuryId { get; } = Guid.CreateVersion7();

    public Guid RefundReasonId { get; } = Guid.CreateVersion7();

    public FakeVisits Visits { get; } = new();

    public FakeStations Stations { get; } = new();

    public InMemoryAccounts Accounts { get; } = new();

    public InMemoryClosings Closings { get; } = new();

    public FakeDrawer Drawer { get; } = new();

    public FakeReasons Reasons { get; } = new();

    /// <summary>The laboratory's payment method definitions (change request 2026-10-19); empty = every request uses the legacy kind rule.</summary>
    public InMemoryPaymentMethods Methods { get; } = new();

    public FakeDirectory Directory { get; } = new();

    public CountingNumbers Numbers { get; } = new();

    public CountingUnitOfWork UnitOfWork { get; } = new();

    public FakeUser User { get; } = new();

    public FakePrivileges Privileges { get; } = new();

    public FixedBillingClock Clock { get; } = new(Now);

    public FakeBillingSettings Settings { get; } = new();

    public FakeCollections Collections { get; } = new();

    public FakeLaboratoryZone Zone { get; } = new();

    public BillingWorld()
    {
        User.UserId = Cashier;
        User.BranchId = Branch;
        Stations.Add(new TreasuryStationView(TreasuryId, "MAIN", "Main drawer", "الخزينة الرئيسية", Branch, IsActive: true, ApprovedForDevice: true));
        Reasons.Active.Add(RefundReasonId);
    }

    public BillableVisit AddVisit(decimal patientShare, string accession, Guid? batchId = null, string? batchKind = null, string? batchNumber = null)
    {
        var visit = new BillableVisit(Guid.CreateVersion7(), accession, Guid.CreateVersion7(), Branch, null, null, batchId, "Open", Now.AddHours(-1), patientShare + 10m, 10m, patientShare, 0m, batchNumber, batchKind);
        Visits.Items.Add(visit);
        return visit;
    }
}

internal sealed class FakeVisits : IBillableVisitLookup
{
    public List<BillableVisit> Items { get; } = [];

    public List<BillableBatch> Batches { get; } = [];

    public List<BillableLine> Lines { get; } = [];

    public Task<BillableVisit?> GetAsync(Guid visitId, CancellationToken cancellationToken) => Task.FromResult(Items.FirstOrDefault(visit => visit.VisitId == visitId));

    public Task<BillableVisit?> GetByAccessionAsync(string accession, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(visit => string.Equals(visit.Accession, accession, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyDictionary<Guid, BillableVisit>> GetManyAsync(IReadOnlyCollection<Guid> visitIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, BillableVisit>>(Items.Where(visit => visitIds.Contains(visit.VisitId)).ToDictionary(visit => visit.VisitId));

    public Task<IReadOnlyList<BillableLine>> LinesAsync(Guid visitId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BillableLine>>([.. Lines.Where(line => line.VisitId == visitId)]);

    public Task<BillableBatch?> GetBatchAsync(Guid batchId, CancellationToken cancellationToken) => Task.FromResult(Batches.FirstOrDefault(batch => batch.BatchId == batchId));

    public Task<BillableBatch?> GetBatchByNumberAsync(string number, CancellationToken cancellationToken) => Task.FromResult(Batches.FirstOrDefault(batch => batch.Number == number));

    public Task<IReadOnlyList<BillableVisit>> BatchMembersAsync(Guid batchId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BillableVisit>>([.. Items.Where(visit => visit.BatchId == batchId).OrderBy(visit => visit.Accession, StringComparer.Ordinal)]);
}

internal sealed class FakeStations : ITreasuryStationLookup
{
    private readonly List<TreasuryStationView> _items = [];

    public void Add(TreasuryStationView station) => _items.Add(station);

    public void Replace(TreasuryStationView station)
    {
        _items.RemoveAll(item => item.TreasuryId == station.TreasuryId);
        _items.Add(station);
    }

    public string? LastDeviceId { get; private set; }

    public Task<TreasuryStationView?> GetAsync(Guid treasuryId, Guid userId, string deviceId, CancellationToken cancellationToken)
    {
        LastDeviceId = deviceId;
        return Task.FromResult(_items.FirstOrDefault(item => item.TreasuryId == treasuryId));
    }

    public Task<IReadOnlyList<BillingTreasuryItem>> ApprovedAsync(Guid userId, string deviceId, Guid branchId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BillingTreasuryItem>>([.. _items.Where(item => item.ApprovedForDevice && item.IsActive && item.BranchId == branchId).Select(Item)]);

    public Task<IReadOnlyList<BillingTreasuryItem>> OfBranchAsync(Guid branchId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BillingTreasuryItem>>([.. _items.Where(item => item.BranchId == branchId).Select(Item)]);

    public Task<BillingTreasuryItem?> GetTreasuryAsync(Guid treasuryId, CancellationToken cancellationToken) =>
        Task.FromResult(_items.Where(item => item.TreasuryId == treasuryId).Select(Item).FirstOrDefault());

    private static BillingTreasuryItem Item(TreasuryStationView view) => new(view.TreasuryId, view.Code, view.NameEn, view.NameAr, view.BranchId, view.IsActive);
}

internal sealed class InMemoryAccounts : IVisitAccountRepository
{
    public List<VisitAccount> Items { get; } = [];

    public Task<VisitAccount?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Items.FirstOrDefault(account => account.Id == id));

    public void Add(VisitAccount aggregate) => Items.Add(aggregate);

    public Task<VisitAccount?> GetByPaymentIdForUpdateAsync(Guid paymentId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(account => account.Payments.Any(payment => payment.Id == paymentId)));

    public Task<IReadOnlyDictionary<Guid, VisitAccount>> GetManyForUpdateAsync(IReadOnlyCollection<Guid> visitIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, VisitAccount>>(Items.Where(account => visitIds.Contains(account.Id)).ToDictionary(account => account.Id));

    public Task<Paged<PaymentListItem>> ListPaymentsAsync(PaymentFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var rows = Payments(filter).ToList();
        return Task.FromResult(new Paged<PaymentListItem>([.. rows.Skip(page.Skip).Take(page.PageSize)], page.Page, page.PageSize, rows.Count));
    }

    public Task<IReadOnlyList<PaymentListItem>> SearchPaymentsAsync(PaymentFilter filter, int maxRows, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PaymentListItem>>([.. Payments(filter).Take(maxRows)]);

    public Task<PaymentListItem?> GetPaymentAsync(Guid paymentId, CancellationToken cancellationToken) =>
        Task.FromResult(Payments(new PaymentFilter(null, null, null, null, null, null, null)).FirstOrDefault(item => item.Id == paymentId));

    public Task<Paged<RefundListItem>> ListRefundsAsync(RefundFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var rows = Refunds(filter).ToList();
        return Task.FromResult(new Paged<RefundListItem>([.. rows.Skip(page.Skip).Take(page.PageSize)], page.Page, page.PageSize, rows.Count));
    }

    public Task<IReadOnlyList<RefundListItem>> SearchRefundsAsync(RefundFilter filter, int maxRows, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RefundListItem>>([.. Refunds(filter).Take(maxRows)]);

    public Task<RefundListItem?> GetRefundAsync(Guid refundId, CancellationToken cancellationToken) =>
        Task.FromResult(Refunds(new RefundFilter(null, null, null, null, null, null)).FirstOrDefault(item => item.Id == refundId));

    private IEnumerable<PaymentListItem> Payments(PaymentFilter filter) =>
        Items.SelectMany(account => account.Payments)
            .Where(payment => (filter.VisitId is null || payment.VisitId == filter.VisitId) && (filter.Status is null || payment.Status == filter.Status) && (filter.Method is null || payment.Method == filter.Method))
            .OrderByDescending(payment => payment.PaidAtUtc)
            .Select(payment => new PaymentListItem(
                payment.Id, payment.ReceiptNumber, payment.VisitId, null, payment.Amount, payment.Method.ToString(), payment.Reference, payment.TreasuryId, "MAIN", "Main drawer", "الخزينة الرئيسية",
                payment.CashierUserId, "Cashier One", payment.BranchId, payment.DeviceId, payment.BatchId, payment.PaidAtUtc, payment.Status.ToString(), false, payment.VoidReason, null, payment.VoidedAtUtc));

    private IEnumerable<RefundListItem> Refunds(RefundFilter filter) =>
        Items.SelectMany(account => account.Refunds)
            .Where(refund => filter.VisitId is null || refund.VisitId == filter.VisitId)
            .OrderByDescending(refund => refund.RefundedAtUtc)
            .Select(refund => new RefundListItem(
                refund.Id, refund.Number, refund.VisitId, null, refund.Amount, refund.Method.ToString(), refund.Reference, refund.ReasonId, null, null, refund.Note, refund.TreasuryId, "MAIN",
                "Main drawer", "الخزينة الرئيسية", refund.CashierUserId, "Cashier One", refund.BranchId, refund.DeviceId, refund.RefundedAtUtc));
}

internal sealed class InMemoryClosings : ICashClosingRepository
{
    public List<CashClosing> Items { get; } = [];

    public bool CoversAnything { get; set; }

    public Task<CashClosing?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Items.FirstOrDefault(closing => closing.Id == id));

    public void Add(CashClosing aggregate) => Items.Add(aggregate);

    public Task<bool> OverlapsAsync(Guid treasuryId, Guid? cashierUserId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Any(closing => closing.Overlaps(treasuryId, cashierUserId, fromUtc, toUtc)));

    public Task<bool> CoversAsync(Guid treasuryId, Guid cashierUserId, DateTimeOffset atUtc, CancellationToken cancellationToken) =>
        Task.FromResult(CoversAnything || Items.Any(closing => closing.Covers(treasuryId, cashierUserId, atUtc)));

    public Task<Paged<CashClosingListItem>> ListAsync(CashClosingFilter filter, PageRequest page, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<CashClosingListItem>> SearchAsync(CashClosingFilter filter, int maxRows, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<CashClosingRecord?> GetRecordAsync(Guid closingId, CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class FakeDrawer : ICashDrawerQueries
{
    public ClosingTotals Totals { get; set; } = ClosingTotals.Empty;

    public Task<ClosingTotals> TotalsAsync(DrawerScope scope, CancellationToken cancellationToken) => Task.FromResult(Totals);

    public Task<IReadOnlyList<DrawerMovement>> MovementsAsync(DrawerScope scope, int maxRows, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DrawerMovement>>([]);

    public Task<IReadOnlyList<DrawerCashier>> CashiersAsync(Guid treasuryId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DrawerCashier>>([]);

    public Task<IReadOnlyDictionary<Guid, string>> UserNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(userIds.ToDictionary(id => id, _ => "Cashier One"));
}

internal sealed class FakeReasons : IRefundReasonLookup
{
    public HashSet<Guid> Active { get; } = [];

    public Task<RefundReasonView?> GetActiveAsync(Guid reasonId, CancellationToken cancellationToken) =>
        Task.FromResult(Active.Contains(reasonId) ? new RefundReasonView(reasonId, "Overpayment", "مبلغ زائد") : null);

    public Task<IReadOnlyDictionary<Guid, RefundReasonView>> GetManyAsync(IReadOnlyCollection<Guid> reasonIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, RefundReasonView>>(reasonIds.Where(Active.Contains).ToDictionary(id => id, id => new RefundReasonView(id, "Overpayment", "مبلغ زائد")));
}

internal sealed class InMemoryPaymentMethods : IPaymentMethodRepository
{
    public List<PaymentMethodDefinition> Items { get; } = [];

    public Task<PaymentMethodDefinition?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Items.FirstOrDefault(method => method.Id == id));

    public void Add(PaymentMethodDefinition aggregate) => Items.Add(aggregate);

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Any(method => method.Code == code.Trim().ToUpperInvariant()));

    public Task<PaymentMethodDefinition?> FindActiveByCodeAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(method => method.IsActive && method.Code == code.Trim().ToUpperInvariant()));

    public Task<bool> OtherActiveCashExistsAsync(Guid excludedId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Any(method => method.Id != excludedId && method.IsActive && method.Kind == PaymentMethod.Cash));

    public Task<IReadOnlyCollection<string>> ExistingCodesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<string>>(Items.Select(method => method.Code).ToList());

    public Task<Paged<PaymentMethodListItem>> ListAsync(string? search, bool? isActive, ListSort sort, PageRequest page, CancellationToken cancellationToken)
    {
        var rows = Items.Where(method => isActive is null || method.IsActive == isActive).OrderBy(method => method.Sequence).ThenBy(method => method.Code).Select(Item).ToList();
        return Task.FromResult(new Paged<PaymentMethodListItem>([.. rows.Skip(page.Skip).Take(page.PageSize)], page.Page, page.PageSize, rows.Count));
    }

    public Task<PaymentMethodListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Where(method => method.Id == id).Select(Item).FirstOrDefault());

    public Task<IReadOnlyList<PaymentMethodOption>> OptionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PaymentMethodOption>>(Items.Where(method => method.IsActive).OrderBy(method => method.Sequence).ThenBy(method => method.Code)
            .Select(method => new PaymentMethodOption(method.Id, method.Code, method.Name.En, method.Name.Ar, method.Kind.ToString(), method.RequiresReference)).ToList());

    private static PaymentMethodListItem Item(PaymentMethodDefinition method) =>
        new(method.Id, method.Code, method.Name.En, method.Name.Ar, method.Kind.ToString(), method.RequiresReference, method.IsActive, method.Sequence);
}

internal sealed class FakeDirectory : IBillingDirectory
{
    public Task<DocumentLetterhead> LetterheadAsync(Guid branchId, CancellationToken cancellationToken) => Task.FromResult(new DocumentLetterhead("Sky Lab", "مختبر سكاي", "Main", "الرئيسي"));

    public Task<IReadOnlyDictionary<Guid, PatientCaption>> PatientsAsync(IReadOnlyCollection<Guid> patientIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, PatientCaption>>(patientIds.ToDictionary(id => id, id => new PatientCaption(id, "PAT-2026-00001", "Patient Name")));

    public Task<IReadOnlyDictionary<Guid, NamedReference>> ServicesAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, NamedReference>>(serviceIds.ToDictionary(id => id, id => new NamedReference(id, "CBC", "Complete blood count", "صورة دم كاملة")));

    public Task<IReadOnlyDictionary<Guid, NamedReference>> PayersAsync(IReadOnlyCollection<Guid> payerIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, NamedReference>>(new Dictionary<Guid, NamedReference>());

    public Task<IReadOnlyDictionary<Guid, NamedReference>> BranchesAsync(IReadOnlyCollection<Guid> branchIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, NamedReference>>(branchIds.ToDictionary(id => id, id => new NamedReference(id, "MAIN", "Main branch", "الفرع الرئيسي")));
}

/// <summary>The cells of the collections report, whatever the scope asked for (the scope itself is recorded for the handler tests).</summary>
internal sealed class FakeCollections : ICollectionsReportQueries
{
    public List<CollectionsCell> Cells { get; } = [];

    public CollectionsScope? LastScope { get; private set; }

    public Task<IReadOnlyList<CollectionsCell>> CellsAsync(CollectionsScope scope, CancellationToken cancellationToken)
    {
        LastScope = scope;
        return Task.FromResult<IReadOnlyList<CollectionsCell>>([.. Cells]);
    }
}

/// <summary>A fixed laboratory zone (+03, no daylight saving) so the handler's local days are predictable.</summary>
internal sealed class FakeLaboratoryZone : ILaboratoryTimeZone
{
    public static readonly TimeZoneInfo PlusThree = TimeZoneInfo.CreateCustomTimeZone("Test+03", TimeSpan.FromHours(3), "Test +03", "Test +03");

    public TimeZoneInfo? Zone { get; set; } = PlusThree;

    public Task<TimeZoneInfo?> FindAsync(CancellationToken cancellationToken) => Task.FromResult(Zone);
}

internal sealed class FakeBalances(InMemoryAccounts accounts, FakeVisits visits) : IVisitBalanceLookup
{
    public async Task<VisitBalance?> GetAsync(Guid visitId, CancellationToken cancellationToken) =>
        (await GetManyAsync([visitId], cancellationToken)).TryGetValue(visitId, out var balance) ? balance : null;

    public Task<IReadOnlyDictionary<Guid, VisitBalance>> GetManyAsync(IReadOnlyCollection<Guid> visitIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, VisitBalance>();
        foreach (var visit in visits.Items.Where(visit => visitIds.Contains(visit.VisitId)))
        {
            var account = accounts.Items.FirstOrDefault(candidate => candidate.Id == visit.VisitId);
            var figures = new VisitBalanceFigures(visit.PatientShareTotal, account?.PaidTotal ?? 0m, account?.RefundedTotal ?? 0m);
            result[visit.VisitId] = new VisitBalance(visit.VisitId, visit.PatientShareTotal, figures.Paid, figures.Refunded, figures.Remaining, figures.Refundable);
        }

        return Task.FromResult<IReadOnlyDictionary<Guid, VisitBalance>>(result);
    }
}

internal sealed class CountingNumbers : IReferenceNumbers
{
    private readonly Dictionary<string, int> _counters = new(StringComparer.Ordinal);

    public int Drawn { get; private set; }

    public Task<string> NextAsync(string kind, string prefix, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        Drawn++;
        _counters[kind] = _counters.GetValueOrDefault(kind) + 1;
        return Task.FromResult($"{prefix}-{nowUtc.Year}-{_counters[kind]:00000}");
    }
}

internal sealed class CountingUnitOfWork : IUnitOfWork
{
    public int Commits { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        Commits++;
        return Task.FromResult(1);
    }
}

internal sealed class FakePrivileges : IUserPrivileges
{
    public SpecialRights SpecialRights { get; set; } = SpecialRights.All;

    public IReadOnlySet<string> PermissionKeys { get; } = new HashSet<string>(StringComparer.Ordinal);

    public IReadOnlyList<ResultRight> ResultRights { get; } = [];

    public bool Has(string permissionKey) => true;

    public bool CanOnResults(ResultAction action, Guid clinicalGroupId, Guid branchId) => false;
}

internal sealed class FakeUser : ICurrentUser
{
    public bool IsPrimaryAdministrator { get; set; }
    public bool IsAuthenticated => UserId is not null;

    public Guid? UserId { get; set; }

    public Guid? SessionId => null;

    public string? DisplayName => "Cashier One";

    public Guid? BranchId { get; set; }
}

internal sealed class FixedBillingClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
}

internal sealed class FakeBillingSettings : ITenantSettings
{
    public ValueTask<T> GetAsync<T>(SettingDescriptor<T> descriptor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return ValueTask.FromResult(descriptor.Default);
    }

    public ValueTask<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyDictionary<string, string>>(SettingKeys.All.ToDictionary(setting => setting.Key, setting => setting.DefaultValue));
}
