using Microsoft.EntityFrameworkCore;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Catalogue;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;
using Platform.Domain.Catalogue;

namespace Platform.Infrastructure.Persistence.Catalogue;

// Repositories of Catalogue B: write side (load tracked, stage new) plus the register projections. Reads are bounded
// by the tenant query filter (and RLS); lists are untracked, stably ordered and paged; search is case-insensitive
// ILIKE with the wildcards of the text escaped. Linked services and clinical groups get their labels from Catalogue A's
// tables through CatalogueBSql (same wave: raw SQL, never their types).

internal static class CatalogueBListing
{
    public const string Escape = "\\";

    public static async Task<Paged<TItem>> PageAsync<TEntity, TItem>(
        IQueryable<TEntity> ordered,
        PageRequest page,
        Func<IReadOnlyList<TEntity>, Task<IReadOnlyList<TItem>>> map,
        CancellationToken cancellationToken)
    {
        var total = await ordered.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await ordered.Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        var items = await map(rows).ConfigureAwait(false);
        return new Paged<TItem>(items, page.Page, page.PageSize, total);
    }

    public static IQueryable<TEntity> Active<TEntity>(this IQueryable<TEntity> query, bool? isActive)
        where TEntity : CatalogueBRecord =>
        isActive is { } active ? query.Where(record => record.IsActive == active) : query;
}

internal sealed class PreparationRepository(AppDbContext context) : Repository<Preparation>(context), IPreparationRepository
{
    private static readonly SortColumns<Preparation> Sorts = new SortColumns<Preparation>(CatalogueSortKeys.Preparations)
        .Column("code", preparation => preparation.Code)
        .Column("questionEn", preparation => preparation.Question.En)
        .Column("questionAr", preparation => preparation.Question.Ar)
        .ThenBy(preparation => preparation.Code)
        .ThenBy(preparation => preparation.Id);

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) =>
        Set.AnyAsync(preparation => preparation.Code == code, cancellationToken);

    public Task<Paged<PreparationListItem>> ListAsync(CatalogueBFilter filter, PreparationAnswerType? answerType, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking().Active(filter.IsActive);
        if (answerType is { } type)
        {
            query = query.Where(preparation => preparation.AnswerType == type);
        }

        if (CatalogueBSql.SearchPattern(filter.Search) is { } pattern)
        {
            query = query.Where(preparation => EF.Functions.ILike(preparation.Code, pattern, CatalogueBListing.Escape)
                                               || EF.Functions.ILike(preparation.Question.En, pattern, CatalogueBListing.Escape)
                                               || EF.Functions.ILike(preparation.Question.Ar, pattern, CatalogueBListing.Escape));
        }

        return CatalogueBListing.PageAsync(Sorts.Apply(query, filter.Sort, rows => rows.OrderBy(preparation => preparation.Code)), page, rows => MapAsync(rows, cancellationToken), cancellationToken);
    }

    public async Task<PreparationListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken)
    {
        var rows = await Set.AsNoTracking().Where(preparation => preparation.Id == id).ToListAsync(cancellationToken).ConfigureAwait(false);
        return (await MapAsync(rows, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
    }

    private async Task<IReadOnlyList<PreparationListItem>> MapAsync(IReadOnlyList<Preparation> rows, CancellationToken cancellationToken)
    {
        var services = await CatalogueBSql.ServicesAsync(Context, rows.SelectMany(row => row.ServiceIds), cancellationToken).ConfigureAwait(false);
        return rows.Select(row => new PreparationListItem(
            row.Id, row.Code, row.Question.En, row.Question.Ar, row.AnswerType.ToString(), row.Options, row.Gender.ToString(), CatalogueBSql.Link(row.ServiceIds, services), row.IsActive)).ToList();
    }
}

internal sealed class CommentTemplateRepository(AppDbContext context) : Repository<CommentTemplate>(context), ICommentTemplateRepository
{
    private static readonly SortColumns<CommentTemplate> Sorts = new SortColumns<CommentTemplate>(CatalogueSortKeys.CommentTemplates)
        .Column("shortName", template => template.ShortName)
        .ThenBy(template => template.Id);

    public Task<bool> ShortNameExistsAsync(string shortName, CancellationToken cancellationToken) =>
        Set.AnyAsync(template => template.ShortName == shortName, cancellationToken);

    public Task<Paged<CommentTemplateListItem>> ListAsync(CatalogueBFilter filter, Guid? clinicalGroupId, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking().Active(filter.IsActive);
        if (clinicalGroupId is { } groupId)
        {
            // a comment without a group is offered in every group
            query = query.Where(template => template.ClinicalGroupId == groupId || template.ClinicalGroupId == null);
        }

        if (CatalogueBSql.SearchPattern(filter.Search) is { } pattern)
        {
            query = query.Where(template => EF.Functions.ILike(template.ShortName, pattern, CatalogueBListing.Escape)
                                            || EF.Functions.ILike(template.Text.En, pattern, CatalogueBListing.Escape)
                                            || EF.Functions.ILike(template.Text.Ar, pattern, CatalogueBListing.Escape));
        }

        return CatalogueBListing.PageAsync(Sorts.Apply(query, filter.Sort, rows => rows.OrderBy(template => template.ShortName)), page, rows => MapAsync(rows, cancellationToken), cancellationToken);
    }

    public async Task<CommentTemplateListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken)
    {
        var rows = await Set.AsNoTracking().Where(template => template.Id == id).ToListAsync(cancellationToken).ConfigureAwait(false);
        return (await MapAsync(rows, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
    }

    private async Task<IReadOnlyList<CommentTemplateListItem>> MapAsync(IReadOnlyList<CommentTemplate> rows, CancellationToken cancellationToken)
    {
        var groups = await CatalogueBSql.ClinicalGroupsAsync(Context, rows.Select(row => row.ClinicalGroupId), cancellationToken).ConfigureAwait(false);
        return rows.Select(row =>
        {
            var group = row.ClinicalGroupId is { } groupId && groups.TryGetValue(groupId, out var found) ? found : null;
            return new CommentTemplateListItem(row.Id, row.ShortName, row.Text.En, row.Text.Ar, row.ClinicalGroupId, group?.NameEn, group?.NameAr, row.IsActive);
        }).ToList();
    }
}

internal sealed class DoctorRepository(AppDbContext context) : Repository<Doctor>(context), IDoctorRepository
{
    private static readonly SortColumns<Doctor> Sorts = new SortColumns<Doctor>(CatalogueSortKeys.Doctors)
        .Column("code", doctor => doctor.Code)
        .Column("nameEn", doctor => doctor.Name.En)
        .Column("nameAr", doctor => doctor.Name.Ar)
        .Column("specialty", doctor => doctor.Specialty)
        .ThenBy(doctor => doctor.Code)
        .ThenBy(doctor => doctor.Id);

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) =>
        Set.AnyAsync(doctor => doctor.Code == code, cancellationToken);

    public Task<Paged<DoctorListItem>> ListAsync(CatalogueBFilter filter, Guid? branchId, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking().Active(filter.IsActive);
        if (branchId is { } branch)
        {
            query = query.Where(doctor => doctor.BranchIds.Length == 0 || doctor.BranchIds.Contains(branch));
        }

        if (CatalogueBSql.SearchPattern(filter.Search) is { } pattern)
        {
            query = query.Where(doctor => EF.Functions.ILike(doctor.Code, pattern, CatalogueBListing.Escape)
                                          || EF.Functions.ILike(doctor.Name.En, pattern, CatalogueBListing.Escape)
                                          || EF.Functions.ILike(doctor.Name.Ar, pattern, CatalogueBListing.Escape)
                                          || (doctor.Specialty != null && EF.Functions.ILike(doctor.Specialty, pattern, CatalogueBListing.Escape))
                                          || (doctor.Phone != null && EF.Functions.ILike(doctor.Phone, pattern, CatalogueBListing.Escape)));
        }

        return CatalogueBListing.PageAsync(Sorts.Apply(query, filter.Sort, rows => rows.OrderBy(doctor => doctor.Code)), page, rows => Task.FromResult(Map(rows)), cancellationToken);
    }

    public async Task<DoctorListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken)
    {
        var rows = await Set.AsNoTracking().Where(doctor => doctor.Id == id).ToListAsync(cancellationToken).ConfigureAwait(false);
        return Map(rows).FirstOrDefault();
    }

    private static IReadOnlyList<DoctorListItem> Map(IReadOnlyList<Doctor> rows) =>
        rows.Select(row => new DoctorListItem(row.Id, row.Code, row.Name.En, row.Name.Ar, row.Specialty, row.Occupation, row.BranchIds, row.SharePercent, row.Phone, row.IsActive, [])).ToList();   // Branches: joined by the query handlers (DoctorBranches)
}

