using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Registration;
using Platform.Application.Results;
using Platform.Contracts.Results;
using Platform.Domain.Catalogue;
using Platform.Domain.Organization;
using Platform.Domain.PlatformAdmin;
using Platform.Domain.Pricing;
using Platform.SharedKernel.Abstractions;

namespace Platform.Infrastructure.Persistence.Results;

/// <summary>
/// Integration's <c>app.analysers</c> and <c>app.analyser_test_mappings</c> (same wave 3) by raw SQL on the agreed names:
/// the read first checks that the table and its columns exist (cached once found), so a missing table answers empty.
/// </summary>
internal sealed class ResultAnalyserDirectory(AppDbContext context) : IResultAnalyserDirectory
{
    private static readonly ConcurrentDictionary<string, bool> Installed = new(StringComparer.Ordinal);

    private const string ColumnCountSql = """
        SELECT count(*)::int AS "Value"
        FROM information_schema.columns
        WHERE table_schema = 'app' AND table_name = {0} AND column_name::text = ANY({1})
        """;

    private const string MappedTestsSql = """
        SELECT DISTINCT m.test_id AS "Value"
        FROM app.analyser_test_mappings m
        WHERE m.tenant_id = {0} AND m.analyser_id = {1} AND m.is_active
        """;

    private const string AnalysersSql = """
        SELECT a.id, a.code, a.name_en, a.name_ar
        FROM app.analysers a
        WHERE a.tenant_id = {0} AND a.is_active
        ORDER BY a.code
        """;

    private static readonly string[] MappingColumns = ["tenant_id", "analyser_id", "test_id", "is_active"];
    private static readonly string[] AnalyserColumns = ["id", "tenant_id", "code", "name_en", "name_ar", "is_active"];

