using Microsoft.EntityFrameworkCore;
using Platform.Application.Abstractions.Files;
using Platform.Application.Abstractions.Security;
using Platform.Application.Catalogue;
using Platform.Application.Catalogue.Commands;
using Platform.Domain.Catalogue;
using Platform.SharedKernel.Abstractions;

namespace Platform.Infrastructure.Persistence.Catalogue;

/// <summary>
/// <see cref="ICatalogueBCloner"/> (BR-25): copies the source tenant's <b>active</b> Catalogue B templates to the target
/// tenant with fresh UUIDv7 ids, set-based, one statement per list, in the ambient transaction of the provisioning.
/// Service and clinical-group ids are remapped by <c>code</c> through Catalogue A's tables of both tenants (Catalogue A's
/// cloner runs first); drug types and commercial names are remapped by English name. A row whose meaning would change
/// is skipped instead (a preparation or diagnosis that loses every linked service, a comment whose group is missing,
/// a shortcut set without services, a rule that loses a trigger or every added service). Existing rows of the target
/// win (<c>ON CONFLICT DO NOTHING</c>), so a repeated clone adds only what is missing. Doctors and sending labs are
/// laboratory-specific and never copied. Report-template images (logo, signature) are <b>copied</b> into the target
/// tenant's file store under the new template and get new file ids (decision 2026-10-12, intake LIS-INT-CTB-001 A10):
/// a laboratory never reads the platform's files, and a later change of the platform template leaves the laboratory's
/// copy alone; an image whose content is missing is dropped. Files are not transactional: a provisioning that rolls
/// back afterwards leaves unreferenced copies behind (no business deletion, BR-15). The statements bypass the change
/// tracker: the provisioning's own events record the clone. Last, every clinical group of the target that links no report
/// template takes the target's copy of its source group's template (validation Z-02): Catalogue A copies the groups before
/// the templates exist in the target, and a laboratory sets no link in Release 1, so its reports would never use one.
/// </summary>
internal sealed class CatalogueBCloner(AppDbContext context, ICurrentTenant tenant, ICurrentUser currentUser, IClock clock, IFileStore files) : ICatalogueBCloner
{
    private const string Provenance = "Platform provisioning";

    // {0} source tenant, {1} target tenant, {2} source ids (uuid[]), {3} new ids (uuid[]), {4} now, {5} actor id, {6} actor name.
    private const string Map = "JOIN unnest({2}::uuid[], {3}::uuid[]) AS m(old_id, new_id) ON m.old_id = s.id";
    private const string Stamp = "true, {4}::timestamptz, NULLIF({5}, '00000000-0000-0000-0000-000000000000'::uuid), {6}::text";   // {5} = Guid.Empty when no actor (a DBNull parameter has no store type)
    private const string StampColumns = "is_active, created_at_utc, created_by_user_id, created_by_display_name";

    private const string PreparationsSql = $$"""
        INSERT INTO app.preparations (tenant_id, id, code, question_en, question_ar, answer_type, options, gender, service_ids, {{StampColumns}})
        SELECT {1}, m.new_id, s.code, s.question_en, s.question_ar, s.answer_type, s.options, s.gender, r.ids, {{Stamp}}
        FROM app.preparations s
        {{Map}}
        CROSS JOIN LATERAL (SELECT ARRAY(
            SELECT ts.id FROM unnest(s.service_ids) WITH ORDINALITY AS u(service_id, ord)
            JOIN app.services ss ON ss.tenant_id = {0} AND ss.id = u.service_id
            JOIN app.services ts ON ts.tenant_id = {1} AND ts.code = ss.code
            ORDER BY u.ord) AS ids) r
        WHERE s.tenant_id = {0} AND (cardinality(s.service_ids) = 0 OR cardinality(r.ids) > 0)
        ON CONFLICT DO NOTHING
        """;