internal sealed class DiagnosisRepository(AppDbContext context) : Repository<Diagnosis>(context), IDiagnosisRepository
{
    private static readonly SortColumns<Diagnosis> Sorts = new SortColumns<Diagnosis>(CatalogueSortKeys.Diagnoses)
        .Column("icdCode", diagnosis => diagnosis.IcdCode)
        .Column("nameEn", diagnosis => diagnosis.Name.En)
        .Column("nameAr", diagnosis => diagnosis.Name.Ar)
        .ThenBy(diagnosis => diagnosis.IcdCode)
        .ThenBy(diagnosis => diagnosis.Id);

    public Task<bool> IcdCodeExistsAsync(string icdCode, CancellationToken cancellationToken) =>
        Set.AnyAsync(diagnosis => diagnosis.IcdCode == icdCode, cancellationToken);

    public Task<Paged<DiagnosisListItem>> ListAsync(CatalogueBFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking().Active(filter.IsActive);
        if (CatalogueBSql.SearchPattern(filter.Search) is { } pattern)
        {
            query = query.Where(diagnosis => EF.Functions.ILike(diagnosis.IcdCode, pattern, CatalogueBListing.Escape)
                                             || EF.Functions.ILike(diagnosis.Name.En, pattern, CatalogueBListing.Escape)
                                             || EF.Functions.ILike(diagnosis.Name.Ar, pattern, CatalogueBListing.Escape));
        }

        return CatalogueBListing.PageAsync(Sorts.Apply(query, filter.Sort, rows => rows.OrderBy(diagnosis => diagnosis.IcdCode)), page, rows => MapAsync(rows, cancellationToken), cancellationToken);
    }

