using Microsoft.EntityFrameworkCore;
using Platform.Application.Billing;
using Platform.Application.Registration;
using Platform.Contracts.Billing;
using Platform.Domain.Billing;
using Platform.Domain.Treasury;

namespace Platform.Infrastructure.Persistence.Billing;

// Read adapters of the Billing module. Tables of Registration (same wave) are read with raw SQL on the agreed columns of
// MODULES.md §5 (app.visits, app.visit_lines, app.batches), never through its entity types (MODULE-PATTERN §3 "Same-wave
// lookups"). Every statement names the tenant explicitly besides RLS; statements are constants, never built from input.

/// <summary>
/// BR-14 for Registration and Billing's own documents: <c>remaining = patient_share_total − Σ posted payments + Σ refunds</c>
/// over <c>app.visits</c>, <c>app.payments</c> and <c>app.refunds</c> (the definition of MODULES.md §5).
/// </summary>
internal sealed class VisitBalanceLookup(AppDbContext context) : IVisitBalanceLookup
{
    public const int MaxIds = 1000;

    private const string Sql = """
        SELECT v.id AS visit_id,
               v.patient_share_total,
               COALESCE((SELECT SUM(p.amount) FROM app.payments p WHERE p.tenant_id = v.tenant_id AND p.visit_id = v.id AND p.status = 'Posted'), 0) AS paid,
               COALESCE((SELECT SUM(r.amount) FROM app.refunds r WHERE r.tenant_id = v.tenant_id AND r.visit_id = v.id), 0) AS refunded
        FROM app.visits v
        WHERE v.tenant_id = {0} AND v.id = ANY({1})
        """;

    public async Task<VisitBalance?> GetAsync(Guid visitId, CancellationToken cancellationToken)
    {
        var balances = await GetManyAsync([visitId], cancellationToken).ConfigureAwait(false);
        return balances.TryGetValue(visitId, out var balance) ? balance : null;
    }

    public async Task<IReadOnlyDictionary<Guid, VisitBalance>> GetManyAsync(IReadOnlyCollection<Guid> visitIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visitIds);
        var result = new Dictionary<Guid, VisitBalance>();
        foreach (var chunk in visitIds.Where(id => id != Guid.Empty).Distinct().Chunk(MaxIds))
        {
            var rows = await context.Database.SqlQueryRaw<BalanceRow>(Sql, context.CurrentTenantIdOrNil, chunk).ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var row in rows)
            {
                var figures = new VisitBalanceFigures(row.PatientShareTotal, row.Paid, row.Refunded);
                result[row.VisitId] = new VisitBalance(row.VisitId, row.PatientShareTotal, row.Paid, row.Refunded, figures.Remaining, figures.Refundable);
            }
        }

        return result;
    }

    private sealed class BalanceRow
    {
        public Guid VisitId { get; set; }

        public decimal PatientShareTotal { get; set; }

        public decimal Paid { get; set; }

        public decimal Refunded { get; set; }
    }
}

/// <summary>Registration's visits, billed lines and batches through the agreed wave-2 columns (MODULES.md §5).</summary>
internal sealed class BillableVisitLookup(AppDbContext context) : IBillableVisitLookup
{
    private const string VisitColumns = """
        SELECT v.id, v.accession, v.patient_id, v.branch_id, v.payer_id, v.contract_tier_id, v.batch_id, v.status, v.registered_at_utc,
               v.total_list_price, v.discount_total, v.patient_share_total, v.payer_share_total,
               b.number AS batch_number, b.kind AS batch_kind
        FROM app.visits v
        LEFT JOIN app.batches b ON b.tenant_id = v.tenant_id AND b.id = v.batch_id
        """;

    private const string ById = VisitColumns + " WHERE v.tenant_id = {0} AND v.id = {1}";
    private const string ByAccession = VisitColumns + " WHERE v.tenant_id = {0} AND v.accession IN ({1}, {2})";
    private const string ByIds = VisitColumns + " WHERE v.tenant_id = {0} AND v.id = ANY({1})";
    private const string ByBatch = VisitColumns + " WHERE v.tenant_id = {0} AND v.batch_id = {1} ORDER BY v.accession";

