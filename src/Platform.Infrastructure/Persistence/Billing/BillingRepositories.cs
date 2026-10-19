using Microsoft.EntityFrameworkCore;
using Platform.Application.Billing;
using Platform.Contracts.Billing;
using Platform.Contracts.Common;
using Platform.Domain.Billing;

namespace Platform.Infrastructure.Persistence.Billing;

/// <summary>Visit accounts (payments and refunds auto-included) and the payments and refunds registers.</summary>
internal sealed class VisitAccountRepository(AppDbContext context) : Repository<VisitAccount>(context), IVisitAccountRepository
{
    public async Task<VisitAccount?> GetByPaymentIdForUpdateAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var visitId = await Context.Set<Payment>().AsNoTracking()
            .Where(payment => payment.Id == paymentId)
            .Select(payment => (Guid?)payment.VisitId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return visitId is { } id ? await GetForUpdateAsync(id, cancellationToken).ConfigureAwait(false) : null;
    }

    public async Task<IReadOnlyDictionary<Guid, VisitAccount>> GetManyForUpdateAsync(IReadOnlyCollection<Guid> visitIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visitIds);
        if (visitIds.Count == 0)
        {
            return new Dictionary<Guid, VisitAccount>();
        }

        var ids = visitIds.Distinct().ToList();
        var accounts = await Set.Where(account => ids.Contains(account.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return accounts.ToDictionary(account => account.Id);
    }

    public async Task<Paged<PaymentListItem>> ListPaymentsAsync(PaymentFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Filter(filter);
        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await Project(Newest(query).Skip(page.Skip).Take(page.PageSize)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Paged<PaymentListItem>(rows.Select(ToItem).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<IReadOnlyList<PaymentListItem>> SearchPaymentsAsync(PaymentFilter filter, int maxRows, CancellationToken cancellationToken)
    {
        var rows = await Project(Newest(Filter(filter)).Take(maxRows)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(ToItem).ToList();
    }

    public async Task<PaymentListItem?> GetPaymentAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var row = await Project(Context.Set<Payment>().AsNoTracking().Where(payment => payment.Id == paymentId)).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return row is null ? null : ToItem(row);
    }

    public async Task<Paged<RefundListItem>> ListRefundsAsync(RefundFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Filter(filter);
        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await Project(Newest(query).Skip(page.Skip).Take(page.PageSize)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Paged<RefundListItem>(rows.Select(ToItem).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<IReadOnlyList<RefundListItem>> SearchRefundsAsync(RefundFilter filter, int maxRows, CancellationToken cancellationToken)
    {
        var rows = await Project(Newest(Filter(filter)).Take(maxRows)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(ToItem).ToList();
    }

    public async Task<RefundListItem?> GetRefundAsync(Guid refundId, CancellationToken cancellationToken)
    {
        var row = await Project(Context.Set<Refund>().AsNoTracking().Where(refund => refund.Id == refundId)).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return row is null ? null : ToItem(row);
    }

    private IQueryable<Payment> Filter(PaymentFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Context.Set<Payment>().AsNoTracking();
        if (filter.VisitId is { } visitId)
        {
            query = query.Where(payment => payment.VisitId == visitId);
        }

        if (filter.FromUtc is { } from)
        {
            query = query.Where(payment => payment.PaidAtUtc >= from);
        }

        if (filter.ToUtc is { } to)
        {
            query = query.Where(payment => payment.PaidAtUtc < to);
        }

        if (filter.TreasuryId is { } treasuryId)
        {
            query = query.Where(payment => payment.TreasuryId == treasuryId);
        }

        if (filter.CashierUserId is { } cashierUserId)
        {
            query = query.Where(payment => payment.CashierUserId == cashierUserId);
        }

        if (filter.Method is { } method)
        {
            query = query.Where(payment => payment.Method == method);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(payment => payment.Status == status);
        }

        return query;
    }

    private IQueryable<Refund> Filter(RefundFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Context.Set<Refund>().AsNoTracking();
        if (filter.VisitId is { } visitId)
        {
            query = query.Where(refund => refund.VisitId == visitId);
        }

        if (filter.FromUtc is { } from)
        {
            query = query.Where(refund => refund.RefundedAtUtc >= from);
        }

        if (filter.ToUtc is { } to)
        {
            query = query.Where(refund => refund.RefundedAtUtc < to);
        }

        if (filter.TreasuryId is { } treasuryId)
        {
            query = query.Where(refund => refund.TreasuryId == treasuryId);
        }

        if (filter.CashierUserId is { } cashierUserId)
        {
            query = query.Where(refund => refund.CashierUserId == cashierUserId);
        }

        if (filter.Method is { } method)
        {
            query = query.Where(refund => refund.Method == method);
        }

        return query;
    }

    private static IQueryable<Payment> Newest(IQueryable<Payment> query) => query.OrderByDescending(payment => payment.PaidAtUtc).ThenByDescending(payment => payment.Id);

    private static IQueryable<Refund> Newest(IQueryable<Refund> query) => query.OrderByDescending(refund => refund.RefundedAtUtc).ThenByDescending(refund => refund.Id);

    /// <summary>Joins the treasury and the user names, and marks a payment frozen when a closed period covers it (INV-BIL-006).</summary>
    private IQueryable<PaymentRow> Project(IQueryable<Payment> payments) =>
        from payment in payments
        join treasury in Context.Treasuries.AsNoTracking() on payment.TreasuryId equals treasury.Id
        join cashier in Context.PlatformUsers.AsNoTracking() on payment.CashierUserId equals cashier.Id
        from voider in Context.PlatformUsers.AsNoTracking().Where(user => user.Id == payment.VoidedByUserId).DefaultIfEmpty()
        from definition in Context.Set<PaymentMethodDefinition>().AsNoTracking().Where(candidate => candidate.Code == payment.MethodCode).DefaultIfEmpty()
        select new PaymentRow
        {
            Id = payment.Id,
            ReceiptNumber = payment.ReceiptNumber,
            VisitId = payment.VisitId,
            Amount = payment.Amount,
            Method = payment.Method,
            MethodCode = payment.MethodCode,
            MethodNameEn = definition == null ? null : definition.Name.En,
            MethodNameAr = definition == null ? null : definition.Name.Ar,
            Reference = payment.Reference,
            TreasuryId = payment.TreasuryId,
            TreasuryCode = treasury.Code,
            TreasuryNameEn = treasury.Name.En,
            TreasuryNameAr = treasury.Name.Ar,
            CashierUserId = payment.CashierUserId,
            CashierName = cashier.DisplayName,
            BranchId = payment.BranchId,
            DeviceId = payment.DeviceId,
            BatchId = payment.BatchId,
            PaidAtUtc = payment.PaidAtUtc,
            Status = payment.Status,
            IsFrozen = Context.Set<CashClosing>().Any(closing =>
                closing.TreasuryId == payment.TreasuryId
                && (closing.CashierUserId == null || closing.CashierUserId == payment.CashierUserId)
                && payment.PaidAtUtc >= closing.PeriodFromUtc
                && payment.PaidAtUtc < closing.PeriodToUtc),
            VoidReason = payment.VoidReason,
            VoidedByName = voider == null ? null : voider.DisplayName,
            VoidedAtUtc = payment.VoidedAtUtc,
        };

    private IQueryable<RefundRow> Project(IQueryable<Refund> refunds) =>
        from refund in refunds
        join treasury in Context.Treasuries.AsNoTracking() on refund.TreasuryId equals treasury.Id
        join cashier in Context.PlatformUsers.AsNoTracking() on refund.CashierUserId equals cashier.Id
        from definition in Context.Set<PaymentMethodDefinition>().AsNoTracking().Where(candidate => candidate.Code == refund.MethodCode).DefaultIfEmpty()
        select new RefundRow
        {
            Id = refund.Id,
            Number = refund.Number,
            VisitId = refund.VisitId,
            Amount = refund.Amount,
            Method = refund.Method,
            MethodCode = refund.MethodCode,
            MethodNameEn = definition == null ? null : definition.Name.En,
            MethodNameAr = definition == null ? null : definition.Name.Ar,
            Reference = refund.Reference,
            ReasonId = refund.ReasonId,
            Note = refund.Note,
            TreasuryId = refund.TreasuryId,
            TreasuryCode = treasury.Code,
            TreasuryNameEn = treasury.Name.En,
            TreasuryNameAr = treasury.Name.Ar,
            CashierUserId = refund.CashierUserId,
            CashierName = cashier.DisplayName,
            BranchId = refund.BranchId,
            DeviceId = refund.DeviceId,
            RefundedAtUtc = refund.RefundedAtUtc,
        };

    private static PaymentListItem ToItem(PaymentRow row) => new(
        row.Id, row.ReceiptNumber, row.VisitId, null, row.Amount, row.Method.ToString(), row.Reference, row.TreasuryId, row.TreasuryCode, row.TreasuryNameEn, row.TreasuryNameAr,
        row.CashierUserId, row.CashierName, row.BranchId, row.DeviceId, row.BatchId, row.PaidAtUtc, row.Status.ToString(), row.IsFrozen, row.VoidReason, row.VoidedByName, row.VoidedAtUtc,
        row.MethodCode, row.MethodNameEn, row.MethodNameAr);

    private static RefundListItem ToItem(RefundRow row) => new(
        row.Id, row.Number, row.VisitId, null, row.Amount, row.Method.ToString(), row.Reference, row.ReasonId, null, null, row.Note, row.TreasuryId, row.TreasuryCode,
        row.TreasuryNameEn, row.TreasuryNameAr, row.CashierUserId, row.CashierName, row.BranchId, row.DeviceId, row.RefundedAtUtc,
        row.MethodCode, row.MethodNameEn, row.MethodNameAr);

    private sealed class PaymentRow
    {
        public Guid Id { get; init; }

        public string ReceiptNumber { get; init; } = string.Empty;

        public Guid VisitId { get; init; }

        public decimal Amount { get; init; }

        public PaymentMethod Method { get; init; }

        public string? MethodCode { get; init; }

        public string? MethodNameEn { get; init; }

        public string? MethodNameAr { get; init; }

        public string? Reference { get; init; }

        public Guid TreasuryId { get; init; }

        public string TreasuryCode { get; init; } = string.Empty;

        public string TreasuryNameEn { get; init; } = string.Empty;

        public string TreasuryNameAr { get; init; } = string.Empty;

        public Guid CashierUserId { get; init; }

        public string CashierName { get; init; } = string.Empty;

        public Guid BranchId { get; init; }

        public string DeviceId { get; init; } = string.Empty;

        public Guid? BatchId { get; init; }

        public DateTimeOffset PaidAtUtc { get; init; }

        public PaymentStatus Status { get; init; }

        public bool IsFrozen { get; init; }

        public string? VoidReason { get; init; }

        public string? VoidedByName { get; init; }

        public DateTimeOffset? VoidedAtUtc { get; init; }
    }

    private sealed class RefundRow
    {
        public Guid Id { get; init; }

        public string Number { get; init; } = string.Empty;

        public Guid VisitId { get; init; }

        public decimal Amount { get; init; }

        public PaymentMethod Method { get; init; }

        public string? MethodCode { get; init; }

        public string? MethodNameEn { get; init; }

        public string? MethodNameAr { get; init; }

        public string? Reference { get; init; }

        public Guid ReasonId { get; init; }

        public string? Note { get; init; }

        public Guid TreasuryId { get; init; }

        public string TreasuryCode { get; init; } = string.Empty;

        public string TreasuryNameEn { get; init; } = string.Empty;

        public string TreasuryNameAr { get; init; } = string.Empty;

        public Guid CashierUserId { get; init; }

        public string CashierName { get; init; } = string.Empty;

        public Guid BranchId { get; init; }

        public string DeviceId { get; init; } = string.Empty;

        public DateTimeOffset RefundedAtUtc { get; init; }
    }
}

/// <summary>Closed periods: overlap and cover checks (INV-BIL-005/006) and the closings register.</summary>
internal sealed class CashClosingRepository(AppDbContext context) : Repository<CashClosing>(context), ICashClosingRepository
{
    public Task<bool> OverlapsAsync(Guid treasuryId, Guid? cashierUserId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken) =>
        Set.AnyAsync(
            closing => closing.TreasuryId == treasuryId
                       && (closing.CashierUserId == null || cashierUserId == null || closing.CashierUserId == cashierUserId)
                       && fromUtc < closing.PeriodToUtc
                       && closing.PeriodFromUtc < toUtc,
            cancellationToken);

    public Task<bool> CoversAsync(Guid treasuryId, Guid cashierUserId, DateTimeOffset atUtc, CancellationToken cancellationToken) =>
        Set.AnyAsync(
            closing => closing.TreasuryId == treasuryId
                       && (closing.CashierUserId == null || closing.CashierUserId == cashierUserId)
                       && atUtc >= closing.PeriodFromUtc
                       && atUtc < closing.PeriodToUtc,
            cancellationToken);

    public async Task<Paged<CashClosingListItem>> ListAsync(CashClosingFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Filter(filter);
        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await Project(Newest(query).Skip(page.Skip).Take(page.PageSize)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Paged<CashClosingListItem>(rows.Select(row => row.Item()).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<IReadOnlyList<CashClosingListItem>> SearchAsync(CashClosingFilter filter, int maxRows, CancellationToken cancellationToken)
    {
        var rows = await Project(Newest(Filter(filter)).Take(maxRows)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(row => row.Item()).ToList();
    }

    public async Task<CashClosingRecord?> GetRecordAsync(Guid closingId, CancellationToken cancellationToken)
    {
        var row = await Project(Set.AsNoTracking().Where(closing => closing.Id == closingId)).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return row is null ? null : new CashClosingRecord(row.Item(), row.Totals, row.Note, row.BranchId);
    }

    private IQueryable<CashClosing> Filter(CashClosingFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking();
        if (filter.TreasuryId is { } treasuryId)
        {
            query = query.Where(closing => closing.TreasuryId == treasuryId);
        }

        if (filter.CashierUserId is { } cashierUserId)
        {
            query = query.Where(closing => closing.CashierUserId == cashierUserId);
        }

        if (filter.FromUtc is { } from)
        {
            query = query.Where(closing => closing.PeriodToUtc > from);
        }

        if (filter.ToUtc is { } to)
        {
            query = query.Where(closing => closing.PeriodFromUtc < to);
        }

        return query;
    }

    private static IQueryable<CashClosing> Newest(IQueryable<CashClosing> query) => query.OrderByDescending(closing => closing.ClosedAtUtc).ThenByDescending(closing => closing.Id);

    private IQueryable<ClosingRow> Project(IQueryable<CashClosing> closings) =>
        from closing in closings
        join treasury in Context.Treasuries.AsNoTracking() on closing.TreasuryId equals treasury.Id
        join closer in Context.PlatformUsers.AsNoTracking() on closing.ClosedByUserId equals closer.Id
        from cashier in Context.PlatformUsers.AsNoTracking().Where(user => user.Id == closing.CashierUserId).DefaultIfEmpty()
        select new ClosingRow
        {
            Id = closing.Id,
            Number = closing.Number,
            TreasuryId = closing.TreasuryId,
            TreasuryCode = treasury.Code,
            TreasuryNameEn = treasury.Name.En,
            TreasuryNameAr = treasury.Name.Ar,
            BranchId = closing.BranchId,
            CashierUserId = closing.CashierUserId,
            CashierName = cashier == null ? null : cashier.DisplayName,
            PeriodFromUtc = closing.PeriodFromUtc,
            PeriodToUtc = closing.PeriodToUtc,
            Totals = closing.Totals,
            ExpectedCash = closing.ExpectedCash,
            CountedCash = closing.CountedCash,
            Difference = closing.Difference,
            Note = closing.Note,
            ClosedByUserId = closing.ClosedByUserId,
            ClosedByName = closer.DisplayName,
            ClosedAtUtc = closing.ClosedAtUtc,
        };

    private sealed class ClosingRow
    {
        public Guid Id { get; init; }

        public string Number { get; init; } = string.Empty;

        public Guid TreasuryId { get; init; }

        public string TreasuryCode { get; init; } = string.Empty;

        public string TreasuryNameEn { get; init; } = string.Empty;

        public string TreasuryNameAr { get; init; } = string.Empty;

        public Guid BranchId { get; init; }

        public Guid? CashierUserId { get; init; }

        public string? CashierName { get; init; }

        public DateTimeOffset PeriodFromUtc { get; init; }

        public DateTimeOffset PeriodToUtc { get; init; }

        public ClosingTotals Totals { get; init; } = ClosingTotals.Empty;

        public decimal ExpectedCash { get; init; }

        public decimal? CountedCash { get; init; }

        public decimal? Difference { get; init; }

        public string? Note { get; init; }

        public Guid ClosedByUserId { get; init; }

        public string ClosedByName { get; init; } = string.Empty;

        public DateTimeOffset ClosedAtUtc { get; init; }

        public CashClosingListItem Item() => new(
            Id, Number, TreasuryId, TreasuryCode, TreasuryNameEn, TreasuryNameAr, CashierUserId, CashierName, PeriodFromUtc, PeriodToUtc,
            Totals.TotalPayments, Totals.TotalRefunds, ExpectedCash, CountedCash, Difference, ClosedByUserId, ClosedByName, ClosedAtUtc);
    }
}