    public async Task<DiagnosisListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken)
    {
        var rows = await Set.AsNoTracking().Where(diagnosis => diagnosis.Id == id).ToListAsync(cancellationToken).ConfigureAwait(false);
        return (await MapAsync(rows, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
    }

    private async Task<IReadOnlyList<DiagnosisListItem>> MapAsync(IReadOnlyList<Diagnosis> rows, CancellationToken cancellationToken)
    {
        var services = await CatalogueBSql.ServicesAsync(Context, rows.SelectMany(row => row.ServiceIds), cancellationToken).ConfigureAwait(false);
        return rows.Select(row => new DiagnosisListItem(row.Id, row.IcdCode, row.Name.En, row.Name.Ar, CatalogueBSql.Link(row.ServiceIds, services), row.IsActive)).ToList();
    }
}

internal sealed class SendingLabRepository(AppDbContext context) : Repository<SendingLab>(context), ISendingLabRepository
{
    private static readonly SortColumns<SendingLab> Sorts = new SortColumns<SendingLab>(CatalogueSortKeys.SendingLabs)
        .Column("nameEn", lab => lab.Name.En)
        .Column("nameAr", lab => lab.Name.Ar)
        .ThenBy(lab => lab.Id);

    public Task<bool> NameExistsAsync(string nameEn, Guid? exceptId, CancellationToken cancellationToken)
    {
        var lowered = nameEn.Trim().ToLowerInvariant();
        return Set.AnyAsync(lab => lab.Name.En.ToLower() == lowered && (exceptId == null || lab.Id != exceptId), cancellationToken);
    }

    public Task<Paged<SendingLabListItem>> ListAsync(CatalogueBFilter filter, Guid? payerId, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking().Active(filter.IsActive);
        if (payerId is { } payer)
        {
            query = query.Where(lab => lab.PayerId == payer);
        }

        if (CatalogueBSql.SearchPattern(filter.Search) is { } pattern)
        {
            query = query.Where(lab => EF.Functions.ILike(lab.Name.En, pattern, CatalogueBListing.Escape)
                                       || EF.Functions.ILike(lab.Name.Ar, pattern, CatalogueBListing.Escape)
                                       || EF.Functions.ILike(lab.Courier.En, pattern, CatalogueBListing.Escape)
                                       || EF.Functions.ILike(lab.Courier.Ar, pattern, CatalogueBListing.Escape)
                                       || (lab.Phone != null && EF.Functions.ILike(lab.Phone, pattern, CatalogueBListing.Escape)));
        }

        return CatalogueBListing.PageAsync(Sorts.Apply(query, filter.Sort, rows => rows.OrderBy(lab => lab.Name.En).ThenBy(lab => lab.Id)), page, rows => MapAsync(rows, cancellationToken), cancellationToken);
    }

    public async Task<SendingLabListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken)
    {
        var rows = await Set.AsNoTracking().Where(lab => lab.Id == id).ToListAsync(cancellationToken).ConfigureAwait(false);
        return (await MapAsync(rows, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
    }

    private async Task<IReadOnlyList<SendingLabListItem>> MapAsync(IReadOnlyList<SendingLab> rows, CancellationToken cancellationToken)
    {
        var doctorIds = rows.Where(row => row.DoctorId != null).Select(row => row.DoctorId!.Value).Distinct().ToList();
        var doctors = await Context.Set<Doctor>().AsNoTracking().Where(doctor => doctorIds.Contains(doctor.Id))
                .Select(doctor => new { doctor.Id, doctor.Name.En, doctor.Name.Ar })
                .ToDictionaryAsync(doctor => doctor.Id, cancellationToken)
                .ConfigureAwait(false);
        return rows.Select(row =>
        {
            var doctor = row.DoctorId is { } doctorId && doctors.TryGetValue(doctorId, out var found) ? found : null;
            return new SendingLabListItem(row.Id, row.Name.En, row.Name.Ar, row.PayerId, row.ContractTierId, row.Courier.En, row.Courier.Ar, row.Phone, row.DoctorId, doctor?.En, doctor?.Ar, row.IsActive);
        }).ToList();
    }
}

internal sealed class ShortcutSetRepository(AppDbContext context) : Repository<ShortcutSet>(context), IShortcutSetRepository
{
    private static readonly SortColumns<ShortcutSet> Sorts = new SortColumns<ShortcutSet>(CatalogueSortKeys.ShortcutSets)
        .Column("code", set => set.Code)
        .Column("nameEn", set => set.Name.En)
        .Column("nameAr", set => set.Name.Ar)
        .ThenBy(set => set.Code)
        .ThenBy(set => set.Id);

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) =>
        Set.AnyAsync(set => set.Code == code, cancellationToken);

    public Task<Paged<ShortcutSetListItem>> ListAsync(CatalogueBFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking().Active(filter.IsActive);
        if (CatalogueBSql.SearchPattern(filter.Search) is { } pattern)
        {
            query = query.Where(set => EF.Functions.ILike(set.Code, pattern, CatalogueBListing.Escape)
                                       || EF.Functions.ILike(set.Name.En, pattern, CatalogueBListing.Escape)
                                       || EF.Functions.ILike(set.Name.Ar, pattern, CatalogueBListing.Escape));
        }

        return CatalogueBListing.PageAsync(Sorts.Apply(query, filter.Sort, rows => rows.OrderBy(set => set.Code)), page, rows => MapAsync(rows, cancellationToken), cancellationToken);
    }

    public async Task<ShortcutSetListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken)
    {
        var rows = await Set.AsNoTracking().Where(set => set.Id == id).ToListAsync(cancellationToken).ConfigureAwait(false);
        return (await MapAsync(rows, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
    }

    private async Task<IReadOnlyList<ShortcutSetListItem>> MapAsync(IReadOnlyList<ShortcutSet> rows, CancellationToken cancellationToken)
    {
        var services = await CatalogueBSql.ServicesAsync(Context, rows.SelectMany(row => row.ServiceIds), cancellationToken).ConfigureAwait(false);
        return rows.Select(row => new ShortcutSetListItem(row.Id, row.Code, row.Name.En, row.Name.Ar, CatalogueBSql.Link(row.ServiceIds, services), row.IsActive)).ToList();
    }
}

internal sealed class TestRuleRepository(AppDbContext context) : Repository<TestRule>(context), ITestRuleRepository
{
    private static readonly SortColumns<TestRule> Sorts = new SortColumns<TestRule>(CatalogueSortKeys.TestRules)
        .Column("nameEn", rule => rule.Name.En)
        .Column("nameAr", rule => rule.Name.Ar)
        .ThenBy(rule => rule.Id);

    public Task<bool> NameExistsAsync(string nameEn, Guid? exceptId, CancellationToken cancellationToken)
    {
        var lowered = nameEn.Trim().ToLowerInvariant();
        return Set.AnyAsync(rule => rule.Name.En.ToLower() == lowered && (exceptId == null || rule.Id != exceptId), cancellationToken);
    }

    public async Task<IReadOnlyList<TestRule>> ActiveRulesAsync(CancellationToken cancellationToken) =>
        await Set.AsNoTracking().Where(rule => rule.IsActive).OrderBy(rule => rule.Id).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<Paged<TestRuleListItem>> ListAsync(CatalogueBFilter filter, Guid? clinicalGroupId, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking().Active(filter.IsActive);
        if (clinicalGroupId is { } groupId)
        {
            query = query.Where(rule => rule.ClinicalGroupId == groupId);
        }

        if (CatalogueBSql.SearchPattern(filter.Search) is { } pattern)
        {
            query = query.Where(rule => EF.Functions.ILike(rule.Name.En, pattern, CatalogueBListing.Escape)
                                        || EF.Functions.ILike(rule.Name.Ar, pattern, CatalogueBListing.Escape));
        }

        return CatalogueBListing.PageAsync(Sorts.Apply(query, filter.Sort, rows => rows.OrderBy(rule => rule.Name.En).ThenBy(rule => rule.Id)), page, rows => MapAsync(rows, cancellationToken), cancellationToken);
    }

    public async Task<TestRuleListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken)
    {
        var rows = await Set.AsNoTracking().Where(rule => rule.Id == id).ToListAsync(cancellationToken).ConfigureAwait(false);
        return (await MapAsync(rows, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
    }

    private async Task<IReadOnlyList<TestRuleListItem>> MapAsync(IReadOnlyList<TestRule> rows, CancellationToken cancellationToken)
    {
        var services = await CatalogueBSql.ServicesAsync(Context, rows.SelectMany(row => row.TriggerServiceIds.Concat(row.AddedServiceIds)), cancellationToken).ConfigureAwait(false);
        var groups = await CatalogueBSql.ClinicalGroupsAsync(Context, rows.Select(row => row.ClinicalGroupId), cancellationToken).ConfigureAwait(false);
        return rows.Select(row =>
        {
            var group = row.ClinicalGroupId is { } groupId && groups.TryGetValue(groupId, out var found) ? found : null;
            return new TestRuleListItem(
                row.Id, row.Name.En, row.Name.Ar, row.ClinicalGroupId, group?.NameEn, group?.NameAr,
                CatalogueBSql.Link(row.TriggerServiceIds, services), CatalogueBSql.Link(row.AddedServiceIds, services), row.IsActive);
        }).ToList();
    }
}

internal sealed class ReasonRepository(AppDbContext context) : Repository<Reason>(context), IReasonRepository
{
    private static readonly SortColumns<Reason> Sorts = new SortColumns<Reason>(CatalogueSortKeys.Reasons)
        .Column("kind", reason => reason.Kind)
        .Column("nameEn", reason => reason.Name.En)
        .Column("nameAr", reason => reason.Name.Ar)
        .ThenBy(reason => reason.Name.En)
        .ThenBy(reason => reason.Id);

    public Task<bool> NameExistsAsync(ReasonKind kind, string nameEn, Guid? exceptId, CancellationToken cancellationToken)
    {
        var lowered = nameEn.Trim().ToLowerInvariant();
        return Set.AnyAsync(reason => reason.Kind == kind && reason.Name.En.ToLower() == lowered && (exceptId == null || reason.Id != exceptId), cancellationToken);
    }

    public Task<Paged<ReasonListItem>> ListAsync(CatalogueBFilter filter, ReasonKind? kind, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking().Active(filter.IsActive);
        if (kind is { } wanted)
        {
            query = query.Where(reason => reason.Kind == wanted);
        }

        if (CatalogueBSql.SearchPattern(filter.Search) is { } pattern)
        {
            query = query.Where(reason => EF.Functions.ILike(reason.Name.En, pattern, CatalogueBListing.Escape)
                                          || EF.Functions.ILike(reason.Name.Ar, pattern, CatalogueBListing.Escape));
        }

        return CatalogueBListing.PageAsync(Sorts.Apply(query, filter.Sort, rows => rows.OrderBy(reason => reason.Kind).ThenBy(reason => reason.Name.En).ThenBy(reason => reason.Id)), page, rows => Task.FromResult(Map(rows)), cancellationToken);
    }

    public async Task<ReasonListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken)
    {
        var rows = await Set.AsNoTracking().Where(reason => reason.Id == id).ToListAsync(cancellationToken).ConfigureAwait(false);
        return Map(rows).FirstOrDefault();
    }

    private static IReadOnlyList<ReasonListItem> Map(IReadOnlyList<Reason> rows) =>
        rows.Select(row => new ReasonListItem(row.Id, row.Kind.ToString(), row.Name.En, row.Name.Ar, row.IsActive)).ToList();
}

internal sealed class ReportTemplateRepository(AppDbContext context) : Repository<ReportTemplate>(context), IReportTemplateRepository
{
    private static readonly SortColumns<ReportTemplate> Sorts = new SortColumns<ReportTemplate>(CatalogueSortKeys.ReportTemplates)
        .Column("nameEn", template => template.Name.En)
        .Column("nameAr", template => template.Name.Ar)
        .Column("type", template => template.Type)
        .ThenBy(template => template.Name.En)
        .ThenBy(template => template.Id);

    public Task<bool> NameExistsAsync(string nameEn, Guid? exceptId, CancellationToken cancellationToken)
    {
        var lowered = nameEn.Trim().ToLowerInvariant();
        return Set.AnyAsync(template => template.Name.En.ToLower() == lowered && (exceptId == null || template.Id != exceptId), cancellationToken);
    }

    public Task<Paged<ReportTemplateListItem>> ListAsync(CatalogueBFilter filter, ReportTemplateType? type, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking().Active(filter.IsActive);
        if (type is { } wanted)
        {
            query = query.Where(template => template.Type == wanted);
        }

        if (CatalogueBSql.SearchPattern(filter.Search) is { } pattern)
        {
            query = query.Where(template => EF.Functions.ILike(template.Name.En, pattern, CatalogueBListing.Escape)
                                            || EF.Functions.ILike(template.Name.Ar, pattern, CatalogueBListing.Escape));
        }

        return CatalogueBListing.PageAsync(Sorts.Apply(query, filter.Sort, rows => rows.OrderBy(template => template.Name.En).ThenBy(template => template.Id)), page, rows => Task.FromResult(Map(rows)), cancellationToken);
    }

    public async Task<ReportTemplateListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken)
    {
        var rows = await Set.AsNoTracking().Where(template => template.Id == id).ToListAsync(cancellationToken).ConfigureAwait(false);
        return Map(rows).FirstOrDefault();
    }

    private static IReadOnlyList<ReportTemplateListItem> Map(IReadOnlyList<ReportTemplate> rows) =>
        rows.Select(row => new ReportTemplateListItem(
            row.Id, row.Name.En, row.Name.Ar, row.Type.ToString(), row.LogoFileId, row.SignatureFileId, row.ShowQr, row.Language.ToString(),
            row.HeaderText?.En, row.HeaderText?.Ar, row.FooterText?.En, row.FooterText?.Ar, row.IsActive, row.Design?.ToString())).ToList();
}

internal sealed class DrugTypeRepository(AppDbContext context) : Repository<DrugType>(context), IDrugTypeRepository
{
    private static readonly SortColumns<DrugType> Sorts = new SortColumns<DrugType>(CatalogueSortKeys.DrugTypes)
        .Column("nameEn", drugType => drugType.Name.En)
        .Column("nameAr", drugType => drugType.Name.Ar)
        .ThenBy(drugType => drugType.Id);

    public Task<bool> NameExistsAsync(string nameEn, Guid? exceptId, CancellationToken cancellationToken)
    {
        var lowered = nameEn.Trim().ToLowerInvariant();
        return Set.AnyAsync(drugType => drugType.Name.En.ToLower() == lowered && (exceptId == null || drugType.Id != exceptId), cancellationToken);
    }

    public Task<Paged<DrugTypeListItem>> ListAsync(CatalogueBFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking().Active(filter.IsActive);
        if (CatalogueBSql.SearchPattern(filter.Search) is { } pattern)
        {
            query = query.Where(drugType => EF.Functions.ILike(drugType.Name.En, pattern, CatalogueBListing.Escape)
                                            || EF.Functions.ILike(drugType.Name.Ar, pattern, CatalogueBListing.Escape));
        }

        return CatalogueBListing.PageAsync(Sorts.Apply(query, filter.Sort, rows => rows.OrderBy(drugType => drugType.Name.En).ThenBy(drugType => drugType.Id)), page, rows => Task.FromResult(Map(rows)), cancellationToken);
    }

    public async Task<DrugTypeListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken)
    {
        var rows = await Set.AsNoTracking().Where(drugType => drugType.Id == id).ToListAsync(cancellationToken).ConfigureAwait(false);
        return Map(rows).FirstOrDefault();
    }

    private static IReadOnlyList<DrugTypeListItem> Map(IReadOnlyList<DrugType> rows) =>
        rows.Select(row => new DrugTypeListItem(row.Id, row.Name.En, row.Name.Ar, row.IsActive)).ToList();
}