    private const string Lines = """
        SELECT l.id, l.visit_id, l.service_id, l.status, l.list_price, l.discount, l.patient_share, l.payer_share
        FROM app.visit_lines l
        WHERE l.tenant_id = {0} AND l.visit_id = {1} AND l.status <> 'Cancelled'
        ORDER BY l.id
        """;

    private const string BatchColumns = "SELECT b.id, b.number, b.kind, b.payer_id, b.contract_tier_id, b.sending_lab_id FROM app.batches b";
    private const string BatchById = BatchColumns + " WHERE b.tenant_id = {0} AND b.id = {1}";
    private const string BatchByNumber = BatchColumns + " WHERE b.tenant_id = {0} AND b.number IN ({1}, {2})";

    public async Task<BillableVisit?> GetAsync(Guid visitId, CancellationToken cancellationToken)
    {
        var rows = await context.Database.SqlQueryRaw<VisitRow>(ById, context.CurrentTenantIdOrNil, visitId).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Count == 0 ? null : rows[0].ToVisit();
    }

    public async Task<BillableVisit?> GetByAccessionAsync(string accession, CancellationToken cancellationToken)
    {
        var typed = (accession ?? string.Empty).Trim();
        if (typed.Length == 0)
        {
            return null;
        }

        var rows = await context.Database.SqlQueryRaw<VisitRow>(ByAccession, context.CurrentTenantIdOrNil, typed, typed.ToUpperInvariant()).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Count == 0 ? null : rows[0].ToVisit();
    }

    public async Task<IReadOnlyDictionary<Guid, BillableVisit>> GetManyAsync(IReadOnlyCollection<Guid> visitIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visitIds);
        var result = new Dictionary<Guid, BillableVisit>();
        foreach (var chunk in visitIds.Where(id => id != Guid.Empty).Distinct().Chunk(VisitBalanceLookup.MaxIds))
        {
            var rows = await context.Database.SqlQueryRaw<VisitRow>(ByIds, context.CurrentTenantIdOrNil, chunk).ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var row in rows)
            {
                result[row.Id] = row.ToVisit();
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<BillableLine>> LinesAsync(Guid visitId, CancellationToken cancellationToken)
    {
        var rows = await context.Database.SqlQueryRaw<LineRow>(Lines, context.CurrentTenantIdOrNil, visitId).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(row => new BillableLine(row.Id, row.VisitId, row.ServiceId, row.Status, row.ListPrice, row.Discount, row.PatientShare, row.PayerShare)).ToList();
    }

    public async Task<BillableBatch?> GetBatchAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var rows = await context.Database.SqlQueryRaw<BatchRow>(BatchById, context.CurrentTenantIdOrNil, batchId).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Count == 0 ? null : rows[0].ToBatch();
    }

    public async Task<BillableBatch?> GetBatchByNumberAsync(string number, CancellationToken cancellationToken)
    {
        var typed = (number ?? string.Empty).Trim();
        if (typed.Length == 0)
        {
            return null;
        }

        var rows = await context.Database.SqlQueryRaw<BatchRow>(BatchByNumber, context.CurrentTenantIdOrNil, typed, typed.ToUpperInvariant()).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Count == 0 ? null : rows[0].ToBatch();
    }

    public async Task<IReadOnlyList<BillableVisit>> BatchMembersAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var rows = await context.Database.SqlQueryRaw<VisitRow>(ByBatch, context.CurrentTenantIdOrNil, batchId).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(row => row.ToVisit()).ToList();
    }

    private sealed class VisitRow
    {
        public Guid Id { get; set; }

        public string Accession { get; set; } = string.Empty;

        public Guid PatientId { get; set; }

        public Guid BranchId { get; set; }

        public Guid? PayerId { get; set; }

        public Guid? ContractTierId { get; set; }

        public Guid? BatchId { get; set; }

        public string Status { get; set; } = string.Empty;

        public DateTimeOffset RegisteredAtUtc { get; set; }

        public decimal TotalListPrice { get; set; }

        public decimal DiscountTotal { get; set; }

        public decimal PatientShareTotal { get; set; }

        public decimal PayerShareTotal { get; set; }

        public string? BatchNumber { get; set; }

        public string? BatchKind { get; set; }

        public BillableVisit ToVisit() => new(
            Id, Accession, PatientId, BranchId, PayerId, ContractTierId, BatchId, Status, RegisteredAtUtc,
            TotalListPrice, DiscountTotal, PatientShareTotal, PayerShareTotal, BatchNumber, BatchKind);
    }

    private sealed class LineRow
    {
        public Guid Id { get; set; }

        public Guid VisitId { get; set; }

        public Guid ServiceId { get; set; }

        public string Status { get; set; } = string.Empty;

        public decimal ListPrice { get; set; }

        public decimal Discount { get; set; }

        public decimal PatientShare { get; set; }

        public decimal PayerShare { get; set; }
    }

    private sealed class BatchRow
    {
        public Guid Id { get; set; }

        public string Number { get; set; } = string.Empty;

        public string Kind { get; set; } = string.Empty;

        public Guid? PayerId { get; set; }

        public Guid? ContractTierId { get; set; }

        public Guid? SendingLabId { get; set; }

        public BillableBatch ToBatch() => new(Id, Number, Kind, PayerId, ContractTierId, SendingLabId);
    }
}

