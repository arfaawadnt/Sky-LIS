using Microsoft.EntityFrameworkCore;
using Platform.Application.Catalogue;
using Platform.Domain.Catalogue;

namespace Platform.Infrastructure.Persistence.Catalogue;

/// <summary>
/// <see cref="IOrderRulesLookup"/> over the Catalogue B tables. The tenant's lists are small (tens to hundreds of
/// rows), so the rules are applied in memory by the domain (<see cref="TestRuleEngine"/>, <see cref="Preparation.AppliesTo"/>).
/// </summary>
internal sealed class OrderRulesLookup(AppDbContext context) : IOrderRulesLookup
{
    public async Task<IReadOnlyList<RuleAddedService>> ServicesAddedByRulesAsync(IReadOnlyCollection<Guid> orderedServiceIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orderedServiceIds);
        if (orderedServiceIds.Count == 0)
        {
            return [];
        }

        var rules = await context.Set<TestRule>().AsNoTracking().Where(rule => rule.IsActive).ToListAsync(cancellationToken).ConfigureAwait(false);
        var byId = rules.ToDictionary(rule => rule.Id);
        return TestRuleEngine.Expand(rules, orderedServiceIds)
            .Select(addition => new RuleAddedService(addition.RuleId, byId[addition.RuleId].Name.En, byId[addition.RuleId].Name.Ar, addition.ServiceId))
            .ToList();
    }

    public async Task<DiagnosisServices?> DiagnosisAsync(Guid diagnosisId, CancellationToken cancellationToken)
    {
        var diagnosis = await context.Set<Diagnosis>().AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == diagnosisId && candidate.IsActive, cancellationToken)
            .ConfigureAwait(false);
        return diagnosis is null ? null : new DiagnosisServices(diagnosis.Id, diagnosis.IcdCode, diagnosis.Name.En, diagnosis.Name.Ar, diagnosis.ServiceIds);
    }

    public async Task<IReadOnlyList<PreparationQuestion>> PreparationsForAsync(IReadOnlyCollection<Guid> serviceIds, PreparationGender? patientGender, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceIds);
        var preparations = await context.Set<Preparation>().AsNoTracking().Where(preparation => preparation.IsActive).OrderBy(preparation => preparation.Code)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return preparations
            .Where(preparation => preparation.AppliesTo(serviceIds, patientGender))
            .Select(preparation => new PreparationQuestion(
                preparation.Id, preparation.Code, preparation.Question.En, preparation.Question.Ar, preparation.AnswerType, preparation.Options, preparation.Gender, preparation.ServiceIds))
            .ToList();
    }

    public async Task<ShortcutSetServices?> ShortcutSetByCodeAsync(string code, CancellationToken cancellationToken)
    {
        var wanted = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (wanted.Length == 0)
        {
            return null;
        }

        var set = await context.Set<ShortcutSet>().AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Code == wanted && candidate.IsActive, cancellationToken)
            .ConfigureAwait(false);
        return set is null ? null : new ShortcutSetServices(set.Id, set.Code, set.Name.En, set.Name.Ar, set.ServiceIds);
    }

    public async Task<IReadOnlyList<ReasonOption>> ReasonsAsync(ReasonKind kind, CancellationToken cancellationToken) =>
        await context.Set<Reason>().AsNoTracking()
            .Where(reason => reason.Kind == kind && reason.IsActive)
            .OrderBy(reason => reason.Name.En)
            .Select(reason => new ReasonOption(reason.Id, reason.Kind, reason.Name.En, reason.Name.Ar))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<ReasonOption?> ReasonAsync(Guid reasonId, ReasonKind kind, CancellationToken cancellationToken) =>
        context.Set<Reason>().AsNoTracking()
            .Where(reason => reason.Id == reasonId && reason.Kind == kind && reason.IsActive)
            .Select(reason => new ReasonOption(reason.Id, reason.Kind, reason.Name.En, reason.Name.Ar))
            .FirstOrDefaultAsync(cancellationToken);
}

/// <summary><see cref="IReportTemplateLookup"/> over <c>app.report_templates</c>.</summary>
internal sealed class ReportTemplateLookup(AppDbContext context) : IReportTemplateLookup
{
    public async Task<ReportTemplateView?> GetAsync(Guid templateId, CancellationToken cancellationToken)
    {
        var template = await context.Set<ReportTemplate>().AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == templateId, cancellationToken).ConfigureAwait(false);
        return template is null ? null : View(template);
    }

    public async Task<IReadOnlyList<ReportTemplateView>> ActiveOfTypeAsync(ReportTemplateType type, CancellationToken cancellationToken)
    {
        var templates = await context.Set<ReportTemplate>().AsNoTracking()
            .Where(template => template.Type == type && template.IsActive)
            .OrderBy(template => template.Name.En)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return templates.Select(View).ToList();
    }

    private static ReportTemplateView View(ReportTemplate template) => new(
        template.Id, template.Name.En, template.Name.Ar, template.Type, template.Language, template.LogoFileId, template.SignatureFileId, template.ShowQr,
        template.HeaderText?.En, template.HeaderText?.Ar, template.FooterText?.En, template.FooterText?.Ar, template.IsActive);
}

/// <summary><see cref="IDoctorLookup"/> over <c>app.doctors</c>.</summary>
internal sealed class DoctorLookup(AppDbContext context) : IDoctorLookup
{
    public async Task<DoctorView?> GetAsync(Guid doctorId, CancellationToken cancellationToken)
    {
        var doctor = await context.Set<Doctor>().AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == doctorId, cancellationToken).ConfigureAwait(false);
        return doctor is null ? null : View(doctor);
    }

    public async Task<IReadOnlyList<DoctorView>> GetManyAsync(IReadOnlyCollection<Guid> doctorIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(doctorIds);
        var ids = doctorIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var doctors = await context.Set<Doctor>().AsNoTracking().Where(doctor => ids.Contains(doctor.Id)).OrderBy(doctor => doctor.Code)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return doctors.Select(View).ToList();
    }

    public async Task<bool> IsSelectableAsync(Guid doctorId, Guid branchId, CancellationToken cancellationToken)
    {
        var doctor = await context.Set<Doctor>().AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == doctorId, cancellationToken).ConfigureAwait(false);
        return doctor is not null && doctor.IsAvailableIn(branchId);
    }

    private static DoctorView View(Doctor doctor) =>
        new(doctor.Id, doctor.Code, doctor.Name.En, doctor.Name.Ar, doctor.Specialty, doctor.SharePercent, doctor.BranchIds, doctor.IsActive);
}