internal sealed class CommercialNameRepository(AppDbContext context) : Repository<CommercialName>(context), ICommercialNameRepository
{
    /// <summary>The drug type columns sort by the drug type's name (a correlated sub-select), the others by the row's own columns.</summary>
    private SortColumns<CommercialName> Sorts()
    {
        var drugTypes = Context.Set<DrugType>().AsNoTracking();
        return new SortColumns<CommercialName>(CatalogueSortKeys.CommercialNames)
            .Column("nameEn", commercialName => commercialName.Name.En)
            .Column("nameAr", commercialName => commercialName.Name.Ar)
            .Column("drugTypeEn", commercialName => drugTypes.Where(drugType => drugType.Id == commercialName.DrugTypeId).Select(drugType => drugType.Name.En).FirstOrDefault())
            .Column("drugTypeAr", commercialName => drugTypes.Where(drugType => drugType.Id == commercialName.DrugTypeId).Select(drugType => drugType.Name.Ar).FirstOrDefault())
            .ThenBy(commercialName => commercialName.Name.En)
            .ThenBy(commercialName => commercialName.Id);
    }

    public Task<bool> NameExistsAsync(string nameEn, Guid? exceptId, CancellationToken cancellationToken)
    {
        var lowered = nameEn.Trim().ToLowerInvariant();
        return Set.AnyAsync(commercialName => commercialName.Name.En.ToLower() == lowered && (exceptId == null || commercialName.Id != exceptId), cancellationToken);
    }