    private const string CommentTemplatesSql = $$"""
        INSERT INTO app.comment_templates (tenant_id, id, short_name, text_en, text_ar, clinical_group_id, {{StampColumns}})
        SELECT {1}, m.new_id, s.short_name, s.text_en, s.text_ar, tg.id, {{Stamp}}
        FROM app.comment_templates s
        {{Map}}
        LEFT JOIN app.clinical_groups sg ON sg.tenant_id = {0} AND sg.id = s.clinical_group_id
        LEFT JOIN app.clinical_groups tg ON tg.tenant_id = {1} AND tg.code = sg.code
        WHERE s.tenant_id = {0} AND (s.clinical_group_id IS NULL OR tg.id IS NOT NULL)
        ON CONFLICT DO NOTHING
        """;

    private const string DiagnosesSql = $$"""
        INSERT INTO app.diagnoses (tenant_id, id, icd_code, name_en, name_ar, service_ids, {{StampColumns}})
        SELECT {1}, m.new_id, s.icd_code, s.name_en, s.name_ar, r.ids, {{Stamp}}
        FROM app.diagnoses s
        {{Map}}
        CROSS JOIN LATERAL (SELECT ARRAY(
            SELECT ts.id FROM unnest(s.service_ids) WITH ORDINALITY AS u(service_id, ord)
            JOIN app.services ss ON ss.tenant_id = {0} AND ss.id = u.service_id
            JOIN app.services ts ON ts.tenant_id = {1} AND ts.code = ss.code
            ORDER BY u.ord) AS ids) r
        WHERE s.tenant_id = {0} AND (cardinality(s.service_ids) = 0 OR cardinality(r.ids) > 0)
        ON CONFLICT DO NOTHING
        """;

    private const string ShortcutSetsSql = $$"""
        INSERT INTO app.shortcut_sets (tenant_id, id, code, name_en, name_ar, service_ids, {{StampColumns}})
        SELECT {1}, m.new_id, s.code, s.name_en, s.name_ar, r.ids, {{Stamp}}
        FROM app.shortcut_sets s
        {{Map}}
        CROSS JOIN LATERAL (SELECT ARRAY(
            SELECT ts.id FROM unnest(s.service_ids) WITH ORDINALITY AS u(service_id, ord)
            JOIN app.services ss ON ss.tenant_id = {0} AND ss.id = u.service_id
            JOIN app.services ts ON ts.tenant_id = {1} AND ts.code = ss.code
            ORDER BY u.ord) AS ids) r
        WHERE s.tenant_id = {0} AND cardinality(r.ids) >= 1
        ON CONFLICT DO NOTHING
        """;

    private const string TestRulesSql = $$"""
        INSERT INTO app.test_rules (tenant_id, id, name_en, name_ar, clinical_group_id, trigger_service_ids, added_service_ids, {{StampColumns}})
        SELECT {1}, m.new_id, s.name_en, s.name_ar, tg.id, rt.ids, ra.ids, {{Stamp}}
        FROM app.test_rules s
        {{Map}}
        LEFT JOIN app.clinical_groups sg ON sg.tenant_id = {0} AND sg.id = s.clinical_group_id
        LEFT JOIN app.clinical_groups tg ON tg.tenant_id = {1} AND tg.code = sg.code
        CROSS JOIN LATERAL (SELECT ARRAY(
            SELECT ts.id FROM unnest(s.trigger_service_ids) WITH ORDINALITY AS u(service_id, ord)
            JOIN app.services ss ON ss.tenant_id = {0} AND ss.id = u.service_id
            JOIN app.services ts ON ts.tenant_id = {1} AND ts.code = ss.code
            ORDER BY u.ord) AS ids) rt
        CROSS JOIN LATERAL (SELECT ARRAY(
            SELECT ts.id FROM unnest(s.added_service_ids) WITH ORDINALITY AS u(service_id, ord)
            JOIN app.services ss ON ss.tenant_id = {0} AND ss.id = u.service_id
            JOIN app.services ts ON ts.tenant_id = {1} AND ts.code = ss.code
            ORDER BY u.ord) AS ids) ra
        WHERE s.tenant_id = {0} AND cardinality(rt.ids) = cardinality(s.trigger_service_ids) AND cardinality(ra.ids) >= 1
        ON CONFLICT DO NOTHING
        """;

