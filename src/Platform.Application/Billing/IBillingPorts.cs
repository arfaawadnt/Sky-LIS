using Platform.Application.Abstractions.Persistence;
using Platform.Contracts.Billing;
using Platform.Contracts.Common;
using Platform.Domain.Billing;

namespace Platform.Application.Billing;

// Write side, projections and read ports of the Billing module (intake LIS-INT-BIL-001). Every read is bounded to the
// current tenant (query filter + RLS); adapters live in Platform.Infrastructure/Persistence/Billing and register by
// convention. Ports over tables of other modules read them directly (MODULES.md §1), never their repositories.

public sealed record PaymentFilter(Guid? VisitId, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc, Guid? BranchId, Guid? CashierUserId, PaymentMethod? Method, PaymentStatus? Status);

public sealed record RefundFilter(Guid? VisitId, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc, Guid? BranchId, Guid? CashierUserId, PaymentMethod? Method);

public sealed record CashClosingFilter(Guid? BranchId, Guid? CashierUserId, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc);

/// <summary>The account of a visit with its payments and refunds; register projections join user names and method definitions.</summary>
public interface IVisitAccountRepository : IRepository<VisitAccount>
{
    /// <summary>The account holding the payment (tracked, with its payments and refunds), or <see langword="null"/>.</summary>
    Task<VisitAccount?> GetByPaymentIdForUpdateAsync(Guid paymentId, CancellationToken cancellationToken);

    /// <summary>The existing accounts of the visits (tracked), keyed by visit id; visits without an account are absent.</summary>
    Task<IReadOnlyDictionary<Guid, VisitAccount>> GetManyForUpdateAsync(IReadOnlyCollection<Guid> visitIds, CancellationToken cancellationToken);

    /// <summary>Payments newest first; <see cref="PaymentListItem.Accession"/> is left empty (the handler fills it from Registration).</summary>
    Task<Paged<PaymentListItem>> ListPaymentsAsync(PaymentFilter filter, PageRequest page, CancellationToken cancellationToken);

    /// <summary>Up to <paramref name="maxRows"/> payments newest first (exports, a visit's own payments).</summary>
    Task<IReadOnlyList<PaymentListItem>> SearchPaymentsAsync(PaymentFilter filter, int maxRows, CancellationToken cancellationToken);

    Task<PaymentListItem?> GetPaymentAsync(Guid paymentId, CancellationToken cancellationToken);

    Task<Paged<RefundListItem>> ListRefundsAsync(RefundFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<IReadOnlyList<RefundListItem>> SearchRefundsAsync(RefundFilter filter, int maxRows, CancellationToken cancellationToken);

    Task<RefundListItem?> GetRefundAsync(Guid refundId, CancellationToken cancellationToken);
}

/// <summary>A closing with everything its sheet shows.</summary>
/// <param name="LegacyTreasuryId">The treasury of a closing made before LIS-CR-004 (its sheet lists that drawer's movements only); null otherwise.</param>
public sealed record CashClosingRecord(CashClosingListItem Item, ClosingTotals Totals, string? Note, Guid BranchId, Guid? LegacyTreasuryId = null);

public interface ICashClosingRepository : IRepository<CashClosing>
{
    /// <summary>INV-BIL-005: a closed period of the branch intersects <c>[from, to)</c> for an intersecting cashier scope.</summary>
    Task<bool> OverlapsAsync(Guid branchId, Guid? cashierUserId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);

    /// <summary>INV-BIL-006: a closed period of the branch covers a movement of the cashier at <paramref name="atUtc"/>.</summary>
    Task<bool> CoversAsync(Guid branchId, Guid cashierUserId, DateTimeOffset atUtc, CancellationToken cancellationToken);

    Task<Paged<CashClosingListItem>> ListAsync(CashClosingFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<IReadOnlyList<CashClosingListItem>> SearchAsync(CashClosingFilter filter, int maxRows, CancellationToken cancellationToken);

    Task<CashClosingRecord?> GetRecordAsync(Guid closingId, CancellationToken cancellationToken);
}

/// <summary>The scope of a drawer: a branch, one cashier or all (<see langword="null"/>), and a half-open period (LIS-CR-004: the branch replaces the treasury).</summary>
/// <param name="LegacyTreasuryId">Narrows the scope to one treasury of the branch: only for the sheet of a closing made before LIS-CR-004.</param>
public sealed record DrawerScope(Guid BranchId, Guid? CashierUserId, DateTimeOffset FromUtc, DateTimeOffset ToUtc, Guid? LegacyTreasuryId = null);

/// <summary>The figures of a drawer (CP-30, BR-16) over <c>app.payments</c>, <c>app.refunds</c> and, for outstanding balances and discounts, <c>app.visits</c>.</summary>
public interface ICashDrawerQueries
{
    /// <summary>Posted payments and refunds per method, counts, and the outstanding balance and discounts of the visits touched.</summary>
    Task<ClosingTotals> TotalsAsync(DrawerScope scope, CancellationToken cancellationToken);

    /// <summary>Payments (posted and voided) and refunds of the scope in time order, at most <paramref name="maxRows"/>; accessions left empty.</summary>
    Task<IReadOnlyList<DrawerMovement>> MovementsAsync(DrawerScope scope, int maxRows, CancellationToken cancellationToken);

    /// <summary>The cashiers who moved money in the branch in the period.</summary>
    Task<IReadOnlyList<DrawerCashier>> CashiersAsync(Guid branchId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);