    public Task<Paged<CommercialNameListItem>> ListAsync(CatalogueBFilter filter, Guid? drugTypeId, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking().Active(filter.IsActive);
        if (drugTypeId is { } drugType)
        {
            query = query.Where(commercialName => commercialName.DrugTypeId == drugType);
        }

        if (CatalogueBSql.SearchPattern(filter.Search) is { } pattern)
        {
            query = query.Where(commercialName => EF.Functions.ILike(commercialName.Name.En, pattern, CatalogueBListing.Escape)
                                                  || EF.Functions.ILike(commercialName.Name.Ar, pattern, CatalogueBListing.Escape));
        }

        return CatalogueBListing.PageAsync(Sorts().Apply(query, filter.Sort, rows => rows.OrderBy(commercialName => commercialName.Name.En).ThenBy(commercialName => commercialName.Id)), page, rows => MapAsync(rows, cancellationToken), cancellationToken);
    }

    public async Task<CommercialNameListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken)
    {
        var rows = await Set.AsNoTracking().Where(commercialName => commercialName.Id == id).ToListAsync(cancellationToken).ConfigureAwait(false);
        return (await MapAsync(rows, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
    }

    private async Task<IReadOnlyList<CommercialNameListItem>> MapAsync(IReadOnlyList<CommercialName> rows, CancellationToken cancellationToken)
    {
        var drugTypeIds = rows.Select(row => row.DrugTypeId).Distinct().ToList();
        var drugTypes = await Context.Set<DrugType>().AsNoTracking().Where(drugType => drugTypeIds.Contains(drugType.Id))
            .Select(drugType => new { drugType.Id, drugType.Name.En, drugType.Name.Ar })
            .ToDictionaryAsync(drugType => drugType.Id, cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(row =>
        {
            var drugType = drugTypes.GetValueOrDefault(row.DrugTypeId);
            return new CommercialNameListItem(row.Id, row.DrugTypeId, drugType?.En ?? string.Empty, drugType?.Ar ?? string.Empty, row.Name.En, row.Name.Ar, row.IsActive);
        }).ToList();
    }
}

internal sealed class AntibioticRepository(AppDbContext context) : Repository<Antibiotic>(context), IAntibioticRepository
{
    private static readonly SortColumns<Antibiotic> Sorts = new SortColumns<Antibiotic>(CatalogueSortKeys.Antibiotics)
        .Column("code", antibiotic => antibiotic.Code)
        .Column("nameEn", antibiotic => antibiotic.Name.En)
        .Column("nameAr", antibiotic => antibiotic.Name.Ar)
        .ThenBy(antibiotic => antibiotic.Code);

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) =>
        Set.AnyAsync(antibiotic => antibiotic.Code == code, cancellationToken);

    public Task<Paged<AntibioticListItem>> ListAsync(CatalogueBFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking().Active(filter.IsActive);
        if (CatalogueBSql.SearchPattern(filter.Search) is { } pattern)
        {
            query = query.Where(antibiotic => EF.Functions.ILike(antibiotic.Code, pattern, CatalogueBListing.Escape)
                                              || EF.Functions.ILike(antibiotic.Abbreviation, pattern, CatalogueBListing.Escape)
                                              || EF.Functions.ILike(antibiotic.Name.En, pattern, CatalogueBListing.Escape)
                                              || EF.Functions.ILike(antibiotic.Name.Ar, pattern, CatalogueBListing.Escape));
        }

        return CatalogueBListing.PageAsync(Sorts.Apply(query, filter.Sort, rows => rows.OrderBy(antibiotic => antibiotic.Code)), page, rows => MapAsync(rows, cancellationToken), cancellationToken);
    }

    public async Task<AntibioticListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken)
    {
        var rows = await Set.AsNoTracking().Where(antibiotic => antibiotic.Id == id).ToListAsync(cancellationToken).ConfigureAwait(false);
        return (await MapAsync(rows, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
    }

    private async Task<IReadOnlyList<AntibioticListItem>> MapAsync(IReadOnlyList<Antibiotic> rows, CancellationToken cancellationToken)
    {
        var ids = rows.Where(row => row.CommercialNameId != null).Select(row => row.CommercialNameId!.Value).Distinct().ToList();
        var names = await Context.Set<CommercialName>().AsNoTracking().Where(commercialName => ids.Contains(commercialName.Id))
                .Select(commercialName => new { commercialName.Id, commercialName.Name.En, commercialName.Name.Ar })
                .ToDictionaryAsync(commercialName => commercialName.Id, cancellationToken)
                .ConfigureAwait(false);
        return rows.Select(row =>
        {
            var name = row.CommercialNameId is { } commercialNameId && names.TryGetValue(commercialNameId, out var found) ? found : null;
            return new AntibioticListItem(row.Id, row.Code, row.Name.En, row.Name.Ar, row.Abbreviation, row.CommercialNameId, name?.En, name?.Ar, row.IsActive);
        }).ToList();
    }
}

internal sealed class OrganismRepository(AppDbContext context) : Repository<Organism>(context), IOrganismRepository
{
    private static readonly SortColumns<Organism> Sorts = new SortColumns<Organism>(CatalogueSortKeys.Organisms)
        .Column("code", organism => organism.Code)
        .Column("nameEn", organism => organism.Name.En)
        .Column("nameAr", organism => organism.Name.Ar)
        .ThenBy(organism => organism.Code);

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) =>
        Set.AnyAsync(organism => organism.Code == code, cancellationToken);

    public Task<Paged<OrganismListItem>> ListAsync(CatalogueBFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking().Active(filter.IsActive);
        if (CatalogueBSql.SearchPattern(filter.Search) is { } pattern)
        {
            query = query.Where(organism => EF.Functions.ILike(organism.Code, pattern, CatalogueBListing.Escape)
                                            || EF.Functions.ILike(organism.Name.En, pattern, CatalogueBListing.Escape)
                                            || EF.Functions.ILike(organism.Name.Ar, pattern, CatalogueBListing.Escape));
        }

        return CatalogueBListing.PageAsync(Sorts.Apply(query, filter.Sort, rows => rows.OrderBy(organism => organism.Code)), page, rows => Task.FromResult(Map(rows)), cancellationToken);
    }

    public async Task<OrganismListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken)
    {
        var rows = await Set.AsNoTracking().Where(organism => organism.Id == id).ToListAsync(cancellationToken).ConfigureAwait(false);
        return Map(rows).FirstOrDefault();
    }

    private static IReadOnlyList<OrganismListItem> Map(IReadOnlyList<Organism> rows) =>
        rows.Select(row => new OrganismListItem(row.Id, row.Code, row.Name.En, row.Name.Ar, row.IsActive)).ToList();
}