    private const string ReasonsSql = $$"""
        INSERT INTO app.reasons (tenant_id, id, kind, name_en, name_ar, {{StampColumns}})
        SELECT {1}, m.new_id, s.kind, s.name_en, s.name_ar, {{Stamp}}
        FROM app.reasons s
        {{Map}}
        WHERE s.tenant_id = {0}
        ON CONFLICT DO NOTHING
        """;

    private const string ReportTemplatesSql = $$"""
        INSERT INTO app.report_templates (tenant_id, id, name_en, name_ar, type, logo_file_id, signature_file_id, show_qr, language, header_text_en, header_text_ar, footer_text_en, footer_text_ar, design, {{StampColumns}})
        SELECT {1}, m.new_id, s.name_en, s.name_ar, s.type, NULL, NULL, s.show_qr, s.language, s.header_text_en, s.header_text_ar, s.footer_text_en, s.footer_text_ar, s.design, {{Stamp}}
        FROM app.report_templates s
        {{Map}}
        WHERE s.tenant_id = {0}
        ON CONFLICT DO NOTHING
        """;

    private const string DrugTypesSql = $$"""
        INSERT INTO app.drug_types (tenant_id, id, name_en, name_ar, {{StampColumns}})
        SELECT {1}, m.new_id, s.name_en, s.name_ar, {{Stamp}}
        FROM app.drug_types s
        {{Map}}
        WHERE s.tenant_id = {0}
        ON CONFLICT DO NOTHING
        """;

    private const string CommercialNamesSql = $$"""
        INSERT INTO app.commercial_names (tenant_id, id, drug_type_id, name_en, name_ar, {{StampColumns}})
        SELECT {1}, m.new_id, td.id, s.name_en, s.name_ar, {{Stamp}}
        FROM app.commercial_names s
        {{Map}}
        JOIN app.drug_types sd ON sd.tenant_id = {0} AND sd.id = s.drug_type_id
        JOIN app.drug_types td ON td.tenant_id = {1} AND lower(td.name_en) = lower(sd.name_en)
        WHERE s.tenant_id = {0}
        ON CONFLICT DO NOTHING
        """;

    private const string AntibioticsSql = $$"""
        INSERT INTO app.antibiotics (tenant_id, id, code, name_en, name_ar, abbreviation, commercial_name_id, {{StampColumns}})
        SELECT {1}, m.new_id, s.code, s.name_en, s.name_ar, s.abbreviation, tc.id, {{Stamp}}
        FROM app.antibiotics s
        {{Map}}
        LEFT JOIN app.commercial_names sc ON sc.tenant_id = {0} AND sc.id = s.commercial_name_id
        LEFT JOIN app.commercial_names tc ON tc.tenant_id = {1} AND lower(tc.name_en) = lower(sc.name_en)
        WHERE s.tenant_id = {0}
        ON CONFLICT DO NOTHING
        """;

    private const string OrganismsSql = $$"""
        INSERT INTO app.organisms (tenant_id, id, code, name_en, name_ar, {{StampColumns}})
        SELECT {1}, m.new_id, s.code, s.name_en, s.name_ar, {{Stamp}}
        FROM app.organisms s
        {{Map}}
        WHERE s.tenant_id = {0}
        ON CONFLICT DO NOTHING
        """;

    private const string SensitivityValuesSql = $$"""
        INSERT INTO app.sensitivity_values (tenant_id, id, code, name_en, name_ar, display_order, {{StampColumns}})
        SELECT {1}, m.new_id, s.code, s.name_en, s.name_ar, s.display_order, {{Stamp}}
        FROM app.sensitivity_values s
        {{Map}}
        WHERE s.tenant_id = {0}
        ON CONFLICT DO NOTHING
        """;

