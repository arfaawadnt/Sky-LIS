using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Application.Catalogue;
using Platform.Application.Organization;
using Platform.Application.Pricing;
using Platform.Application.Reporting;
using Platform.Contracts.Reporting;
using Platform.Domain.Organization;
using Platform.Domain.Registration;

namespace Platform.Infrastructure.Persistence.Reporting;

/// <summary>
/// <see cref="IWorkAmountReader"/> over Registration's tables (an earlier wave, so the entity types are used; MODULES.md
/// §5): the live (Ordered, Reserved) top-level lines of the visits of one branch registered in <c>[from, to)</c>. The
/// tenant query filter of both entity types and RLS bound the read to the current tenant; nothing is elevated. Package
/// member lines carry no amounts (the package line does) and are left out so services are not counted twice.
/// </summary>
internal sealed class WorkAmountReader(AppDbContext context) : IWorkAmountReader
{
    public async Task<IReadOnlyList<WorkAmountLine>> ReadAsync(Guid branchId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        if (fromUtc >= toUtc)
        {
            return [];
        }

        return await (
            from visit in context.Set<Visit>().AsNoTracking()
            join line in context.Set<VisitLine>().AsNoTracking() on visit.Id equals line.VisitId
            where visit.BranchId == branchId
                  && visit.RegisteredAtUtc >= fromUtc
                  && visit.RegisteredAtUtc < toUtc
                  && line.Status != VisitLineStatus.Cancelled
                  && line.Kind != VisitLineKind.PackageMember
            orderby visit.RegisteredAtUtc, visit.Id, line.Sequence
            select new WorkAmountLine(
                visit.Id,
                visit.RegisteredAtUtc,
                visit.BranchId,
                visit.RegisteredByUserId,
                visit.PayerId,
                visit.ContractTierId,
                visit.ReferringDoctorId,
                line.ServiceId,
                line.ServiceCode,
                line.ServiceNameEn,
                line.ServiceNameAr,
                line.ListPrice,
                line.PatientShare,
                line.PayerShare,
                line.Discount)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary><see cref="IWorkAmountNames"/>: the captions of the groups through the owning modules' read ports.</summary>
internal sealed class WorkAmountNames(IBranchLookup branches, IUserDirectory users, IPayerLookup payers, IDoctorLookup doctors, ICatalogueLookup catalogue) : IWorkAmountNames
{
    public async Task<IReadOnlyDictionary<Guid, WorkAmountGroupLabel>> BranchesAsync(IReadOnlyCollection<Guid> branchIds, CancellationToken cancellationToken)
    {
        var found = await branches.GetManyAsync(branchIds, cancellationToken).ConfigureAwait(false);
        return found.ToDictionary(pair => pair.Key, pair => new WorkAmountGroupLabel(pair.Value.NameEn, pair.Value.NameAr, pair.Value.Code));
    }

    public async Task<IReadOnlyDictionary<Guid, WorkAmountGroupLabel>> UsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        var found = await users.GetNamesAsync(userIds, cancellationToken).ConfigureAwait(false);
        return found.ToDictionary(pair => pair.Key, pair => new WorkAmountGroupLabel(pair.Value.FullNameEn ?? pair.Value.DisplayName, pair.Value.FullNameAr ?? pair.Value.FullNameEn ?? pair.Value.DisplayName));
    }

    public async Task<IReadOnlyDictionary<Guid, WorkAmountGroupLabel>> PayersAsync(IReadOnlyCollection<Guid> payerIds, CancellationToken cancellationToken)
    {
        var labels = new Dictionary<Guid, WorkAmountGroupLabel>();
        foreach (var payerId in payerIds.Distinct())
        {
            if (await payers.FindPayerAsync(payerId, cancellationToken).ConfigureAwait(false) is { } payer)
            {
                labels[payerId] = new WorkAmountGroupLabel(payer.NameEn, payer.NameAr);
            }
        }

        return labels;
    }

    public async Task<IReadOnlyDictionary<Guid, WorkAmountGroupLabel>> ContractTiersAsync(IReadOnlyCollection<Guid> contractTierIds, CancellationToken cancellationToken)
    {
        var labels = new Dictionary<Guid, WorkAmountGroupLabel>();
        var payerNames = new Dictionary<Guid, WorkAmountGroupLabel>();
        foreach (var tierId in contractTierIds.Distinct())
        {
            if (await payers.FindTierAsync(tierId, cancellationToken).ConfigureAwait(false) is not { } tier)
            {
                continue;
            }

            if (!payerNames.TryGetValue(tier.PayerId, out var payer))
            {
                var found = await payers.FindPayerAsync(tier.PayerId, cancellationToken).ConfigureAwait(false);
                payer = found is null ? new WorkAmountGroupLabel(string.Empty, string.Empty) : new WorkAmountGroupLabel(found.NameEn, found.NameAr);
                payerNames[tier.PayerId] = payer;
            }

            labels[tierId] = new WorkAmountGroupLabel(Join(payer.LabelEn, tier.NameEn), Join(payer.LabelAr, tier.NameAr));
        }

        return labels;
    }

    public async Task<IReadOnlyDictionary<Guid, WorkAmountGroupLabel>> DoctorsAsync(IReadOnlyCollection<Guid> doctorIds, CancellationToken cancellationToken)
    {
        var found = await doctors.GetManyAsync(doctorIds, cancellationToken).ConfigureAwait(false);
        return found.ToDictionary(doctor => doctor.DoctorId, doctor => new WorkAmountGroupLabel(doctor.NameEn, doctor.NameAr, doctor.Code));
    }

    public async Task<IReadOnlyDictionary<Guid, Guid>> ClinicalGroupOfServicesAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken)
    {
        var services = await catalogue.GetServicesAsync(serviceIds, cancellationToken).ConfigureAwait(false);
        return services.Where(service => service.ClinicalGroupId is not null).ToDictionary(service => service.Id, service => service.ClinicalGroupId!.Value);
    }

    public async Task<IReadOnlyDictionary<Guid, WorkAmountGroupLabel>> ClinicalGroupsAsync(IReadOnlyCollection<Guid> clinicalGroupIds, CancellationToken cancellationToken)
    {
        var groups = await catalogue.GetClinicalGroupsAsync(clinicalGroupIds, cancellationToken).ConfigureAwait(false);
        return groups.ToDictionary(group => group.Id, group => new WorkAmountGroupLabel(group.NameEn, group.NameAr, group.Code));
    }

    private static string Join(string payer, string tier) => string.IsNullOrWhiteSpace(payer) ? tier : $"{payer} · {tier}";
}

/// <summary>
/// <see cref="IReportLetterhead"/>: the laboratory's names from its client record (<c>saas.tenants</c>, readable by id
/// from a tenant session; tolerant while the platform tables are not migrated) and the branch from Organization.
/// </summary>
internal sealed class ReportLetterhead(AppDbContext context) : IReportLetterhead
{
    public const string DefaultLabNameEn = "Laboratory";
    public const string DefaultLabNameAr = "المختبر";

    private const string TenantSql = "SELECT t.name_en, t.name_ar FROM saas.tenants t WHERE t.id = {0}";

    public async Task<WorkAmountLetterhead> GetAsync(Guid branchId, CancellationToken cancellationToken)
    {
        List<LabRow> labs;
        try
        {
            labs = await context.Database.SqlQueryRaw<LabRow>(TenantSql, context.CurrentTenantIdOrNil).ToListAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn or PostgresErrorCodes.InsufficientPrivilege)
        {
            labs = [];
        }

        var branch = branchId == Guid.Empty
            ? null
            : await context.Set<Branch>().AsNoTracking().Where(candidate => candidate.Id == branchId).Select(candidate => new { candidate.Name.En, candidate.Name.Ar }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var lab = labs.FirstOrDefault();
        return new WorkAmountLetterhead(
            string.IsNullOrWhiteSpace(lab?.NameEn) ? DefaultLabNameEn : lab.NameEn,
            string.IsNullOrWhiteSpace(lab?.NameAr) ? DefaultLabNameAr : lab.NameAr,
            branch?.En,
            branch?.Ar);
    }

    private sealed class LabRow
    {
        public string? NameEn { get; set; }

        public string? NameAr { get; set; }
    }
}