internal sealed class SensitivityValueRepository(AppDbContext context) : Repository<SensitivityValue>(context), ISensitivityValueRepository
{
    private static readonly SortColumns<SensitivityValue> Sorts = new SortColumns<SensitivityValue>(CatalogueSortKeys.SensitivityValues)
        .Column("displayOrder", value => value.DisplayOrder)
        .Column("code", value => value.Code)
        .Column("nameEn", value => value.Name.En)
        .Column("nameAr", value => value.Name.Ar)
        .ThenBy(value => value.Code);

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) =>
        Set.AnyAsync(value => value.Code == code, cancellationToken);

    public Task<Paged<SensitivityValueListItem>> ListAsync(CatalogueBFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking().Active(filter.IsActive);
        if (CatalogueBSql.SearchPattern(filter.Search) is { } pattern)
        {
            query = query.Where(value => EF.Functions.ILike(value.Code, pattern, CatalogueBListing.Escape)
                                         || EF.Functions.ILike(value.Name.En, pattern, CatalogueBListing.Escape)
                                         || EF.Functions.ILike(value.Name.Ar, pattern, CatalogueBListing.Escape));
        }

        return CatalogueBListing.PageAsync(Sorts.Apply(query, filter.Sort, rows => rows.OrderBy(value => value.DisplayOrder).ThenBy(value => value.Code)), page, rows => Task.FromResult(Map(rows)), cancellationToken);
    }

    public async Task<SensitivityValueListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken)
    {
        var rows = await Set.AsNoTracking().Where(value => value.Id == id).ToListAsync(cancellationToken).ConfigureAwait(false);
        return Map(rows).FirstOrDefault();
    }

    private static IReadOnlyList<SensitivityValueListItem> Map(IReadOnlyList<SensitivityValue> rows) =>
        rows.Select(row => new SensitivityValueListItem(row.Id, row.Code, row.Name.En, row.Name.Ar, row.DisplayOrder, row.IsActive)).ToList();
}