    // Validation Z-02: the clinical groups of the target (Catalogue A's flushed copies, matched by code) take the target's
    // copy of the report template their source group links, matched by the case-insensitive English name (the unique key of
    // report_templates); a group that already links a template keeps it. {0} source tenant, {1} target tenant.
    private const string GroupTemplateLinksSql = """
        UPDATE app.clinical_groups AS tg
        SET report_template_id = tt.id
        FROM app.clinical_groups AS sg
        JOIN app.report_templates AS st ON st.tenant_id = sg.tenant_id AND st.id = sg.report_template_id
        JOIN app.report_templates AS tt ON tt.tenant_id = {1} AND lower(tt.name_en) = lower(st.name_en) AND tt.is_active
        WHERE sg.tenant_id = {0} AND tg.tenant_id = {1} AND tg.code = sg.code AND tg.report_template_id IS NULL
        """;

    // {0} target tenant, {1} target template, {2} logo file id, {3} signature file id (Guid.Empty = none).
    private const string TemplateImagesSql = """
        UPDATE app.report_templates
        SET logo_file_id = NULLIF({2}, '00000000-0000-0000-0000-000000000000'::uuid),
            signature_file_id = NULLIF({3}, '00000000-0000-0000-0000-000000000000'::uuid)
        WHERE tenant_id = {0} AND id = {1}
        """;

    // Links lost: the service references of the copied source rows minus those of the inserted target rows.
    private const string ServiceLinksLostSql = """
        SELECT (
            (SELECT COALESCE(SUM(cardinality(s.service_ids)), 0) FROM app.preparations s WHERE s.tenant_id = {0} AND s.id = ANY({2}))
          + (SELECT COALESCE(SUM(cardinality(s.service_ids)), 0) FROM app.diagnoses s WHERE s.tenant_id = {0} AND s.id = ANY({2}))
          + (SELECT COALESCE(SUM(cardinality(s.service_ids)), 0) FROM app.shortcut_sets s WHERE s.tenant_id = {0} AND s.id = ANY({2}))
          + (SELECT COALESCE(SUM(cardinality(s.trigger_service_ids) + cardinality(s.added_service_ids)), 0) FROM app.test_rules s WHERE s.tenant_id = {0} AND s.id = ANY({2}))
          - (SELECT COALESCE(SUM(cardinality(t.service_ids)), 0) FROM app.preparations t WHERE t.tenant_id = {1} AND t.id = ANY({3}))
          - (SELECT COALESCE(SUM(cardinality(t.service_ids)), 0) FROM app.diagnoses t WHERE t.tenant_id = {1} AND t.id = ANY({3}))
          - (SELECT COALESCE(SUM(cardinality(t.service_ids)), 0) FROM app.shortcut_sets t WHERE t.tenant_id = {1} AND t.id = ANY({3}))
          - (SELECT COALESCE(SUM(cardinality(t.trigger_service_ids) + cardinality(t.added_service_ids)), 0) FROM app.test_rules t WHERE t.tenant_id = {1} AND t.id = ANY({3}))
        )::int AS "Value"
        """;

    private const string GroupLinksLostSql = """
        SELECT (
            (SELECT COUNT(*) FROM app.comment_templates s WHERE s.tenant_id = {0} AND s.id = ANY({2}) AND s.clinical_group_id IS NOT NULL)
          + (SELECT COUNT(*) FROM app.test_rules s WHERE s.tenant_id = {0} AND s.id = ANY({2}) AND s.clinical_group_id IS NOT NULL)
          - (SELECT COUNT(*) FROM app.comment_templates t WHERE t.tenant_id = {1} AND t.id = ANY({3}) AND t.clinical_group_id IS NOT NULL)
          - (SELECT COUNT(*) FROM app.test_rules t WHERE t.tenant_id = {1} AND t.id = ANY({3}) AND t.clinical_group_id IS NOT NULL)
        )::int AS "Value"
        """;

    /// <summary>The lists in dependency order (drug types before commercial names before antibiotics).</summary>
    private static readonly (string List, string Table, string Sql)[] Lists =
    [
        ("preparations", "preparations", PreparationsSql),
        ("commentTemplates", "comment_templates", CommentTemplatesSql),
        ("diagnoses", "diagnoses", DiagnosesSql),
        ("shortcutSets", "shortcut_sets", ShortcutSetsSql),
        ("testRules", "test_rules", TestRulesSql),
        ("reasons", "reasons", ReasonsSql),
        ("reportTemplates", "report_templates", ReportTemplatesSql),
        ("drugTypes", "drug_types", DrugTypesSql),
        ("commercialNames", "commercial_names", CommercialNamesSql),
        ("antibiotics", "antibiotics", AntibioticsSql),
        ("organisms", "organisms", OrganismsSql),
        ("sensitivityValues", "sensitivity_values", SensitivityValuesSql),
    ];

