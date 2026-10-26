using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Registration;
using Platform.Contracts.Common;
using Platform.Contracts.Registration;
using Platform.Domain.Patients;
using Platform.Domain.Registration;

namespace Platform.Infrastructure.Persistence.Registration;

/// <summary>Visits with their lines, tubes, answers and transfers; register projections. Patients (wave 1) are read for the name, number and mobile filters.</summary>
internal sealed class VisitRepository(AppDbContext context) : Repository<Visit>(context), IVisitRepository
{
    public override Task<Visit?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        WithChildren(Set).FirstOrDefaultAsync(visit => visit.Id == id, cancellationToken);

    public Task<Visit?> GetReadOnlyAsync(Guid id, CancellationToken cancellationToken) =>
        WithChildren(Set.AsNoTracking()).FirstOrDefaultAsync(visit => visit.Id == id, cancellationToken);

    public async Task<Guid?> FindIdByAccessionAsync(string accession, CancellationToken cancellationToken)
    {
        var typed = (accession ?? string.Empty).Trim();
        if (typed.Length == 0)
        {
            return null;
        }

        var upper = typed.ToUpperInvariant();
        return await Set.AsNoTracking().Where(visit => visit.Accession == typed || visit.Accession == upper).Select(visit => (Guid?)visit.Id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Visit>> ListByPatientForUpdateAsync(Guid patientId, CancellationToken cancellationToken) =>
        await Set.Where(visit => visit.PatientId == patientId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<PriorTestOrder>> PriorTestOrdersAsync(Guid patientId, IReadOnlyCollection<Guid> testIds, DateTimeOffset sinceUtc, Guid? excludeVisitId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(testIds);
        var wanted = testIds.Distinct().ToArray();
        if (wanted.Length == 0)
        {
            return [];
        }

        var excluded = excludeVisitId ?? Guid.Empty;
        var rows = await (
            from line in Context.Set<VisitLine>().AsNoTracking()
            join visit in Set.AsNoTracking() on line.VisitId equals visit.Id
            where visit.PatientId == patientId
                  && visit.Id != excluded
                  && line.Status != VisitLineStatus.Cancelled
                  && line.OrderedAtUtc >= sinceUtc
                  && line.TestIds.Any(test => wanted.Contains(test))
            select new { visit.Id, visit.Accession, line.OrderedAtUtc, line.TestIds })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var set = wanted.ToHashSet();
        return [.. rows.SelectMany(row => row.TestIds.Where(set.Contains).Select(test => new PriorTestOrder(test, row.Id, row.Accession, row.OrderedAtUtc)))];
    }

    public async Task<Paged<VisitRow>> SearchAsync(VisitSearchFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Filtered(filter);
        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var visits = await query.OrderByDescending(visit => visit.RegisteredAtUtc).ThenByDescending(visit => visit.Id).Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Paged<VisitRow>(await RowsAsync(visits, cancellationToken).ConfigureAwait(false), page.Page, page.PageSize, total);
    }

    public async Task<IReadOnlyList<VisitRow>> SearchAllAsync(VisitSearchFilter filter, int limit, CancellationToken cancellationToken)
    {
        var visits = await Filtered(filter).OrderByDescending(visit => visit.RegisteredAtUtc).ThenByDescending(visit => visit.Id).Take(limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        return await RowsAsync(visits, cancellationToken).ConfigureAwait(false);
    }

    public Task<int> VisitOrdinalAsync(Guid patientId, DateTimeOffset registeredAtUtc, CancellationToken cancellationToken) =>
        Set.AsNoTracking().CountAsync(visit => visit.PatientId == patientId && visit.RegisteredAtUtc <= registeredAtUtc, cancellationToken);

    public async Task<IReadOnlyList<VisitRow>> ListByBatchAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var visits = await Set.AsNoTracking().Where(visit => visit.BatchId == batchId).OrderBy(visit => visit.Accession).ToListAsync(cancellationToken).ConfigureAwait(false);
        return await RowsAsync(visits, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Paged<CancelledLineRow>> CancelledLinesAsync(DateTimeOffset? fromUtc, DateTimeOffset? toUtc, Guid? branchId, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Cancelled(fromUtc, toUtc, branchId);
        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query.OrderByDescending(row => row.CancelledAtUtc).ThenBy(row => row.LineId).Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Paged<CancelledLineRow>(rows, page.Page, page.PageSize, total);
    }

    public async Task<IReadOnlyList<CancelledLineRow>> CancelledLinesAllAsync(DateTimeOffset? fromUtc, DateTimeOffset? toUtc, Guid? branchId, int limit, CancellationToken cancellationToken) =>
        await Cancelled(fromUtc, toUtc, branchId).OrderByDescending(row => row.CancelledAtUtc).ThenBy(row => row.LineId).Take(limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<Paged<TransferRow>> TransfersAsync(DateTimeOffset? fromUtc, DateTimeOffset? toUtc, VisitTransferKind? kind, Guid? visitId, PageRequest page, CancellationToken cancellationToken)
    {
        var transfers = Context.Set<VisitTransfer>().AsNoTracking();
        if (fromUtc is { } from)
        {
            transfers = transfers.Where(transfer => transfer.AtUtc >= from);
        }

        if (toUtc is { } to)
        {
            transfers = transfers.Where(transfer => transfer.AtUtc < to);
        }

        if (kind is { } wanted)
        {
            transfers = transfers.Where(transfer => transfer.Kind == wanted);
        }

        if (visitId is { } id)
        {
            transfers = transfers.Where(transfer => transfer.VisitId == id);
        }

        var query =
            from transfer in transfers
            join visit in Set.AsNoTracking() on transfer.VisitId equals visit.Id
            from line in Context.Set<VisitLine>().AsNoTracking().Where(line => line.Id == transfer.LineId).DefaultIfEmpty()
            select new TransferRow(
                transfer.Id,
                transfer.VisitId,
                visit.Accession,
                transfer.Kind,
                transfer.LineId,
                line == null ? null : line.ServiceCode,
                transfer.FromPayerId,
                transfer.FromContractTierId,
                transfer.ToPayerId,
                transfer.ToContractTierId,
                transfer.PatientShareBefore,
                transfer.PatientShareAfter,
                transfer.PayerShareBefore,
                transfer.PayerShareAfter,
                transfer.Reason,
                transfer.ActorUserId,
                transfer.AtUtc);

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query.OrderByDescending(row => row.AtUtc).ThenBy(row => row.Id).Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Paged<TransferRow>(rows, page.Page, page.PageSize, total);
    }

    private static IQueryable<Visit> WithChildren(IQueryable<Visit> query) =>
        query.Include(visit => visit.Lines).Include(visit => visit.Tubes).Include(visit => visit.PreparationAnswers).Include(visit => visit.Transfers).AsSplitQuery();

    private IQueryable<Visit> Filtered(VisitSearchFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking();
        if (filter.Accession is { } accession)
        {
            var pattern = "%" + Escape(accession.ToUpperInvariant()) + "%";
            query = query.Where(visit => EF.Functions.ILike(visit.Accession, pattern, "\\"));
        }

        if (filter.FromUtc is { } from)
        {
            query = query.Where(visit => visit.RegisteredAtUtc >= from);
        }

        if (filter.ToUtc is { } to)
        {
            query = query.Where(visit => visit.RegisteredAtUtc < to);
        }

        if (filter.BranchId is { } branchId)
        {
            query = query.Where(visit => visit.BranchId == branchId);
        }

        if (filter.PayerId is { } payerId)
        {
            query = query.Where(visit => visit.PayerId == payerId);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(visit => visit.Status == status);
        }

        if (filter.PatientId is { } patientId)
        {
            query = query.Where(visit => visit.PatientId == patientId || visit.OriginalPatientId == patientId);
        }

        if (filter.BatchId is { } batchId)
        {
            query = query.Where(visit => visit.BatchId == batchId);
        }

        var patients = Context.Set<Patient>().AsNoTracking();
        if (filter.PatientNumber is { } number)
        {
            var upper = number.ToUpperInvariant();
            query = query.Where(visit => patients.Any(patient => patient.Id == visit.PatientId && patient.Number.Contains(upper)));
        }

        var includeVip = filter.IncludeVip;
        if (filter.Name is { } name)
        {
            var tokens = PatientNameNormalizer.Tokens(name).Take(6).ToList();
            if (tokens.Count > 0)
            {
                Expression<Func<Patient, bool>> match = patient => includeVip || !patient.IsVip;
                foreach (var token in tokens)
                {
                    match = And(match, patient => patient.NameKey.Contains(token) || patient.NameArKey.Contains(token));
                }

                var matching = patients.Where(match).Select(patient => patient.Id);
                query = query.Where(visit => matching.Contains(visit.PatientId));
            }
        }

        if (filter.Mobile is { } mobile)
        {
            var digits = new string(Patient.FoldDigits(mobile).Where(char.IsAsciiDigit).ToArray());
            if (digits.Length > 0)
            {
                query = query.Where(visit => patients.Any(patient => patient.Id == visit.PatientId && (includeVip || !patient.IsVip) && patient.Mobile != null && patient.Mobile.Contains(digits)));
            }
        }

        return query;
    }

    private IQueryable<CancelledLineRow> Cancelled(DateTimeOffset? fromUtc, DateTimeOffset? toUtc, Guid? branchId)
    {
        var lines = Context.Set<VisitLine>().AsNoTracking().Where(line => line.Status == VisitLineStatus.Cancelled && line.Kind != VisitLineKind.PackageMember && line.Kind != VisitLineKind.FormulaOperand);   // members and formula operands follow their line
        if (fromUtc is { } from)
        {
            lines = lines.Where(line => line.CancelledAtUtc >= from);
        }

        if (toUtc is { } to)
        {
            lines = lines.Where(line => line.CancelledAtUtc < to);
        }

        var visits = Set.AsNoTracking();
        if (branchId is { } branch)
        {
            visits = visits.Where(visit => visit.BranchId == branch);
        }

        return from line in lines
               join visit in visits on line.VisitId equals visit.Id
               select new CancelledLineRow(
                   line.Id,
                   visit.Id,
                   visit.Accession,
                   visit.PatientId,
                   visit.BranchId,
                   line.ServiceCode,
                   line.ServiceNameEn,
                   line.ServiceNameAr,
                   line.ListPrice,
                   line.PatientShare,
                   line.PayerShare,
                   line.CancellationReason,
                   line.CancelledByUserId,
                   line.CancelledAtUtc);
    }

    private async Task<IReadOnlyList<VisitRow>> RowsAsync(List<Visit> visits, CancellationToken cancellationToken)
    {
        if (visits.Count == 0)
        {
            return [];
        }

        var ids = visits.Select(visit => visit.Id).ToArray();
        var lines = await Context.Set<VisitLine>().AsNoTracking()
            .Where(line => ids.Contains(line.VisitId) && line.Kind != VisitLineKind.PackageMember && line.Kind != VisitLineKind.FormulaOperand)
            .OrderBy(line => line.Sequence)
            .Select(line => new { line.VisitId, Chip = new VisitLineChip(line.Id, line.ServiceCode, line.ServiceNameEn, line.ServiceNameAr, line.Status.ToString(), line.Kind.ToString()) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var byVisit = lines.GroupBy(line => line.VisitId).ToDictionary(group => group.Key, group => group.Select(line => line.Chip).ToList());
        var outside = (await Context.Set<VisitTube>().AsNoTracking()
            .Where(tube => ids.Contains(tube.VisitId) && tube.CollectedOutside)
            .Select(tube => tube.VisitId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false)).ToHashSet();   // LIS-CR-008 C-02: derived from the tubes

        return [.. visits.Select(visit => new VisitRow(
            visit.Id,
            visit.Accession,
            visit.RegisteredAtUtc,
            visit.BranchId,
            visit.PatientId,
            visit.PayerId,
            visit.ContractTierId,
            visit.Status,
            visit.LocationType,
            visit.Urgent,
            visit.BatchId,
            outside.Contains(visit.Id),
            visit.TotalListPrice,
            visit.DiscountTotal,
            visit.PatientShareTotal,
            visit.PayerShareTotal,
            byVisit.GetValueOrDefault(visit.Id) ?? []))];
    }

    private static string Escape(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    private static Expression<Func<Patient, bool>> And(Expression<Func<Patient, bool>> left, Expression<Func<Patient, bool>> right)
    {
        var parameter = left.Parameters[0];
        var body = new Rebinder(right.Parameters[0], parameter).Visit(right.Body);
        return Expression.Lambda<Func<Patient, bool>>(Expression.AndAlso(left.Body, body), parameter);
    }

    private sealed class Rebinder(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}

internal sealed class BookingRepository(AppDbContext context) : Repository<Booking>(context), IBookingRepository
{
    public Task<Booking?> GetReadOnlyAsync(Guid id, CancellationToken cancellationToken) =>
        Set.AsNoTracking().FirstOrDefaultAsync(booking => booking.Id == id, cancellationToken);

    public async Task<Paged<Booking>> ListAsync(BookingFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Filtered(filter);
        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query.OrderByDescending(booking => booking.VisitAtUtc).ThenByDescending(booking => booking.Id).Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Paged<Booking>(rows, page.Page, page.PageSize, total);
    }

    public async Task<IReadOnlyList<Booking>> ListAllAsync(BookingFilter filter, int limit, CancellationToken cancellationToken) =>
        await Filtered(filter).OrderByDescending(booking => booking.VisitAtUtc).ThenByDescending(booking => booking.Id).Take(limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Booking>> ListBookedByPatientForUpdateAsync(Guid patientId, CancellationToken cancellationToken) =>
        await Set.Where(booking => booking.PatientId == patientId && booking.Status == BookingStatus.Booked).ToListAsync(cancellationToken).ConfigureAwait(false);

    private IQueryable<Booking> Filtered(BookingFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking();
        if (filter.Status is { } status)
        {
            query = query.Where(booking => booking.Status == status);
        }

        if (filter.FromUtc is { } from)
        {
            query = query.Where(booking => booking.VisitAtUtc >= from);
        }

        if (filter.ToUtc is { } to)
        {
            query = query.Where(booking => booking.VisitAtUtc < to);
        }

        if (filter.PhlebotomistUserId is { } phlebotomist)
        {
            query = query.Where(booking => booking.PhlebotomistUserId == phlebotomist);
        }

        if (filter.BranchId is { } branch)
        {
            query = query.Where(booking => booking.BranchId == branch);
        }

        if (filter.Search is { } search)
        {
            var pattern = "%" + search.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            var patients = Context.Set<Patient>().AsNoTracking();
            query = query.Where(booking =>
                EF.Functions.ILike(booking.Number, pattern, "\\")
                || (booking.PatientName != null && EF.Functions.ILike(booking.PatientName, pattern, "\\"))
                || (booking.Mobile != null && EF.Functions.ILike(booking.Mobile, pattern, "\\"))
                || EF.Functions.ILike(booking.Address, pattern, "\\")
                || patients.Any(patient => patient.Id == booking.PatientId && (EF.Functions.ILike(patient.FullName, pattern, "\\") || EF.Functions.ILike(patient.Number, pattern, "\\"))));
        }

        return query;
    }
}

internal sealed class BatchRepository(AppDbContext context) : Repository<Batch>(context), IBatchRepository
{
    public Task<Batch?> GetReadOnlyAsync(Guid id, CancellationToken cancellationToken) =>
        Set.AsNoTracking().FirstOrDefaultAsync(batch => batch.Id == id, cancellationToken);

    public async Task<Paged<BatchRow>> ListAsync(BatchFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking();
        if (filter.Kind is { } kind)
        {
            query = query.Where(batch => batch.Kind == kind);
        }

        if (filter.FromUtc is { } from)
        {
            query = query.Where(batch => batch.RegisteredAtUtc >= from);
        }

        if (filter.ToUtc is { } to)
        {
            query = query.Where(batch => batch.RegisteredAtUtc < to);
        }

        if (filter.BranchId is { } branch)
        {
            query = query.Where(batch => batch.BranchId == branch);
        }

        if (filter.Search is { } search)
        {
            var pattern = "%" + search.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            query = query.Where(batch => EF.Functions.ILike(batch.Number, pattern, "\\") || (batch.GroupName != null && EF.Functions.ILike(batch.GroupName, pattern, "\\")));
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var batches = await query.OrderByDescending(batch => batch.RegisteredAtUtc).ThenByDescending(batch => batch.Id).Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        var ids = batches.Select(batch => batch.Id).ToArray();
        var sums = await Context.Set<Visit>().AsNoTracking()
            .Where(visit => visit.BatchId != null && ids.Contains(visit.BatchId.Value))
            .GroupBy(visit => visit.BatchId!.Value)
            .Select(group => new { BatchId = group.Key, Patient = group.Sum(visit => visit.PatientShareTotal), Payer = group.Sum(visit => visit.PayerShareTotal) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var byBatch = sums.ToDictionary(sum => sum.BatchId);

        var rows = batches.Select(batch => byBatch.TryGetValue(batch.Id, out var sum) ? new BatchRow(batch, sum.Patient, sum.Payer) : new BatchRow(batch, 0m, 0m)).ToList();
        return new Paged<BatchRow>(rows, page.Page, page.PageSize, total);
    }
}