/// <summary>Any Catalogue B record by list, tracked, for the generic activation commands (T3/T4).</summary>
internal sealed class CatalogueBRecords(AppDbContext context) : ICatalogueBRecords
{
    public async Task<CatalogueBRecord?> GetForUpdateAsync(CatalogueBList list, Guid id, CancellationToken cancellationToken) => list switch
    {
        CatalogueBList.Preparations => await FindAsync<Preparation>(id, cancellationToken).ConfigureAwait(false),
        CatalogueBList.CommentTemplates => await FindAsync<CommentTemplate>(id, cancellationToken).ConfigureAwait(false),
        CatalogueBList.Doctors => await FindAsync<Doctor>(id, cancellationToken).ConfigureAwait(false),
        CatalogueBList.Diagnoses => await FindAsync<Diagnosis>(id, cancellationToken).ConfigureAwait(false),
        CatalogueBList.SendingLabs => await FindAsync<SendingLab>(id, cancellationToken).ConfigureAwait(false),
        CatalogueBList.ShortcutSets => await FindAsync<ShortcutSet>(id, cancellationToken).ConfigureAwait(false),
        CatalogueBList.TestRules => await FindAsync<TestRule>(id, cancellationToken).ConfigureAwait(false),
        CatalogueBList.Reasons => await FindAsync<Reason>(id, cancellationToken).ConfigureAwait(false),
        CatalogueBList.ReportTemplates => await FindAsync<ReportTemplate>(id, cancellationToken).ConfigureAwait(false),
        CatalogueBList.DrugTypes => await FindAsync<DrugType>(id, cancellationToken).ConfigureAwait(false),
        CatalogueBList.CommercialNames => await FindAsync<CommercialName>(id, cancellationToken).ConfigureAwait(false),
        CatalogueBList.Antibiotics => await FindAsync<Antibiotic>(id, cancellationToken).ConfigureAwait(false),
        CatalogueBList.Organisms => await FindAsync<Organism>(id, cancellationToken).ConfigureAwait(false),
        CatalogueBList.SensitivityValues => await FindAsync<SensitivityValue>(id, cancellationToken).ConfigureAwait(false),
        _ => null,
    };