    public async Task<CatalogueBCloneSummary> CloneAsync(Guid sourceTenantId, Guid targetTenantId, CancellationToken cancellationToken)
    {
        if (sourceTenantId == Guid.Empty || targetTenantId == Guid.Empty || sourceTenantId == targetTenantId)
        {
            throw new ArgumentException("The source and target tenants must be two different tenants.", nameof(targetTenantId));
        }

        if (!tenant.IsElevated)
        {
            throw new InvalidOperationException("The Catalogue B clone reads one tenant and writes another: run it inside the provisioning's elevated scope.");
        }

        var now = clock.UtcNow;
        var actorId = currentUser.UserId ?? Guid.Empty;
        var actorName = currentUser.DisplayName ?? Provenance;
        var copied = new Dictionary<string, int>(StringComparer.Ordinal);
        var allOld = new List<Guid>();
        var allNew = new List<Guid>();

        foreach (var (list, table, sql) in Lists)
        {
            var sourceIds = await SourceIdsAsync(table, sourceTenantId, cancellationToken).ConfigureAwait(false);
            if (sourceIds.Count == 0)
            {
                copied[list] = 0;
                continue;
            }

            var newIds = sourceIds.Select(_ => Guid.CreateVersion7()).ToArray();
            copied[list] = await context.Database.ExecuteSqlRawAsync(
                    sql,
                    [sourceTenantId, targetTenantId, sourceIds.ToArray(), newIds, now, actorId, actorName],
                    cancellationToken)
                .ConfigureAwait(false);
            allOld.AddRange(sourceIds);
            allNew.AddRange(newIds);
            if (table == "report_templates")
            {
                await CopyTemplateImagesAsync(sourceTenantId, targetTenantId, sourceIds, newIds, cancellationToken).ConfigureAwait(false);
            }
        }

        await context.Database.ExecuteSqlRawAsync(GroupTemplateLinksSql, [sourceTenantId, targetTenantId], cancellationToken).ConfigureAwait(false);

        var parameters = new object[] { sourceTenantId, targetTenantId, allOld.ToArray(), allNew.ToArray() };
        var servicesLost = await ScalarAsync(ServiceLinksLostSql, parameters, cancellationToken).ConfigureAwait(false);
        var groupsLost = await ScalarAsync(GroupLinksLostSql, parameters, cancellationToken).ConfigureAwait(false);
        return new CatalogueBCloneSummary(copied, servicesLost, groupsLost);
    }