    /// <summary>Display names of platform users, keyed by id (unknown ids are absent).</summary>
    Task<IReadOnlyDictionary<Guid, string>> UserNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);
}

/// <summary>
/// The scope of the collections report: one cashier (<paramref name="CashierUserId"/>) or every cashier of a branch
/// (<paramref name="BranchId"/>), over a half-open UTC period of whole laboratory days.
/// </summary>
public sealed record CollectionsScope(Guid? CashierUserId, Guid? BranchId, DateTimeOffset FromUtc, DateTimeOffset ToUtc);

/// <summary>The collections report (change request 2026-10-13, item 2) over <c>app.payments</c> (Posted only) and <c>app.refunds</c>.</summary>
public interface ICollectionsReportQueries
{
    /// <summary>One cell per (branch, cashier, method) with the sums and counts; one grouping query, nothing else.</summary>
    Task<IReadOnlyList<CollectionsCell>> CellsAsync(CollectionsScope scope, CancellationToken cancellationToken);
}

/// <summary>A visit as Billing reads it from <c>app.visits</c> (+ its batch from <c>app.batches</c>), MODULES.md §5.</summary>
public sealed record BillableVisit(
    Guid VisitId,
    string Accession,
    Guid PatientId,
    Guid BranchId,
    Guid? PayerId,
    Guid? ContractTierId,
    Guid? BatchId,
    string Status,
    DateTimeOffset RegisteredAtUtc,
    decimal TotalListPrice,
    decimal DiscountTotal,
    decimal PatientShareTotal,
    decimal PayerShareTotal,
    string? BatchNumber,
    string? BatchKind)
{
    public const string LabToLabKind = "LabToLab";

    public bool BilledThroughClaims => string.Equals(BatchKind, LabToLabKind, StringComparison.Ordinal);

    public VisitCharge Charge => new(PatientShareTotal, BilledThroughClaims);
}

/// <summary>A billed line of a visit (<c>app.visit_lines</c>, Ordered or Reserved; Cancelled lines are not billed).</summary>
public sealed record BillableLine(Guid LineId, Guid VisitId, Guid ServiceId, string Status, decimal ListPrice, decimal Discount, decimal PatientShare, decimal PayerShare);

/// <summary>A batch of Registration (<c>app.batches</c>): kind Group or LabToLab.</summary>
public sealed record BillableBatch(Guid BatchId, string Number, string Kind, Guid? PayerId, Guid? ContractTierId, Guid? SendingLabId)
{
    public bool BilledThroughClaims => string.Equals(Kind, BillableVisit.LabToLabKind, StringComparison.Ordinal);
}

/// <summary>Registration's visits, lines and batches through the agreed wave-2 columns (raw SQL; MODULES.md §5).</summary>
public interface IBillableVisitLookup
{
    Task<BillableVisit?> GetAsync(Guid visitId, CancellationToken cancellationToken);

    /// <summary>Exact accession match (case-insensitive), or <see langword="null"/>.</summary>
    Task<BillableVisit?> GetByAccessionAsync(string accession, CancellationToken cancellationToken);

    /// <summary>The known visits among the ids, keyed by id.</summary>
    Task<IReadOnlyDictionary<Guid, BillableVisit>> GetManyAsync(IReadOnlyCollection<Guid> visitIds, CancellationToken cancellationToken);

    /// <summary>The billed lines of a visit in their order of creation.</summary>
    Task<IReadOnlyList<BillableLine>> LinesAsync(Guid visitId, CancellationToken cancellationToken);

    Task<BillableBatch?> GetBatchAsync(Guid batchId, CancellationToken cancellationToken);

    /// <summary>Exact batch number match (case-insensitive), or <see langword="null"/>.</summary>
    Task<BillableBatch?> GetBatchByNumberAsync(string number, CancellationToken cancellationToken);

    /// <summary>The member visits of a batch in accession order.</summary>
    Task<IReadOnlyList<BillableVisit>> BatchMembersAsync(Guid batchId, CancellationToken cancellationToken);
}

/// <summary>A catalogue reason of kind Refund (names may be unknown while Catalogue B's adapter is pending).</summary>
public sealed record RefundReasonView(Guid ReasonId, string? NameEn, string? NameAr);

/// <summary>Catalogue B reasons of kind Refund (CP-24 "reason mandatory"): active ones are accepted, history keeps its names.</summary>
public interface IRefundReasonLookup
{
    /// <summary>The reason when it exists, is active and is of kind Refund; otherwise <see langword="null"/> (BIL-016).</summary>
    Task<RefundReasonView?> GetActiveAsync(Guid reasonId, CancellationToken cancellationToken);

    /// <summary>Names of the known reasons whatever their status, keyed by id.</summary>
    Task<IReadOnlyDictionary<Guid, RefundReasonView>> GetManyAsync(IReadOnlyCollection<Guid> reasonIds, CancellationToken cancellationToken);
}

public sealed record PatientCaption(Guid PatientId, string? Number, string? Name);

public sealed record NamedReference(Guid Id, string? Code, string NameEn, string NameAr);

/// <summary>
/// Display names the documents and registers show, owned by other modules: the laboratory (tenant) and branch for the
/// letterhead, patients (number and name), services and payers. Missing names render as blanks, never as refusals.
/// </summary>
public interface IBillingDirectory
{
    Task<DocumentLetterhead> LetterheadAsync(Guid branchId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, PatientCaption>> PatientsAsync(IReadOnlyCollection<Guid> patientIds, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, NamedReference>> ServicesAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, NamedReference>> PayersAsync(IReadOnlyCollection<Guid> payerIds, CancellationToken cancellationToken);

    /// <summary>Bilingual names of the branches (<c>app.branches</c>) the collections report groups by, keyed by id.</summary>
    Task<IReadOnlyDictionary<Guid, NamedReference>> BranchesAsync(IReadOnlyCollection<Guid> branchIds, CancellationToken cancellationToken);
}