    public async Task<IReadOnlyList<Guid>> MappedTestIdsAsync(Guid analyserId, CancellationToken cancellationToken)
    {
        if (!await IsInstalledAsync("analyser_test_mappings", MappingColumns, cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        return await context.Database.SqlQueryRaw<Guid>(MappedTestsSql, context.CurrentTenantIdOrNil, analyserId).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AnalyserOption>> ListAsync(CancellationToken cancellationToken)
    {
        if (!await IsInstalledAsync("analysers", AnalyserColumns, cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        var rows = await context.Database.SqlQueryRaw<AnalyserRow>(AnalysersSql, context.CurrentTenantIdOrNil).ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(row => new AnalyserOption(row.Id, row.Code ?? string.Empty, row.NameEn ?? row.Code ?? string.Empty, row.NameAr ?? row.NameEn ?? string.Empty))];
    }

    private async Task<bool> IsInstalledAsync(string table, string[] columns, CancellationToken cancellationToken)
    {
        var key = $"{table}:{string.Join(',', columns)}";
        if (Installed.ContainsKey(key))
        {
            return true;
        }

        var counts = await context.Database.SqlQueryRaw<int>(ColumnCountSql, table, columns).ToListAsync(cancellationToken).ConfigureAwait(false);
        if ((counts.Count == 0 ? 0 : counts[0]) < columns.Length)
        {
            return false;   // not cached: the table arrives with a later migration
        }

        Installed[key] = true;
        return true;
    }

    private sealed class AnalyserRow
    {
        public Guid Id { get; set; }

        public string? Code { get; set; }

        public string? NameEn { get; set; }

        public string? NameAr { get; set; }
    }
}

/// <summary>Reference names and lists of earlier-wave modules (catalogue B, pricing, organization, catalogue A, the platform tenant record) through their entity types.</summary>
internal sealed class ResultReferenceLookup(AppDbContext context) : IResultReferenceLookup
{
    public const string DefaultLabNameEn = "Laboratory";
    public const string DefaultLabNameAr = "المختبر";

    public async Task<IReadOnlyList<CommentTemplateOption>> CommentTemplatesAsync(string? search, Guid? clinicalGroupId, int take, CancellationToken cancellationToken)
    {
        var query = context.Set<CommentTemplate>().AsNoTracking().Where(template => template.IsActive);
        if (clinicalGroupId is { } groupId)
        {
            query = query.Where(template => template.ClinicalGroupId == null || template.ClinicalGroupId == groupId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var upper = search.Trim().ToUpperInvariant();
            var pattern = "%" + search.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            query = query.Where(template => template.ShortName.StartsWith(upper) || EF.Functions.ILike(template.Text.En, pattern, "\\") || EF.Functions.ILike(template.Text.Ar, pattern, "\\"));
        }

        return await query.OrderBy(template => template.ShortName)
            .Take(Math.Clamp(take, 1, 200))
            .Select(template => new CommentTemplateOption(template.Id, template.ShortName, template.Text.En, template.Text.Ar, template.ClinicalGroupId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<MicrobiologyLookups> MicrobiologyAsync(CancellationToken cancellationToken)
    {
        var organisms = await context.Set<Organism>().AsNoTracking().Where(organism => organism.IsActive).OrderBy(organism => organism.Code)
            .Select(organism => new OrganismOption(organism.Id, organism.Code, organism.Name.En, organism.Name.Ar)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var antibiotics = await context.Set<Antibiotic>().AsNoTracking().Where(antibiotic => antibiotic.IsActive).OrderBy(antibiotic => antibiotic.Code)
            .Select(antibiotic => new AntibioticOption(antibiotic.Id, antibiotic.Code, antibiotic.Abbreviation, antibiotic.Name.En, antibiotic.Name.Ar)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var values = await context.Set<SensitivityValue>().AsNoTracking().Where(value => value.IsActive).OrderBy(value => value.DisplayOrder).ThenBy(value => value.Code)
            .Select(value => new SensitivityOption(value.Id, value.Code, value.Name.En, value.Name.Ar, value.DisplayOrder)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new MicrobiologyLookups(organisms, antibiotics, values);
    }

    public async Task<IReadOnlyDictionary<Guid, CultureOrganismRef>> OrganismsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var keys = ids.Distinct().ToArray();
        if (keys.Length == 0)
        {
            return new Dictionary<Guid, CultureOrganismRef>();
        }

        return await context.Set<Organism>().AsNoTracking().Where(organism => keys.Contains(organism.Id))
            .Select(organism => new CultureOrganismRef(organism.Id, organism.Code, organism.Name.En, organism.Name.Ar, organism.IsActive))
            .ToDictionaryAsync(organism => organism.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<Guid, CultureAntibioticRef>> AntibioticsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var keys = ids.Distinct().ToArray();
        if (keys.Length == 0)
        {
            return new Dictionary<Guid, CultureAntibioticRef>();
        }

        return await context.Set<Antibiotic>().AsNoTracking().Where(antibiotic => keys.Contains(antibiotic.Id))
            .Select(antibiotic => new CultureAntibioticRef(antibiotic.Id, antibiotic.Code, antibiotic.Abbreviation, antibiotic.Name.En, antibiotic.Name.Ar, antibiotic.IsActive))
            .ToDictionaryAsync(antibiotic => antibiotic.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlySet<string>> SensitivityCodesAsync(CancellationToken cancellationToken)
    {
        var codes = await context.Set<SensitivityValue>().AsNoTracking().Where(value => value.IsActive).Select(value => value.Code).ToListAsync(cancellationToken).ConfigureAwait(false);
        return codes.Select(code => code.ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<LookupOption>> ClinicalGroupsAsync(CancellationToken cancellationToken) =>
        await context.Set<ClinicalGroup>().AsNoTracking().Where(group => group.IsActive).OrderBy(group => group.DisplayOrder).ThenBy(group => group.Code)
            .Select(group => new LookupOption(group.Id, group.Code, group.Name.En, group.Name.Ar)).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<LookupOption>> TestsAsync(Guid? clinicalGroupId, string? search, int take, CancellationToken cancellationToken)
    {
        var query = context.Set<LabTest>().AsNoTracking().Where(test => test.IsActive);
        if (clinicalGroupId is { } groupId)
        {
            query = query.Where(test => test.ClinicalGroupId == groupId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var upper = search.Trim().ToUpperInvariant();
            var pattern = "%" + search.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            query = query.Where(test => test.Code.StartsWith(upper) || EF.Functions.ILike(test.Name.En, pattern, "\\") || EF.Functions.ILike(test.Name.Ar, pattern, "\\"));
        }

        return await query.OrderBy(test => test.Code).Take(Math.Clamp(take, 1, 200))
            .Select(test => new LookupOption(test.Id, test.Code, test.Name.En, test.Name.Ar)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LookupOption>> ActivePayersAsync(CancellationToken cancellationToken) =>
        await context.Set<Payer>().AsNoTracking().Where(payer => payer.IsActive).OrderBy(payer => payer.Name.En)
            .Select(payer => new LookupOption(payer.Id, null, payer.Name.En, payer.Name.Ar)).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<ReportLetterhead> LetterheadAsync(Guid branchId, CancellationToken cancellationToken)
    {
        var tenantId = context.CurrentTenantIdOrNil;
        var lab = await context.Set<Tenant>().AsNoTracking().Where(tenant => tenant.Id == tenantId).Select(tenant => new { tenant.Name.En, tenant.Name.Ar }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var branch = branchId == Guid.Empty
            ? null
            : await context.Set<Branch>().AsNoTracking().Where(candidate => candidate.Id == branchId).Select(candidate => new { candidate.Name.En, candidate.Name.Ar }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return new ReportLetterhead(
            string.IsNullOrWhiteSpace(lab?.En) ? DefaultLabNameEn : lab.En,
            string.IsNullOrWhiteSpace(lab?.Ar) ? DefaultLabNameAr : lab.Ar,
            branch?.En,
            branch?.Ar);
    }

    public async Task<IReadOnlyDictionary<Guid, NamedReference>> PayersAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var keys = ids.Distinct().ToArray();
        return keys.Length == 0
            ? new Dictionary<Guid, NamedReference>()
            : await context.Set<Payer>().AsNoTracking().Where(payer => keys.Contains(payer.Id))
                .Select(payer => new NamedReference(payer.Id, null, payer.Name.En, payer.Name.Ar))
                .ToDictionaryAsync(payer => payer.Id, cancellationToken)
                .ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<Guid, NamedReference>> BranchesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var keys = ids.Distinct().ToArray();
        return keys.Length == 0
            ? new Dictionary<Guid, NamedReference>()
            : await context.Set<Branch>().AsNoTracking().Where(branch => keys.Contains(branch.Id))
                .Select(branch => new NamedReference(branch.Id, branch.Code, branch.Name.En, branch.Name.Ar))
                .ToDictionaryAsync(branch => branch.Id, cancellationToken)
                .ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<Guid, NamedReference>> PatientConditionsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var keys = ids.Distinct().ToArray();
        return keys.Length == 0
            ? new Dictionary<Guid, NamedReference>()
            : await context.Set<PatientCondition>().AsNoTracking().Where(condition => keys.Contains(condition.Id))
                .Select(condition => new NamedReference(condition.Id, null, condition.Name.En, condition.Name.Ar))
                .ToDictionaryAsync(condition => condition.Id, cancellationToken)
                .ConfigureAwait(false);
    }

    public async Task<ReportDesign?> ContractReportDesignAsync(Guid contractTierId, CancellationToken cancellationToken) =>
        await context.Set<ContractPricing>().AsNoTracking()
            .Where(pricing => pricing.ContractTierId == contractTierId)
            .Select(pricing => pricing.ReportDesign)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
}

/// <summary>The real adapter of Registration's port (MODULES.md §5): a line with a Verified or Printed result cannot be cancelled (CP-10 / CP-26).</summary>
internal sealed class ResultStatusLookup(AppDbContext context) : IResultStatusLookup
{
    public Task<bool> HasVerifiedResultsAsync(IReadOnlyCollection<Guid> lineIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lineIds);
        return new ResultRepository(context).AnyVerifiedAsync(lineIds, cancellationToken);
    }
}
