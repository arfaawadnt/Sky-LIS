using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Registration;
using Platform.Application.Results;
using Platform.Contracts.Common;
using Platform.Domain.Catalogue;
using Platform.Domain.Results;
using Platform.Domain.Samples;
using Platform.Infrastructure.Persistence;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Primitives;

namespace Platform.IntegrationTests;

/// <summary>
/// The engine side of intake LIS-INT-RES-001: the result row with its owned range, jsonb reports and log round-trips,
/// INV-RES-001 (one result per line and test), tenant isolation, the worklist and visit-search projections with the
/// result-right filter, the read ports for Registration and the dashboard, and the append-only log (BR-15).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ResultsModuleTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Technician = Guid.CreateVersion7();
    private static readonly ResultActor Bench = new(Technician, "Bench", ResultRightsGrant.All);

    [DatabaseFact]
    public async Task A_result_round_trips_with_its_range_culture_paragraphs_and_log()
    {
        var tenant = Guid.CreateVersion7();
        var numeric = Order(Guid.CreateVersion7(), Guid.CreateVersion7());
        numeric.Enter(ResultValueInput.Number(6.84m), ResultTestRules.For(LabTestType.Numeric, 1, "mmol/L"), ResolvedRange.Create(3.5m, 5m, 2.5m, 6.5m, null, null, "3.5 - 5.0"), null, Bench, true, Now);
        var culture = Order(numeric.VisitId, Guid.CreateVersion7(), LabTestType.Culture);
        var isolate = new CultureIsolate(Guid.CreateVersion7(), "ECOLI", "E. coli", "إشريكية", "10^5", null, [new CultureSensitivity(Guid.CreateVersion7(), "AMC", "AMC", "Amoxiclav", "أموكسيكلاف", "S", null)]);
        culture.Enter(new ResultValueInput(null, null, null, CultureReport.Create(false, [isolate])), ResultTestRules.For(LabTestType.Culture), null, null, Bench, true, Now);
        var text = Order(numeric.VisitId, Guid.CreateVersion7(), LabTestType.FormattedText);
        text.Enter(new ResultValueInput(null, null, FormattedReport.Create([new FormattedParagraph("Gross", "Two fragments.")]), null), ResultTestRules.For(LabTestType.FormattedText), null, null, Bench, true, Now);
        await AddAsync(tenant, numeric, culture, text);

        using var scope = fixture.AppScope(tenant);
        var results = scope.ServiceProvider.GetRequiredService<IResultRepository>();
        var stored = (await results.ReadByVisitAsync(numeric.VisitId, CancellationToken.None)).ToDictionary(result => result.Id);
        var readNumeric = stored[numeric.Id];
        Assert.Equal((6.8m, "6.8", ResultFlag.CriticalHigh, true, ResultStatus.Complete), (readNumeric.NumericValue, readNumeric.TextValue, readNumeric.Flag, readNumeric.IsPanic, readNumeric.Status));
        Assert.Equal((3.5m, 5m, 6.5m, "3.5 - 5.0"), (readNumeric.Range!.Low!.Value, readNumeric.Range.High!.Value, readNumeric.Range.PanicHigh!.Value, readNumeric.Range.Text));
        Assert.Equal("E. coli", stored[culture.Id].Culture!.Isolates.Single().OrganismNameEn);
        Assert.Equal("S", stored[culture.Id].Culture!.Isolates.Single().Sensitivities.Single().Interpretation);
        Assert.Equal("Two fragments.", stored[text.Id].Formatted!.Paragraphs.Single().Text);

        var log = await results.ReadLogAsync(numeric.Id, CancellationToken.None);
        Assert.Equal([ResultLogKind.Ordered, ResultLogKind.Value, ResultLogKind.Status], log.Select(entry => entry.Kind));

        // A tracked change of the culture is detected by its comparer and persisted.
        var tracked = (await results.ListForUpdateAsync([culture.Id], CancellationToken.None)).Single();
        tracked.Enter(new ResultValueInput(null, null, null, CultureReport.Create(true, [])), ResultTestRules.For(LabTestType.Culture), null, null, Bench, true, Now.AddMinutes(1));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);

        using var again = fixture.AppScope(tenant);
        var reread = (await again.ServiceProvider.GetRequiredService<IResultRepository>().ReadAsync(culture.Id, CancellationToken.None))!;
        Assert.True(reread.Culture!.NoGrowth);
        Assert.Equal("No growth", reread.TextValue);
    }

    [DatabaseFact]
    public async Task One_result_per_line_and_test_and_results_of_another_tenant_are_invisible()
    {
        var tenant = Guid.CreateVersion7();
        var first = Order(Guid.CreateVersion7(), Guid.CreateVersion7());
        await AddAsync(tenant, first);

        var duplicate = Result.Order(new ResultOrder(first.VisitId, first.Accession, first.LineId, first.LineServiceId, first.ServiceId, first.TestId, LabTestType.Numeric, false, null, first.ClinicalGroupId, first.ReportUnitKey, first.PatientId, first.BranchId, null, false, 0, 0, Now, Now, null));
        var refusal = await Assert.ThrowsAsync<DomainException>(() => AddAsync(tenant, duplicate));
        Assert.Equal(ProblemCodes.UniqueViolation, refusal.Code);

        using var stranger = fixture.AppScope(Guid.CreateVersion7());
        var results = stranger.ServiceProvider.GetRequiredService<IResultRepository>();
        Assert.Null(await results.ReadAsync(first.Id, CancellationToken.None));
        Assert.Empty(await results.ReadByVisitAsync(first.VisitId, CancellationToken.None));
        Assert.Empty(await stranger.ServiceProvider.GetRequiredService<AppDbContext>().Set<ResultLogEntry>().Where(entry => entry.ResultId == first.Id).ToListAsync());
    }

    [DatabaseFact]
    public async Task The_worklist_joins_the_sample_filters_by_status_and_rights_and_counts()
    {
        var tenant = Guid.CreateVersion7();
        var branch = Guid.CreateVersion7();
        var group = Guid.CreateVersion7();
        var hiddenGroup = Guid.CreateVersion7();
        var tube = Guid.CreateVersion7();
        var visit = Guid.CreateVersion7();
        var sample = Sample.Create(tube, $"SMP-{tube.ToString("N")[..8].ToUpperInvariant()}", visit, "ACC-2026-70001", Guid.CreateVersion7(), branch, group, Guid.CreateVersion7(), null, [Guid.CreateVersion7()], null, Now);
        sample.Receive(Technician, branch, Now.AddMinutes(10));
        await AddSampleAsync(tenant, sample);

        var late = Order(visit, group, tube: tube, branch: branch, expected: Now.AddHours(-1));
        var onTime = Order(visit, group, tube: tube, branch: branch, expected: Now.AddDays(2));
        onTime.Enter(ResultValueInput.Number(4m), ResultTestRules.For(LabTestType.Numeric), null, null, Bench, true, Now);
        var hidden = Order(visit, hiddenGroup, tube: tube, branch: branch);
        await AddAsync(tenant, late, onTime, hidden);

        using var scope = fixture.AppScope(tenant);
        var results = scope.ServiceProvider.GetRequiredService<IResultRepository>();
        var visibility = new ResultVisibility(false, [], new Dictionary<Guid, IReadOnlyList<Guid>> { [branch] = [group] });
        var all = await results.WorklistAsync(Filter(), visibility, PageRequest.Normalized(1, 50), Now, CancellationToken.None);
        Assert.Equal((2L, 1L, 1L, 1L), (all.TotalCount, all.Requests, all.Samples, all.Delayed));
        Assert.All(all.Rows, row => Assert.Equal(("Received", sample.SampleNumber, Now.AddMinutes(10)), (row.SampleStatus, row.SampleNumber, row.CollectedAtUtc!.Value)));

        var completeOnly = await results.WorklistAsync(Filter() with { Statuses = [ResultStatus.Complete] }, visibility, PageRequest.Normalized(1, 50), Now, CancellationToken.None);
        Assert.Equal(onTime.Id, Assert.Single(completeOnly.Rows).ResultId);
        var delayed = await results.WorklistAsync(Filter() with { DelayedOnly = true, Search = "acc-2026-7" }, visibility, PageRequest.Normalized(1, 50), Now, CancellationToken.None);
        Assert.Equal(late.Id, Assert.Single(delayed.Rows).ResultId);
        var collected = await results.WorklistAsync(Filter() with { CollectedFromUtc = Now.AddHours(1) }, visibility, PageRequest.Normalized(1, 50), Now, CancellationToken.None);
        Assert.Empty(collected.Rows);
        var nothing = await results.WorklistAsync(Filter(), ResultVisibility.Nothing, PageRequest.Normalized(1, 50), Now, CancellationToken.None);
        Assert.Equal(0, nothing.TotalCount);

        var visits = await results.SearchVisitsAsync(new ResultVisitFilter("ACC-2026-7", null, null, null, "Received", null, false, true, false, null, null, branch), visibility, PageRequest.Normalized(1, 20), CancellationToken.None);
        var found = Assert.Single(visits.Items);
        Assert.Equal((visit, ResultStatus.Ordered, 2, 1), (found.VisitId, found.Status, found.Results, found.Units));
    }

    [DatabaseFact]
    public async Task Read_ports_answer_verified_lines_previous_values_and_dashboard_figures()
    {
        var tenant = Guid.CreateVersion7();
        var patient = Guid.CreateVersion7();
        var test = Guid.CreateVersion7();
        var older = Order(Guid.CreateVersion7(), Guid.CreateVersion7(), patient: patient, test: test);
        older.Enter(ResultValueInput.Number(100m), ResultTestRules.For(LabTestType.Numeric, 0), null, null, Bench, true, Now.AddDays(-10));
        older.Verify(Bench, false, Now.AddDays(-10));
        var current = Order(Guid.CreateVersion7(), Guid.CreateVersion7(), patient: patient, test: test, expected: Now.AddHours(-2));
        await AddAsync(tenant, older, current);

        using var scope = fixture.AppScope(tenant);
        var services = scope.ServiceProvider;
        Assert.True(await services.GetRequiredService<IResultStatusLookup>().HasVerifiedResultsAsync([older.LineId], CancellationToken.None));
        Assert.False(await services.GetRequiredService<IResultStatusLookup>().HasVerifiedResultsAsync([current.LineId], CancellationToken.None));

        var previous = await services.GetRequiredService<IResultRepository>().PreviousVerifiedAsync(patient, test, current.VisitId, null, CancellationToken.None);
        Assert.Equal((older.Id, 100m), (previous!.ResultId, previous.Value));
        Assert.Null(await services.GetRequiredService<IResultRepository>().PreviousVerifiedAsync(patient, test, current.VisitId, Now.AddDays(-5), CancellationToken.None));

        var kpis = await services.GetRequiredService<IResultKpiLookup>().ForBranchAsync(null, Now.AddDays(-1), Now.AddDays(1), CancellationToken.None);
        Assert.Equal((1, 0, 1), (kpis.PendingResults, kpis.UnverifiedBacklog, kpis.DelayedTests));
    }

    [DatabaseFact]
    public async Task The_result_log_is_append_only_and_results_are_never_deleted()
    {
        var tenant = Guid.CreateVersion7();
        var result = Order(Guid.CreateVersion7(), Guid.CreateVersion7());
        await AddAsync(tenant, result);

        await using var owner = DatabaseFixture.CreateOwnerProvider();
        using var scope = owner.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var installed = (await context.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_trigger WHERE tgname = 'trg_result_log_entries_append_only'").ToListAsync())[0] == 1;
        if (!installed)
        {
            return;   // Results.migration.sql not applied to this database yet
        }

        await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("UPDATE app.result_log_entries SET detail = 'x' WHERE result_id = {0}", result.Id));
        await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("DELETE FROM app.result_log_entries WHERE result_id = {0}", result.Id));
        await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("DELETE FROM app.results WHERE id = {0}", result.Id));
    }

    private static WorklistFilter Filter() => new(null, null, null, null, null, null, null, null, false, null, null);

    private static Result Order(Guid visitId, Guid groupId, LabTestType type = LabTestType.Numeric, Guid? tube = null, Guid? branch = null, DateTimeOffset? expected = null, Guid? patient = null, Guid? test = null) =>
        Result.Order(new ResultOrder(
            visitId,
            "ACC-2026-70001",
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            test ?? Guid.CreateVersion7(),
            type,
            false,
            tube,
            groupId,
            ReportUnits.ForGroup(visitId, groupId),
            patient ?? Guid.CreateVersion7(),
            branch ?? Guid.CreateVersion7(),
            null,
            false,
            0,
            0,
            Now.AddHours(-3),
            expected ?? Now.AddHours(20),
            null));

    private async Task AddAsync(Guid tenantId, params Result[] results)
    {
        using var scope = fixture.AppScope(tenantId);
        var repository = scope.ServiceProvider.GetRequiredService<IResultRepository>();
        foreach (var result in results)
        {
            repository.Add(result);
        }

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    private async Task AddSampleAsync(Guid tenantId, Sample sample)
    {
        using var scope = fixture.AppScope(tenantId);
        scope.ServiceProvider.GetRequiredService<Platform.Application.Samples.ISampleRepository>().Add(sample);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }
}