/// <summary>
/// BR-27 over the Treasury module's tables (an earlier module: its entity types are used directly): a treasury is usable
/// by a user on a device while a live Approved access request exists for (user, treasury, device).
/// </summary>
internal sealed class TreasuryStationLookup(AppDbContext context) : ITreasuryStationLookup
{
    public Task<TreasuryStationView?> GetAsync(Guid treasuryId, Guid userId, string deviceId, CancellationToken cancellationToken) =>
        context.Treasuries.AsNoTracking()
            .Where(treasury => treasury.Id == treasuryId)
            .Select(treasury => new TreasuryStationView(
                treasury.Id,
                treasury.Code,
                treasury.Name.En,
                treasury.Name.Ar,
                treasury.BranchId,
                treasury.IsActive,
                context.TreasuryAccessRequests.Any(request =>
                    request.TreasuryId == treasury.Id
                    && request.RequestedByUserId == userId
                    && request.DeviceId == deviceId
                    && request.Status == TreasuryAccessStatus.Approved)))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<BillingTreasuryItem>> ApprovedAsync(Guid userId, string deviceId, Guid branchId, CancellationToken cancellationToken) =>
        await context.Treasuries.AsNoTracking()
            .Where(treasury => treasury.IsActive
                               && treasury.BranchId == branchId
                               && context.TreasuryAccessRequests.Any(request =>
                                   request.TreasuryId == treasury.Id
                                   && request.RequestedByUserId == userId
                                   && request.DeviceId == deviceId
                                   && request.Status == TreasuryAccessStatus.Approved))
            .OrderBy(treasury => treasury.Code)
            .Select(treasury => new BillingTreasuryItem(treasury.Id, treasury.Code, treasury.Name.En, treasury.Name.Ar, treasury.BranchId, treasury.IsActive))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<BillingTreasuryItem>> OfBranchAsync(Guid branchId, CancellationToken cancellationToken) =>
        await context.Treasuries.AsNoTracking()
            .Where(treasury => treasury.BranchId == branchId)
            .OrderBy(treasury => treasury.Code)
            .Select(treasury => new BillingTreasuryItem(treasury.Id, treasury.Code, treasury.Name.En, treasury.Name.Ar, treasury.BranchId, treasury.IsActive))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<BillingTreasuryItem?> GetTreasuryAsync(Guid treasuryId, CancellationToken cancellationToken) =>
        context.Treasuries.AsNoTracking()
            .Where(treasury => treasury.Id == treasuryId)
            .Select(treasury => new BillingTreasuryItem(treasury.Id, treasury.Code, treasury.Name.En, treasury.Name.Ar, treasury.BranchId, treasury.IsActive))
            .FirstOrDefaultAsync(cancellationToken);
}

/// <summary>The drawer figures of CP-30 (BR-16): sums per method of posted payments and refunds, the visits touched and their outstanding balance and discounts.</summary>
internal sealed class CashDrawerQueries(AppDbContext context) : ICashDrawerQueries
{
    private const string VisitFiguresSql = """
        SELECT COALESCE(SUM(GREATEST(x.share - x.paid + x.refunded, 0)), 0) AS outstanding_balance,
               COALESCE(SUM(x.discount), 0) AS discount_total,
               COUNT(*)::int AS visit_count
        FROM (
            SELECT v.patient_share_total AS share,
                   v.discount_total AS discount,
                   COALESCE((SELECT SUM(p.amount) FROM app.payments p WHERE p.tenant_id = v.tenant_id AND p.visit_id = v.id AND p.status = 'Posted'), 0) AS paid,
                   COALESCE((SELECT SUM(r.amount) FROM app.refunds r WHERE r.tenant_id = v.tenant_id AND r.visit_id = v.id), 0) AS refunded
            FROM app.visits v
            WHERE v.tenant_id = {0} AND v.id = ANY({1})
        ) x
        """;