    private Task<TRecord?> FindAsync<TRecord>(Guid id, CancellationToken cancellationToken)
        where TRecord : CatalogueBRecord =>
        context.Set<TRecord>().FirstOrDefaultAsync(record => record.Id == id, cancellationToken);
}

/// <summary>The other modules' records Catalogue B links to (same-wave tables, raw SQL, bounded to the current tenant).</summary>
internal sealed class CatalogueBReferences(AppDbContext context) : ICatalogueBReferences
{
    public Task<IReadOnlyList<Guid>> MissingServicesAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken) =>
        CatalogueBSql.MissingAsync(context, CatalogueBSql.ExistingServicesSql, serviceIds, cancellationToken);

    public Task<IReadOnlyDictionary<Guid, LinkedServiceView>> ServicesAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken) =>
        CatalogueBSql.ServicesAsync(context, serviceIds, cancellationToken);

    public async Task<bool> ClinicalGroupExistsAsync(Guid clinicalGroupId, CancellationToken cancellationToken) =>
        (await CatalogueBSql.MissingAsync(context, CatalogueBSql.ExistingClinicalGroupsSql, [clinicalGroupId], cancellationToken).ConfigureAwait(false)).Count == 0;

    public Task<IReadOnlyList<Guid>> MissingBranchesAsync(IReadOnlyCollection<Guid> branchIds, CancellationToken cancellationToken) =>
        CatalogueBSql.MissingAsync(context, CatalogueBSql.ExistingBranchesSql, branchIds, cancellationToken);

    public async Task<bool> PayerExistsAsync(Guid payerId, CancellationToken cancellationToken) =>
        (await CatalogueBSql.MissingAsync(context, CatalogueBSql.ExistingPayersSql, [payerId], cancellationToken).ConfigureAwait(false)).Count == 0;

    public Task<bool> ContractTierOfPayerExistsAsync(Guid contractTierId, Guid payerId, CancellationToken cancellationToken) =>
        CatalogueBSql.ContractTierOfPayerExistsAsync(context, contractTierId, payerId, cancellationToken);
}
