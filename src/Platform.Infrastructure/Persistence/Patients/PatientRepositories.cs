using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Patients;
using Platform.Contracts.Common;
using Platform.Contracts.Patients;
using Platform.Domain.Patients;

namespace Platform.Infrastructure.Persistence.Patients;

internal sealed class PatientRepository(AppDbContext context) : Repository<Patient>(context), IPatientRepository
{
    /// <summary>Merge chains deeper than this are not followed (a chain of 16 merges is an operational anomaly).</summary>
    public const int MaxMergeDepth = 16;

    private static readonly SortColumns<Patient> Sorts = new SortColumns<Patient>(PatientSortKeys.Patients)
        .Column("number", patient => patient.Number)
        .Column("name", patient => patient.FullName)
        .ThenBy(patient => patient.Number)
        .ThenBy(patient => patient.Id);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) => Set.AnyAsync(patient => patient.Id == id, cancellationToken);

    public async Task<Paged<PatientRow>> SearchAsync(PatientSearchFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = PatientQueries.Filter(Set.AsNoTracking(), filter);
        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        if (filter.Sort.Key is null)
        {
            var pageQuery = query.OrderByDescending(patient => patient.CreatedAtUtc).ThenByDescending(patient => patient.Id).Skip(page.Skip).Take(page.PageSize);
            var rows = await PatientQueries.Rows(pageQuery, Set.AsNoTracking()).ToListAsync(cancellationToken).ConfigureAwait(false);
            return new Paged<PatientRow>(PatientQueries.Newest(rows), page.Page, page.PageSize, total);
        }

        // B-03: the page of the requested order is chosen on the server; the row projection keeps no order, so the rows
        // are put back in the order of the ids.
        var ids = await Order(query, filter).Skip(page.Skip).Take(page.PageSize)
            .Select(patient => patient.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var sorted = await PatientQueries.Rows(Set.AsNoTracking().Where(patient => ids.Contains(patient.Id)), Set.AsNoTracking()).ToListAsync(cancellationToken).ConfigureAwait(false);
        var position = ids.Select((id, index) => (id, index)).ToDictionary(entry => entry.id, entry => entry.index);
        return new Paged<PatientRow>(sorted.OrderBy(row => position[row.Id]).ToList(), page.Page, page.PageSize, total);
    }

    /// <summary>
    /// The requested order. Sorted by name for a user without the See-VIP right (<see cref="PatientSearchFilter.IncludeVip"/>
    /// false), a masked VIP row sorts as if its name were empty: after every named row in either direction, by number, so
    /// its place never hints at the hidden name (validation v0.5.0 R-05).
    /// </summary>
    internal static IOrderedQueryable<Patient> Order(IQueryable<Patient> query, PatientSearchFilter filter)
    {
        if (!filter.IncludeVip && string.Equals(filter.Sort.Key, "name", StringComparison.OrdinalIgnoreCase))
        {
            var namedFirst = query.OrderBy(patient => patient.IsVip);
            var byName = filter.Sort.Descending
                ? namedFirst.ThenByDescending(patient => patient.IsVip ? string.Empty : patient.FullName)
                : namedFirst.ThenBy(patient => patient.IsVip ? string.Empty : patient.FullName);
            return byName.ThenBy(patient => patient.Number).ThenBy(patient => patient.Id);
        }

        return Sorts.Apply(query, filter.Sort, rows => rows.OrderByDescending(patient => patient.CreatedAtUtc));
    }

    public async Task<IReadOnlyList<PatientRow>> SearchAllAsync(PatientSearchFilter filter, int limit, CancellationToken cancellationToken)
    {
        var query = PatientQueries.Filter(Set.AsNoTracking(), filter).OrderByDescending(patient => patient.CreatedAtUtc).ThenByDescending(patient => patient.Id).Take(limit);
        return PatientQueries.Newest(await PatientQueries.Rows(query, Set.AsNoTracking()).ToListAsync(cancellationToken).ConfigureAwait(false));
    }

    public Task<PatientRow?> GetRowAsync(Guid id, CancellationToken cancellationToken) =>
        PatientQueries.Rows(Set.AsNoTracking().Where(patient => patient.Id == id), Set.AsNoTracking()).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<PatientRow>> GetRowsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return [];
        }

        var list = ids.Distinct().ToList();
        return await PatientQueries.Rows(Set.AsNoTracking().Where(patient => list.Contains(patient.Id)), Set.AsNoTracking()).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MergedPatientReference>> ListMergedIntoAsync(Guid survivorId, CancellationToken cancellationToken) =>
        await Set.AsNoTracking()
            .Where(patient => patient.MergedIntoPatientId == survivorId)
            .OrderBy(patient => patient.MergedAtUtc)
            .Select(patient => new MergedPatientReference(patient.Id, patient.Number, patient.MergedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<Guid>> MergedFamilyAsync(Guid survivorId, CancellationToken cancellationToken)
    {
        var family = new List<Guid> { survivorId };
        var frontier = new List<Guid> { survivorId };
        for (var depth = 0; depth < MaxMergeDepth && frontier.Count > 0; depth++)
        {
            var current = frontier;
            var next = await Set.AsNoTracking()
                .Where(patient => patient.MergedIntoPatientId != null && current.Contains(patient.MergedIntoPatientId.Value))
                .Select(patient => patient.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            frontier = next.Except(family).ToList();
            family.AddRange(frontier);
        }

        return family;
    }

    public async Task<IReadOnlyList<PatientRow>> FindSimilarCandidatesAsync(IReadOnlyList<string> nameTokens, string? mobile, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nameTokens);
        Expression<Func<Patient, bool>>? predicate = null;
        if (mobile is not null)
        {
            predicate = patient => patient.Mobile == mobile;
        }

        var tokens = nameTokens.Where(token => token.Length > 0).Distinct(StringComparer.Ordinal).Take(6).ToList();
        if (tokens.Count == 1)
        {
            var key = tokens[0];
            predicate = PatientQueries.Or(predicate, patient => patient.NameKey == key || patient.NameArKey == key);
        }

        for (var i = 0; i < tokens.Count; i++)
        {
            for (var j = i + 1; j < tokens.Count; j++)
            {
                var first = $" {tokens[i]} ";
                var second = $" {tokens[j]} ";
                predicate = PatientQueries.Or(predicate, patient => (" " + patient.NameKey + " ").Contains(first) && (" " + patient.NameKey + " ").Contains(second));
                predicate = PatientQueries.Or(predicate, patient => (" " + patient.NameArKey + " ").Contains(first) && (" " + patient.NameArKey + " ").Contains(second));
            }
        }

        if (predicate is null)
        {
            return [];
        }

        var query = Set.AsNoTracking()
            .Where(patient => patient.Status == PatientStatus.Active)
            .Where(predicate)
            .OrderByDescending(patient => patient.CreatedAtUtc)
            .Take(limit);
        return PatientQueries.Newest(await PatientQueries.Rows(query, Set.AsNoTracking()).ToListAsync(cancellationToken).ConfigureAwait(false));
    }
}

internal sealed class PatientDocumentRepository(AppDbContext context) : Repository<PatientDocument>(context), IPatientDocumentRepository
{
    public async Task<IReadOnlyList<PatientDocumentItem>> ListAsync(IReadOnlyCollection<Guid> patientIds, bool includeInactive, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(patientIds);
        var ids = patientIds.Distinct().ToList();
        var documents = Set.AsNoTracking().Where(document => ids.Contains(document.PatientId));
        if (!includeInactive)
        {
            documents = documents.Where(document => document.IsActive);
        }

        var items = await Items(documents).ToListAsync(cancellationToken).ConfigureAwait(false);
        return items.OrderByDescending(item => item.UploadedAtUtc).ThenByDescending(item => item.Id).ToList();
    }

    public Task<PatientDocumentItem?> GetItemAsync(Guid documentId, CancellationToken cancellationToken) =>
        Items(Set.AsNoTracking().Where(document => document.Id == documentId)).FirstOrDefaultAsync(cancellationToken);

    public Task<string?> GetStorageKeyAsync(Guid documentId, CancellationToken cancellationToken) =>
        Set.AsNoTracking().Where(document => document.Id == documentId).Select(document => document.StorageKey).FirstOrDefaultAsync(cancellationToken);

    /// <summary>Uploaded-by is the creation stamp and deactivated-by the modification stamp of the document (its only change): names as of the action.</summary>
    private IQueryable<PatientDocumentItem> Items(IQueryable<PatientDocument> documents) =>
        from document in documents
        join patient in Context.Set<Patient>().AsNoTracking() on document.PatientId equals patient.Id
        select new PatientDocumentItem(
            document.Id,
            document.PatientId,
            patient.Number,
            document.FileName,
            document.ContentType,
            document.SizeBytes,
            document.Note,
            document.UploadedByUserId,
            document.CreatedByDisplayName,
            document.UploadedAtUtc,
            document.IsActive,
            document.IsActive ? null : document.ModifiedByDisplayName,
            document.DeactivatedAtUtc,
            document.DeactivationReason);
}

/// <summary>Shared query pieces of the module: the register filter, the row projection (with the survivor's number) and predicate composition.</summary>
internal static class PatientQueries
{
    /// <summary>
    /// Gender, VIP and status filters, then the search box: number OR mobile digits OR national id OR every name token
    /// (Latin or Arabic key). Without the See-VIP right (<see cref="PatientSearchFilter.IncludeVip"/> false) a VIP file
    /// matches only its full patient number or full national id, never a name, a mobile or a part of a number, as in the
    /// visit register (REG intake Q12; validation A-03, 2026-10-11).
    /// </summary>
    public static IQueryable<Patient> Filter(IQueryable<Patient> query, PatientSearchFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.Gender is { } gender)
        {
            query = query.Where(patient => patient.Gender == gender);
        }

        if (filter.IsVip is { } vip)
        {
            query = query.Where(patient => patient.IsVip == vip);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(patient => patient.Status == status);
        }

        if (filter.Terms is not { } terms)
        {
            return query;
        }

        if (filter.IncludeVip)
        {
            return query.Where(Matches(terms));
        }

        var number = terms.Number;
        var nationalId = terms.NationalId;
        Expression<Func<Patient, bool>> exactVip = patient => patient.IsVip && (patient.Number == number || (nationalId != null && patient.NationalId == nationalId));
        return query.Where(Or(And(patient => !patient.IsVip, Matches(terms)), exactVip));
    }

    public static IQueryable<PatientRow> Rows(IQueryable<Patient> source, IQueryable<Patient> all) =>
        from patient in source
        from survivor in all.Where(other => other.Id == patient.MergedIntoPatientId).DefaultIfEmpty()
        select new PatientRow(
            patient.Id,
            patient.Number,
            patient.Title,
            patient.FullName,
            patient.NameAr,
            patient.NameKey,
            patient.NameArKey,
            patient.Gender,
            patient.DateOfBirth,
            patient.AgeYears,
            patient.AgeMonths,
            patient.AgeDays,
            patient.AgeRecordedOn,
            patient.RegisteredOn,
            patient.PatientConditionId,
            patient.Mobile,
            patient.NationalId,
            patient.Nationality,
            patient.Email,
            patient.GovernorateId,
            patient.City,
            patient.Address,
            patient.Comments,
            patient.IsVip,
            patient.Status,
            patient.MergedIntoPatientId,
            survivor == null ? null : survivor.Number,
            patient.MergedAtUtc,
            patient.MergedByUserId,
            patient.MergeReason,
            patient.CreatedAtUtc,
            patient.CreatedByDisplayName,
            patient.ModifiedAtUtc,
            patient.ModifiedByDisplayName,
            patient.RowVersion);

    /// <summary>Stable order of a fetched page: newest registration first (UUID v7 ids break ties in time order).</summary>
    public static List<PatientRow> Newest(IEnumerable<PatientRow> rows) => rows.OrderByDescending(row => row.CreatedAtUtc).ThenByDescending(row => row.Id).ToList();

    public static Expression<Func<Patient, bool>> Or(Expression<Func<Patient, bool>>? left, Expression<Func<Patient, bool>> right) =>
        left is null ? right : Combine(left, right, Expression.OrElse);

    public static Expression<Func<Patient, bool>> And(Expression<Func<Patient, bool>> left, Expression<Func<Patient, bool>> right) =>
        Combine(left, right, Expression.AndAlso);

    private static Expression<Func<Patient, bool>> Matches(PatientSearchTerms terms)
    {
        var number = terms.Number;
        Expression<Func<Patient, bool>> predicate = patient => patient.Number.Contains(number);
        if (terms.Digits is { } digits)
        {
            predicate = Or(predicate, patient => patient.Mobile != null && patient.Mobile.Contains(digits));
        }

        if (terms.NationalId is { } nationalId)
        {
            predicate = Or(predicate, patient => patient.NationalId != null && patient.NationalId.StartsWith(nationalId));
        }

        if (terms.NameTokens.Count > 0)
        {
            Expression<Func<Patient, bool>>? names = null;
            foreach (var token in terms.NameTokens.Take(6))
            {
                Expression<Func<Patient, bool>> one = patient => patient.NameKey.Contains(token) || patient.NameArKey.Contains(token);
                names = names is null ? one : And(names, one);
            }

            predicate = Or(predicate, names!);
        }

        return predicate;
    }

    private static Expression<Func<Patient, bool>> Combine(Expression<Func<Patient, bool>> left, Expression<Func<Patient, bool>> right, Func<Expression, Expression, BinaryExpression> merge)
    {
        var parameter = left.Parameters[0];
        var body = new ParameterRebinder(right.Parameters[0], parameter).Visit(right.Body);
        return Expression.Lambda<Func<Patient, bool>>(merge(left.Body, body), parameter);
    }

    private sealed class ParameterRebinder(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