    public async Task<ClosingTotals> TotalsAsync(DrawerScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var payments = Payments(scope).Where(payment => payment.Status == PaymentStatus.Posted);
        var refunds = Refunds(scope);

        var paymentSums = await payments.GroupBy(payment => payment.Method)
            .Select(group => new { Method = group.Key, Sum = group.Sum(payment => payment.Amount), Count = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var refundSums = await refunds.GroupBy(refund => refund.Method)
            .Select(group => new { Method = group.Key, Sum = group.Sum(refund => refund.Amount), Count = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var visitIds = await payments.Select(payment => payment.VisitId)
            .Union(refunds.Select(refund => refund.VisitId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var visits = new VisitFiguresRow();
        if (visitIds.Count > 0)
        {
            var rows = await context.Database.SqlQueryRaw<VisitFiguresRow>(VisitFiguresSql, context.CurrentTenantIdOrNil, visitIds.ToArray()).ToListAsync(cancellationToken).ConfigureAwait(false);
            visits = rows.Count == 0 ? visits : rows[0];
        }

        decimal Paid(PaymentMethod method) => paymentSums.Where(sum => sum.Method == method).Sum(sum => sum.Sum);
        decimal Returned(PaymentMethod method) => refundSums.Where(sum => sum.Method == method).Sum(sum => sum.Sum);

        return new ClosingTotals(
            Paid(PaymentMethod.Cash),
            Paid(PaymentMethod.Card),
            Paid(PaymentMethod.Pos),
            Paid(PaymentMethod.Transfer),
            paymentSums.Sum(sum => sum.Count),
            Returned(PaymentMethod.Cash),
            Returned(PaymentMethod.Card),
            Returned(PaymentMethod.Pos),
            Returned(PaymentMethod.Transfer),
            refundSums.Sum(sum => sum.Count),
            visits.OutstandingBalance,
            visits.DiscountTotal,
            visits.VisitCount);
    }

    public async Task<IReadOnlyList<DrawerMovement>> MovementsAsync(DrawerScope scope, int maxRows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var payments = await (
            from payment in Payments(scope).OrderBy(payment => payment.PaidAtUtc).Take(maxRows)
            join cashier in context.PlatformUsers.AsNoTracking() on payment.CashierUserId equals cashier.Id
            select new { payment.Id, payment.ReceiptNumber, payment.VisitId, payment.Method, payment.Amount, payment.PaidAtUtc, payment.CashierUserId, cashier.DisplayName, payment.Status })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var refunds = await (
            from refund in Refunds(scope).OrderBy(refund => refund.RefundedAtUtc).Take(maxRows)
            join cashier in context.PlatformUsers.AsNoTracking() on refund.CashierUserId equals cashier.Id
            select new { refund.Id, refund.Number, refund.VisitId, refund.Method, refund.Amount, refund.RefundedAtUtc, refund.CashierUserId, cashier.DisplayName })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return payments
            .Select(row => new DrawerMovement("Payment", row.Id, row.ReceiptNumber, row.VisitId, null, row.Method.ToString(), row.Amount, row.PaidAtUtc, row.CashierUserId, row.DisplayName, row.Status.ToString()))
            .Concat(refunds.Select(row => new DrawerMovement("Refund", row.Id, row.Number, row.VisitId, null, row.Method.ToString(), row.Amount, row.RefundedAtUtc, row.CashierUserId, row.DisplayName, nameof(PaymentStatus.Posted))))
            .OrderBy(movement => movement.AtUtc)
            .ThenBy(movement => movement.Number, StringComparer.Ordinal)
            .Take(maxRows)
            .ToList();
    }

    public async Task<IReadOnlyList<DrawerCashier>> CashiersAsync(Guid treasuryId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        var scope = new DrawerScope(treasuryId, null, fromUtc, toUtc);
        var ids = Payments(scope).Select(payment => payment.CashierUserId).Union(Refunds(scope).Select(refund => refund.CashierUserId));
        return await context.PlatformUsers.AsNoTracking()
            .Where(user => ids.Contains(user.Id))
            .OrderBy(user => user.DisplayName)
            .Select(user => new DrawerCashier(user.Id, user.DisplayName))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> UserNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await context.PlatformUsers.AsNoTracking()
            .Where(user => ids.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken)
            .ConfigureAwait(false);
    }

    private IQueryable<Payment> Payments(DrawerScope scope)
    {
        var query = context.Set<Payment>().AsNoTracking()
            .Where(payment => payment.TreasuryId == scope.TreasuryId && payment.PaidAtUtc >= scope.FromUtc && payment.PaidAtUtc < scope.ToUtc);
        return scope.CashierUserId is { } cashier ? query.Where(payment => payment.CashierUserId == cashier) : query;
    }

    private IQueryable<Refund> Refunds(DrawerScope scope)
    {
        var query = context.Set<Refund>().AsNoTracking()
            .Where(refund => refund.TreasuryId == scope.TreasuryId && refund.RefundedAtUtc >= scope.FromUtc && refund.RefundedAtUtc < scope.ToUtc);
        return scope.CashierUserId is { } cashier ? query.Where(refund => refund.CashierUserId == cashier) : query;
    }

    private sealed class VisitFiguresRow
    {
        public decimal OutstandingBalance { get; set; }

        public decimal DiscountTotal { get; set; }

        public int VisitCount { get; set; }
    }
}

/// <summary>
/// The collections report (change request 2026-10-13, item 2): one grouping query over the union of the posted payments
/// and the refunds of the scope, summed per (branch, cashier, method); the domain regroups the cells by the chosen key.
/// </summary>
internal sealed class CollectionsReportQueries(AppDbContext context) : ICollectionsReportQueries
{
    public async Task<IReadOnlyList<CollectionsCell>> CellsAsync(CollectionsScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var payments = context.Set<Payment>().AsNoTracking()
            .Where(payment => payment.Status == PaymentStatus.Posted && payment.PaidAtUtc >= scope.FromUtc && payment.PaidAtUtc < scope.ToUtc);
        var refunds = context.Set<Refund>().AsNoTracking()
            .Where(refund => refund.RefundedAtUtc >= scope.FromUtc && refund.RefundedAtUtc < scope.ToUtc);
        if (scope.CashierUserId is { } cashier)
        {
            payments = payments.Where(payment => payment.CashierUserId == cashier);
            refunds = refunds.Where(refund => refund.CashierUserId == cashier);
        }

        if (scope.BranchId is { } branch)
        {
            payments = payments.Where(payment => payment.BranchId == branch);
            refunds = refunds.Where(refund => refund.BranchId == branch);
        }

        var movements = payments
            .Select(payment => new Movement { BranchId = payment.BranchId, CashierUserId = payment.CashierUserId, Method = payment.Method, Payment = payment.Amount, Refund = 0m, PaymentCount = 1, RefundCount = 0 })
            .Concat(refunds.Select(refund => new Movement { BranchId = refund.BranchId, CashierUserId = refund.CashierUserId, Method = refund.Method, Payment = 0m, Refund = refund.Amount, PaymentCount = 0, RefundCount = 1 }));

        var cells = await movements
            .GroupBy(movement => new { movement.BranchId, movement.CashierUserId, movement.Method })
            .Select(group => new
            {
                group.Key.BranchId,
                group.Key.CashierUserId,
                group.Key.Method,
                Payments = group.Sum(movement => movement.Payment),
                PaymentCount = group.Sum(movement => movement.PaymentCount),
                Refunds = group.Sum(movement => movement.Refund),
                RefundCount = group.Sum(movement => movement.RefundCount),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return cells.Select(cell => new CollectionsCell(cell.BranchId, cell.CashierUserId, cell.Method, cell.Payments, cell.PaymentCount, cell.Refunds, cell.RefundCount)).ToList();
    }

    private sealed class Movement
    {
        public Guid BranchId { get; set; }

        public Guid CashierUserId { get; set; }

        public PaymentMethod Method { get; set; }

        public decimal Payment { get; set; }

        public decimal Refund { get; set; }

        public int PaymentCount { get; set; }

        public int RefundCount { get; set; }
    }
}