    /// <summary>
    /// Copies the logo and signature of every report template this clone inserted (a row skipped by <c>ON CONFLICT</c>
    /// keeps its own images) into the target tenant's store, under the new template, and stamps the new file ids.
    /// </summary>
    private async Task CopyTemplateImagesAsync(Guid sourceTenantId, Guid targetTenantId, IReadOnlyList<Guid> sourceIds, Guid[] newIds, CancellationToken cancellationToken)
    {
        var sources = await context.Set<ReportTemplate>().IgnoreQueryFilters().AsNoTracking()
            .Where(template => template.TenantId == sourceTenantId && sourceIds.Contains(template.Id) && (template.LogoFileId != null || template.SignatureFileId != null))
            .Select(template => new { template.Id, template.LogoFileId, template.SignatureFileId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (sources.Count == 0)
        {
            return;
        }

        var newIdOf = sourceIds.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => newIds[pair.index]);
        var candidates = sources.Select(source => newIdOf[source.Id]).ToList();
        var inserted = (await context.Set<ReportTemplate>().IgnoreQueryFilters().AsNoTracking()
                .Where(template => template.TenantId == targetTenantId && candidates.Contains(template.Id))
                .Select(template => template.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToHashSet();
        if (inserted.Count == 0)
        {
            return;
        }

        if (tenant.TenantId != targetTenantId)
        {
            throw new InvalidOperationException("Template images are copied into the current tenant's store: run the clone in the target tenant's scope.");
        }

        foreach (var source in sources)
        {
            var targetId = newIdOf[source.Id];
            if (!inserted.Contains(targetId))
            {
                continue;
            }

            var logo = await CopyImageAsync(sourceTenantId, source.Id, source.LogoFileId, targetId, cancellationToken).ConfigureAwait(false);
            var signature = await CopyImageAsync(sourceTenantId, source.Id, source.SignatureFileId, targetId, cancellationToken).ConfigureAwait(false);
            await context.Database.ExecuteSqlRawAsync(TemplateImagesSql, [targetTenantId, targetId, logo ?? Guid.Empty, signature ?? Guid.Empty], cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<Guid?> CopyImageAsync(Guid sourceTenantId, Guid sourceTemplateId, Guid? fileId, Guid targetTemplateId, CancellationToken cancellationToken)
    {
        if (fileId is not { } id)
        {
            return null;
        }

        var sourceKey = FileKeys.Compose(sourceTenantId, ReportTemplateImageStorage.Area, sourceTemplateId, id);
        var copied = await files.CopyAsync(sourceKey, ReportTemplateImageStorage.Area, targetTemplateId, ReportTemplateImageFiles.MaxSizeBytes, cancellationToken).ConfigureAwait(false);
        return copied?.FileId;
    }

    private async Task<IReadOnlyList<Guid>> SourceIdsAsync(string table, Guid sourceTenantId, CancellationToken cancellationToken) =>
        await context.Database.SqlQueryRaw<Guid>(SourceIdsSql(table), sourceTenantId).ToListAsync(cancellationToken).ConfigureAwait(false);

    private async Task<int> ScalarAsync(string sql, object[] parameters, CancellationToken cancellationToken) =>
        (await context.Database.SqlQueryRaw<int>(sql, parameters).ToListAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault();

    /// <summary>The active rows of one list of the source tenant; <paramref name="table"/> is one of the constants of <see cref="Lists"/>.</summary>
    private static string SourceIdsSql(string table) => table switch
    {
        "preparations" => "SELECT id AS \"Value\" FROM app.preparations WHERE tenant_id = {0} AND is_active ORDER BY id",
        "comment_templates" => "SELECT id AS \"Value\" FROM app.comment_templates WHERE tenant_id = {0} AND is_active ORDER BY id",
        "diagnoses" => "SELECT id AS \"Value\" FROM app.diagnoses WHERE tenant_id = {0} AND is_active ORDER BY id",
        "shortcut_sets" => "SELECT id AS \"Value\" FROM app.shortcut_sets WHERE tenant_id = {0} AND is_active ORDER BY id",
        "test_rules" => "SELECT id AS \"Value\" FROM app.test_rules WHERE tenant_id = {0} AND is_active ORDER BY id",
        "reasons" => "SELECT id AS \"Value\" FROM app.reasons WHERE tenant_id = {0} AND is_active ORDER BY id",
        "report_templates" => "SELECT id AS \"Value\" FROM app.report_templates WHERE tenant_id = {0} AND is_active ORDER BY id",
        "drug_types" => "SELECT id AS \"Value\" FROM app.drug_types WHERE tenant_id = {0} AND is_active ORDER BY id",
        "commercial_names" => "SELECT id AS \"Value\" FROM app.commercial_names WHERE tenant_id = {0} AND is_active ORDER BY id",
        "antibiotics" => "SELECT id AS \"Value\" FROM app.antibiotics WHERE tenant_id = {0} AND is_active ORDER BY id",
        "organisms" => "SELECT id AS \"Value\" FROM app.organisms WHERE tenant_id = {0} AND is_active ORDER BY id",
        "sensitivity_values" => "SELECT id AS \"Value\" FROM app.sensitivity_values WHERE tenant_id = {0} AND is_active ORDER BY id",
        _ => throw new ArgumentOutOfRangeException(nameof(table), table, "Not a Catalogue B table."),
    };
}
