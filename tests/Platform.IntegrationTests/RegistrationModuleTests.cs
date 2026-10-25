using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Catalogue;
using Platform.Application.Patients;
using Platform.Application.Pricing;
using Platform.Application.Registration;
using Platform.Application.Registration.Commands;
using Platform.Application.Registration.Policies;
using Platform.Contracts.Common;
using Platform.Contracts.Registration;
using Platform.Domain.Catalogue;
using Platform.Domain.Organization;
using Platform.Domain.Patients;
using Platform.Domain.Pricing;
using Platform.Domain.Registration;
using Platform.Infrastructure.Persistence;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.IntegrationTests;

/// <summary>
/// The engine side of intake LIS-INT-REG-001: the registration handlers over the real ports (catalogue, pricing, patients),
/// the tables, projections and read ports of the module, the raw-SQL service entry, uniqueness, tenant isolation and the
/// BR-15 triggers.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RegistrationModuleTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task Register_release_add_and_cancel_round_trip_through_the_real_ports()
    {
        var world = await SeedAsync(Guid.CreateVersion7());

        var registered = await HandleAsync<RegisterVisitHandler, VisitRegisteredResponse>(world, handler => handler.Handle(
            Register(world, [new OrderLineRequest(world.Glucose, false), new OrderLineRequest(world.Cbc, true)]), CancellationToken.None));

        Assert.StartsWith("ACC-", registered.Accession, StringComparison.Ordinal);
        Assert.Equal(1, registered.TubeCount);
        Assert.Contains(registered.Warnings, warning => warning.Code == RegistrationCodes.WarningPrecaution);

        using (var scope = fixture.AppScope(world.Tenant, world.Actor, branchId: world.Branch))
        {
            var visit = (await scope.ServiceProvider.GetRequiredService<IVisitRepository>().GetForUpdateAsync(registered.Id, CancellationToken.None))!;
            Assert.Equal((2, 1, 150m, 150m), (visit.Lines.Count, visit.Tubes.Count, visit.TotalListPrice, visit.PatientShareTotal));
            Assert.Equal((world.Payer, world.Tier, world.Branch), (visit.PayerId, visit.ContractTierId, visit.BranchId));
            Assert.Equal("C", visit.Tubes.Single().UnitCode);
            Assert.NotNull(visit.Lines.Single(line => line.ServiceId == world.Glucose).ExpectedResultAtUtc);
        }

        var cbcLine = await LineIdAsync(world, registered.Id, world.Cbc);
        await HandleAsync<ReleaseReservedLinesHandler>(world, handler => handler.Handle(new ReleaseReservedLinesCommand(registered.Id, [cbcLine]), CancellationToken.None));
        var added = await HandleAsync<AddVisitLinesHandler, VisitLinesAddedResponse>(world, handler => handler.Handle(new AddVisitLinesCommand(registered.Id, [new OrderLineRequest(world.Urea, false)], null, false), CancellationToken.None));
        Assert.Equal((1, 0), (added.AddedLines, added.NewTubes));

        var glucoseLine = await LineIdAsync(world, registered.Id, world.Glucose);
        using (var scope = fixture.AppScope(world.Tenant, world.Actor, branchId: world.Branch))
        {
            var handler = ActivatorUtilities.CreateInstance<CancelVisitLinesHandler>(scope.ServiceProvider, (IUserPrivileges)new AllRights());
            await handler.Handle(new CancelVisitLinesCommand(registered.Id, [glucoseLine], null, "Ordered by mistake"), CancellationToken.None);
        }

        using var reader = fixture.AppScope(world.Tenant, world.Actor, branchId: world.Branch);
        var view = (await reader.ServiceProvider.GetRequiredService<IVisitLookup>().GetByAccessionAsync(registered.Accession.ToLowerInvariant(), CancellationToken.None))!;
        Assert.Equal((3, 2), (view.Lines.Count, view.Tubes.Count));
        Assert.Equal("Cancelled", view.Lines.Single(line => line.ServiceId == world.Glucose).Status);
        Assert.Equal(view.Lines.Single(line => line.ServiceId == world.Glucose).TubeId, view.Lines.Single(line => line.ServiceId == world.Urea).TubeId);
        Assert.Equal(130m, view.PatientShareTotal);

        var context = reader.ServiceProvider.GetRequiredService<AppDbContext>();
        var cancelled = await context.Set<VisitLine>().AsNoTracking().SingleAsync(line => line.Id == glucoseLine);
        Assert.Equal(("Ordered by mistake", world.Actor), (cancelled.CancellationReason, cancelled.CancelledByUserId!.Value));

        var prior = await reader.ServiceProvider.GetRequiredService<IVisitRepository>().PriorTestOrdersAsync(world.Patient, [world.GlucoseTest, world.UreaTest], reader.ServiceProvider.GetRequiredService<Platform.SharedKernel.Abstractions.IClock>().UtcNow.AddDays(-1), null, CancellationToken.None);
        Assert.Equal([world.UreaTest], prior.Select(order => order.TestId).ToArray());
    }

    [DatabaseFact]
    public async Task Collected_outside_is_stored_per_tube_and_derived_for_the_visit_LIS_CR_008()
    {
        var world = await SeedAsync(Guid.CreateVersion7());

        var legacy = await HandleAsync<RegisterVisitHandler, VisitRegisteredResponse>(world, handler => handler.Handle(
            Register(world, [new OrderLineRequest(world.Glucose, false)]) with { CollectedOutside = true }, CancellationToken.None));
        var plain = await HandleAsync<RegisterVisitHandler, VisitRegisteredResponse>(world, handler => handler.Handle(
            Register(world, [new OrderLineRequest(world.Urea, false)]), CancellationToken.None));

        using var reader = fixture.AppScope(world.Tenant, world.Actor, branchId: world.Branch);
        var context = reader.ServiceProvider.GetRequiredService<AppDbContext>();
        var tube = await context.Set<VisitTube>().AsNoTracking().SingleAsync(candidate => candidate.VisitId == legacy.Id);
        Assert.True(tube.CollectedOutside);
        Assert.Null(tube.CollectedOutsideAtUtc);
        Assert.False((await context.Set<VisitTube>().AsNoTracking().SingleAsync(candidate => candidate.VisitId == plain.Id)).CollectedOutside);

        var lookup = reader.ServiceProvider.GetRequiredService<IVisitLookup>();
        Assert.True((await lookup.GetByAccessionAsync(legacy.Accession, CancellationToken.None))!.CollectedOutside);
        Assert.False((await lookup.GetByAccessionAsync(plain.Accession, CancellationToken.None))!.CollectedOutside);
    }

    [DatabaseFact]
    public async Task The_frequency_check_reads_previous_visits_of_the_patient()
    {
        var world = await SeedAsync(Guid.CreateVersion7(), glucoseFrequencyDays: 30);
        await HandleAsync<RegisterVisitHandler, VisitRegisteredResponse>(world, handler => handler.Handle(Register(world, [new OrderLineRequest(world.Glucose, false)]), CancellationToken.None));

        var refusal = await Assert.ThrowsAsync<DomainException>(() => HandleAsync<RegisterVisitHandler, VisitRegisteredResponse>(world, handler => handler.Handle(Register(world, [new OrderLineRequest(world.Glucose, false)]), CancellationToken.None)));
        Assert.Equal(RegistrationCodes.FrequencyConfirmationRequired, refusal.Code);

        var confirmed = await HandleAsync<RegisterVisitHandler, VisitRegisteredResponse>(world, handler => handler.Handle(Register(world, [new OrderLineRequest(world.Glucose, false)]) with { ConfirmFrequencyWarnings = true }, CancellationToken.None));
        Assert.Contains(confirmed.Warnings, warning => warning.Code == RegistrationCodes.WarningFrequency);
    }

    [DatabaseFact]
    public async Task Service_entry_resolves_shortcut_numbers_codes_aliases_and_candidates()
    {
        var world = await SeedAsync(Guid.CreateVersion7());
        using var scope = fixture.AppScope(world.Tenant, world.Actor);
        var finder = scope.ServiceProvider.GetRequiredService<IServiceEntryFinder>();

        Assert.Equal(world.Glucose, Assert.Single(await finder.FindExactAsync("12", CancellationToken.None)).Id);
        Assert.Equal(world.Glucose, Assert.Single(await finder.FindExactAsync("glu", CancellationToken.None)).Id);
        Assert.Equal(world.Glucose, Assert.Single(await finder.FindExactAsync("SUGAR", CancellationToken.None)).Id);
        Assert.Empty(await finder.FindExactAsync("OLD", CancellationToken.None));
        Assert.Contains(await finder.SearchAsync("ure", 10, CancellationToken.None), item => item.Id == world.Urea);
        Assert.Equal(2, (await finder.GetManyAsync([world.Glucose, world.Urea], CancellationToken.None)).Count);
    }

    [DatabaseFact]
    public async Task The_register_filters_by_name_mobile_accession_status_and_patient()
    {
        var world = await SeedAsync(Guid.CreateVersion7());
        var registered = await HandleAsync<RegisterVisitHandler, VisitRegisteredResponse>(world, handler => handler.Handle(Register(world, [new OrderLineRequest(world.Glucose, false), new OrderLineRequest(world.Cbc, true)]), CancellationToken.None));

        using var scope = fixture.AppScope(world.Tenant, world.Actor);
        var visits = scope.ServiceProvider.GetRequiredService<IVisitRepository>();
        async Task<long> Count(VisitSearchFilter filter) => (await visits.SearchAsync(filter, PageRequest.Normalized(1, 20), CancellationToken.None)).TotalCount;
        VisitSearchFilter Filter(string? accession = null, string? name = null, string? mobile = null, VisitStatus? status = null, Guid? patientId = null) =>
            new(accession, null, name, mobile, null, null, null, null, status, patientId, null, true);

        Assert.Equal(1, await Count(Filter(name: "mona")));
        Assert.Equal(0, await Count(Filter(name: "nobody")));
        Assert.Equal(1, await Count(Filter(mobile: "0100 123")));
        Assert.Equal(1, await Count(Filter(accession: registered.Accession[^5..])));
        Assert.Equal(1, await Count(Filter(status: VisitStatus.Open)));
        Assert.Equal(0, await Count(Filter(status: VisitStatus.Completed)));
        Assert.Equal(1, await Count(Filter(patientId: world.Patient)));

        var row = Assert.Single((await visits.SearchAsync(Filter(), PageRequest.Normalized(1, 20), CancellationToken.None)).Items);
        Assert.Equal(["Ordered", "Reserved"], row.Lines.Select(line => line.Status).ToArray());
        Assert.Equal(1, await visits.VisitOrdinalAsync(world.Patient, row.RegisteredAtUtc, CancellationToken.None));
    }

    [DatabaseFact]
    public async Task Accession_and_sample_numbers_are_unique_per_tenant()
    {
        var tenant = Guid.CreateVersion7();
        await AddVisitAsync(tenant, "ACC-2026-90001", "SMP-2026-90001");

        var accession = await Assert.ThrowsAsync<DomainException>(() => AddVisitAsync(tenant, "ACC-2026-90001", "SMP-2026-90002"));
        Assert.Equal(ProblemCodes.UniqueViolation, accession.Code);
        var sample = await Assert.ThrowsAsync<DomainException>(() => AddVisitAsync(tenant, "ACC-2026-90003", "SMP-2026-90001"));
        Assert.Equal(ProblemCodes.UniqueViolation, sample.Code);

        await AddVisitAsync(Guid.CreateVersion7(), "ACC-2026-90001", "SMP-2026-90001");   // another tenant may reuse both
    }

    [DatabaseFact]
    public async Task Visits_bookings_and_batches_of_another_tenant_are_invisible()
    {
        var (tenantA, tenantB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var visitId = await AddVisitAsync(tenantA, "ACC-2026-91001", "SMP-2026-91001");
        Guid bookingId;
        Guid batchId;
        using (var scope = fixture.AppScope(tenantA, Guid.CreateVersion7()))
        {
            var booking = Booking.Create("BKG-2026-91001", Guid.CreateVersion7(), new BookingContent(new BookingPatient(null, "Mona", "Female", "01001234567", 30), "Nile St", Now, null, Guid.CreateVersion7(), Guid.CreateVersion7(), [Guid.CreateVersion7()], null), Now);
            var batch = Batch.Register("BTH-2026-91001", Guid.CreateVersion7(), new BatchHeader(BatchKind.Group, Guid.CreateVersion7(), Guid.CreateVersion7(), null, null, "Acme", null, null, null), 1, Guid.CreateVersion7(), Now);
            scope.ServiceProvider.GetRequiredService<IBookingRepository>().Add(booking);
            scope.ServiceProvider.GetRequiredService<IBatchRepository>().Add(batch);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
            (bookingId, batchId) = (booking.Id, batch.Id);
        }

        using var scopeB = fixture.AppScope(tenantB);
        var context = scopeB.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await context.Set<Visit>().IgnoreQueryFilters().CountAsync(visit => visit.Id == visitId));
        Assert.Equal(0, await context.Set<VisitLine>().IgnoreQueryFilters().CountAsync(line => line.VisitId == visitId));
        Assert.Equal(0, await context.Set<VisitTube>().IgnoreQueryFilters().CountAsync(tube => tube.VisitId == visitId));
        Assert.Equal(0, await context.Set<Booking>().IgnoreQueryFilters().CountAsync(booking => booking.Id == bookingId));
        Assert.Equal(0, await context.Set<Batch>().IgnoreQueryFilters().CountAsync(batch => batch.Id == batchId));
        Assert.Null(await scopeB.ServiceProvider.GetRequiredService<IVisitLookup>().GetAsync(visitId, CancellationToken.None));
    }

    [DatabaseFact]
    public async Task Nothing_is_deleted_and_a_cancelled_line_is_frozen_BR15()
    {
        var tenant = Guid.CreateVersion7();
        var visitId = await AddVisitAsync(tenant, "ACC-2026-92001", "SMP-2026-92001");
        Guid lineId;
        using (var scope = fixture.AppScope(tenant, Guid.CreateVersion7()))
        {
            var visit = (await scope.ServiceProvider.GetRequiredService<IVisitRepository>().GetForUpdateAsync(visitId, CancellationToken.None))!;
            lineId = visit.Lines[0].Id;
            visit.CancelLines([lineId], null, "Wrong patient", Guid.CreateVersion7(), Now);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using var raw = fixture.AppScope(tenant);
        var database = raw.ServiceProvider.GetRequiredService<AppDbContext>().Database;
        await Assert.ThrowsAnyAsync<PostgresException>(() => database.ExecuteSqlRawAsync("DELETE FROM app.visit_lines WHERE tenant_id = {0} AND id = {1}", tenant, lineId));
        await Assert.ThrowsAnyAsync<PostgresException>(() => database.ExecuteSqlRawAsync("DELETE FROM app.visits WHERE tenant_id = {0} AND id = {1}", tenant, visitId));
        await Assert.ThrowsAnyAsync<PostgresException>(() => database.ExecuteSqlRawAsync("UPDATE app.visit_lines SET status = 'Ordered' WHERE tenant_id = {0} AND id = {1}", tenant, lineId));
        await Assert.ThrowsAnyAsync<PostgresException>(() => database.ExecuteSqlRawAsync("UPDATE app.visits SET accession = 'ACC-X' WHERE tenant_id = {0} AND id = {1}", tenant, visitId));
    }

    [DatabaseFact]
    public async Task The_merge_policy_repoints_visits_and_keeps_the_original_file()
    {
        var world = await SeedAsync(Guid.CreateVersion7());
        var registered = await HandleAsync<RegisterVisitHandler, VisitRegisteredResponse>(world, handler => handler.Handle(Register(world, [new OrderLineRequest(world.Glucose, false)]), CancellationToken.None));
        var survivor = await RegisterPatientAsync(world.Tenant, "Mona Hassan Ali", "01001234568");
        var merged = new PatientsMerged(survivor, "PAT-2026-00002", world.Patient, "PAT-2026-00001", world.Actor, "Duplicate file", Now);

        using (var scope = fixture.AppScope(world.Tenant, world.Actor))
        {
            await ActivatorUtilities.CreateInstance<RepointVisitsOnPatientsMergedPolicy>(scope.ServiceProvider).HandleAsync(merged, CancellationToken.None);
        }

        using var reader = fixture.AppScope(world.Tenant, world.Actor);
        var view = (await reader.ServiceProvider.GetRequiredService<IVisitLookup>().GetAsync(registered.Id, CancellationToken.None))!;
        Assert.Equal((survivor, world.Patient), (view.PatientId, view.OriginalPatientId!.Value));
    }

    [DatabaseFact]
    public async Task Bookings_and_batch_registers_project_with_sums()
    {
        var world = await SeedAsync(Guid.CreateVersion7());
        var batch = await HandleAsync<RegisterBatchHandler, BatchRegisteredResponse>(world, handler => handler.Handle(
            new RegisterBatchCommand("Group", world.Payer, world.Tier, null, null, "Acme staff", null, [world.Glucose], [new BatchMemberRequest(world.Patient, null, false)], false, 120),
            CancellationToken.None));

        using var scope = fixture.AppScope(world.Tenant, world.Actor);
        var page = await scope.ServiceProvider.GetRequiredService<IBatchRepository>().ListAsync(new BatchFilter(null, null, null, "acme", null), PageRequest.Normalized(1, 10), CancellationToken.None);
        var row = Assert.Single(page.Items);
        Assert.Equal((batch.Id, 1, 100m), (row.Batch.Id, row.Batch.MemberCount, row.PatientShareTotal));
        var members = await scope.ServiceProvider.GetRequiredService<IVisitRepository>().ListByBatchAsync(batch.Id, CancellationToken.None);
        Assert.Equal(VisitLocationType.Batch, Assert.Single(members).LocationType);
    }

    // ---- seeding ---------------------------------------------------------------------------------------------------

    private sealed record World(Guid Tenant, Guid Actor, Guid Branch, Guid Patient, Guid Payer, Guid Tier, Guid Glucose, Guid Urea, Guid Cbc, Guid GlucoseTest, Guid UreaTest);

    /// <summary>Chemistry (serum) GLU (shortcut 12, alias Sugar) + UREA, haematology (whole blood) CBC, an inactive OLD; the cash payer with the default list (GLU 100, UREA 80, CBC 50); a patient.</summary>
    private async Task<World> SeedAsync(Guid tenant, int? glucoseFrequencyDays = null)
    {
        var actor = Guid.CreateVersion7();
        var branch = Guid.CreateVersion7();
        Guid glucose, urea, cbc, glucoseTest, ureaTest, payer, tier;
        using (var scope = fixture.AppScope(tenant, actor, branchId: branch))
        {
            var provider = scope.ServiceProvider;
            var chemistry = ClinicalGroup.Create("CHEM", "C", new LocalizedText("Chemistry", "الكيمياء"), null, null, 1, Now);
            var haematology = ClinicalGroup.Create("HEMA", "H", new LocalizedText("Haematology", "أمراض الدم"), null, null, 2, Now);
            var serum = SampleType.Create("SERUM", "SER", new LocalizedText("Serum", "مصل"), [], Now);
            var blood = SampleType.Create("WB", "WB", new LocalizedText("Whole blood", "دم كامل"), [], Now);
            provider.GetRequiredService<IClinicalGroupRepository>().Add(chemistry);
            provider.GetRequiredService<IClinicalGroupRepository>().Add(haematology);
            provider.GetRequiredService<ISampleTypeRepository>().Add(serum);
            provider.GetRequiredService<ISampleTypeRepository>().Add(blood);

            var tests = provider.GetRequiredService<ILabTestRepository>();
            LabTest Test(string code, ClinicalGroup group)
            {
                var test = LabTest.Create(code, new LabTestDefinition(new LocalizedText(code, code), null, null, null, group.Id, LabTestType.Numeric, null, null, null, GenderRestriction.Any, null, 0, false, true, 1, null), Now);
                tests.Add(test);
                return test;
            }

            var glu = Test("GLU", chemistry);
            if (glucoseFrequencyDays is { } days)
            {
                glu.SetTiming(new LabTestTiming(24, 0, days, FrequencyCheckAction.Alert, null, null, LabTestTiming.EveryDay), Now);
            }

            var ure = Test("UREA", chemistry);
            var hb = Test("HB", haematology);
            var old = Test("OLDT", chemistry);

            var services = provider.GetRequiredService<IServiceRepository>();
            Service Add(string code, int sequence, int? shortcut, string[] aliases, ClinicalGroup group, SampleType sample, LabTest test, string? precaution = null)
            {
                var service = Service.Create(code, ServiceType.Test, new ServiceDefinition(code, shortcut, new LocalizedText(code + " service", "خدمة " + code), null, null, null, group.Id, sample.Id, null, null, aliases, precaution, GenderRestriction.Any, 0, 1, false, true, false, null), sequence, [new ResolvedServiceMember(ServiceMemberKind.Test, test.Id, 1, false, true, LabTestType.Numeric, null)], Now);
                services.Add(service);
                return service;
            }

            var gluService = Add("GLU", 1, 12, ["Sugar"], chemistry, serum, glu, "Fasting 8 hours");
            var ureaService = Add("UREA", 2, null, [], chemistry, serum, ure);
            var cbcService = Add("CBC", 3, null, [], haematology, blood, hb);
            var oldService = Add("OLD", 4, null, [], chemistry, serum, old);
            oldService.Deactivate(Now);

            var cash = Payer.CreateDefaultCash("CASH", Now);
            provider.GetRequiredService<AppDbContext>().Set<Payer>().Add(cash);
            var list = BasePriceList.Create("L2026", new LocalizedText("List 2026", "قائمة"), 2026, Now);
            list.UpsertLines([new PriceLineInput(gluService.Id, 100m, true), new PriceLineInput(ureaService.Id, 80m, true), new PriceLineInput(cbcService.Id, 50m, true)], Now);
            list.Activate(Now);
            provider.GetRequiredService<AppDbContext>().Set<BasePriceList>().Add(list);

            await provider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
            (glucose, urea, cbc, glucoseTest, ureaTest, payer, tier) = (gluService.Id, ureaService.Id, cbcService.Id, glu.Id, ure.Id, cash.Id, cash.Tiers.Single().Id);
        }

        var patient = await RegisterPatientAsync(tenant, "Mona Hassan", "01001234567");
        return new World(tenant, actor, branch, patient, payer, tier, glucose, urea, cbc, glucoseTest, ureaTest);
    }

    private async Task<Guid> RegisterPatientAsync(Guid tenant, string fullName, string mobile)
    {
        using var scope = fixture.AppScope(tenant, Guid.CreateVersion7());
        var number = await scope.ServiceProvider.GetRequiredService<IReferenceNumbers>().NextAsync(Patient.NumberKind, Patient.NumberPrefix, Now, CancellationToken.None);
        var patient = Patient.Register(number, new PatientDemographics(PatientTitle.Mrs, fullName, null, PatientGender.Female, new DateOnly(1990, 1, 1), null, null, mobile, null, null, null, null, null, null, null), PatientFieldRules.Optional, false, Now);
        scope.ServiceProvider.GetRequiredService<IPatientRepository>().Add(patient);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return patient.Id;
    }

    private static RegisterVisitCommand Register(World world, IReadOnlyList<OrderLineRequest> lines) =>
        new(world.Patient, null, null, world.Payer, world.Tier, null, null, null, false, false, false, lines, null, null, false, 120);

    private async Task<TOut> HandleAsync<THandler, TOut>(World world, Func<THandler, Task<TOut>> handle)
        where THandler : class
    {
        using var scope = fixture.AppScope(world.Tenant, world.Actor, "Reception Clerk", world.Branch);
        return await handle(ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider));
    }

    private async Task HandleAsync<THandler>(World world, Func<THandler, Task> handle)
        where THandler : class
    {
        using var scope = fixture.AppScope(world.Tenant, world.Actor, "Reception Clerk", world.Branch);
        await handle(ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider));
    }

    private async Task<Guid> LineIdAsync(World world, Guid visitId, Guid serviceId)
    {
        using var scope = fixture.AppScope(world.Tenant, world.Actor);
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<VisitLine>().Where(line => line.VisitId == visitId && line.ServiceId == serviceId).Select(line => line.Id).SingleAsync();
    }

    /// <summary>A visit straight from the aggregate (no catalogue): one Test line on a chemistry serum tube.</summary>
    private async Task<Guid> AddVisitAsync(Guid tenant, string accession, string sampleNumber)
    {
        using var scope = fixture.AppScope(tenant, Guid.CreateVersion7());
        var tube = new TubeSpec(Guid.CreateVersion7(), Guid.CreateVersion7(), null, "C", "Chemistry", "الكيمياء", "Serum", "مصل", null, null);
        var item = new VisitOrderItem(Guid.CreateVersion7(), "GLU", "Glucose", "جلوكوز", "Test", VisitLineKind.Service, tube, [Guid.CreateVersion7()], 100m, 100m, 0m, Now.AddHours(24), false, VisitLineOrigin.Manual, null, []);
        var visit = Visit.Register(
            new VisitRegistration(accession, Guid.CreateVersion7(), Guid.CreateVersion7(), VisitLocationType.Walkin, null, null, Guid.CreateVersion7(), Guid.CreateVersion7(), null, null, null, null, false, false, false, 0, null),
            [item],
            [],
            null,
            new SampleNumberSource([sampleNumber]),
            Guid.CreateVersion7(),
            Now);
        scope.ServiceProvider.GetRequiredService<IVisitRepository>().Add(visit);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return visit.Id;
    }

    private sealed class AllRights : IUserPrivileges
    {
        public bool Has(string permissionKey) => true;

        public IReadOnlySet<string> PermissionKeys { get; } = new HashSet<string>(StringComparer.Ordinal);

        public SpecialRights SpecialRights => SpecialRights.All;

        public IReadOnlyList<ResultRight> ResultRights { get; } = [];

        public bool CanOnResults(ResultAction action, Guid clinicalGroupId, Guid branchId) => true;
    }
}
