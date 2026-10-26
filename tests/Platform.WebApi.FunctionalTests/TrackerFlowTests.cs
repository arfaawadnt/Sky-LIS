using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Organization;
using Platform.Application.Patients;
using Platform.Application.Tracker;
using Platform.Contracts.Billing;
using Platform.Contracts.Common;
using Platform.Contracts.Registration;
using Platform.Contracts.Results;
using Platform.Contracts.Tracker;
using Platform.Domain.Catalogue;
using Platform.Domain.Organization;
using Platform.Domain.Patients;
using Platform.Domain.Pricing;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Persistence.Outbox;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Localization;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// The Lab Tracker over the real API (LIS-CR-006; intake LIS-INT-TRK-001 §16 S-T6…S-T9): visits registered, sampled, resulted,
/// verified, printed, paid and cancelled through their own modules are followed on one page with their stage, next step and
/// time left; the counters show the whole day whatever the stage, attention and view filters; sections follow the module
/// permissions, results the result rights, names the See-VIP right; registration.view (D-04), the session branch
/// (TENANT-004) and the shape (VALIDATION-400) are enforced.
/// </summary>
public sealed class TrackerFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Tracker = "/api/v1/tracker/visits";
    private const string Visits = "/api/v1/registration/visits";
    private const string Results = "/api/v1/results";

    private static readonly string[] DeskPermissions =
    [
        "registration.view", "registration.create", "registration.cancel", "catalogue.view", "samples.view",
        "results.view", "results.create", "results.edit", "results.approve", "results.export", "billing.view", "billing.create",
    ];

    [DatabaseFact]
    public async Task The_tracker_follows_every_visit_of_the_day_through_its_stages()
    {
        var world = await SeedAsync();
        var (desk, _, _) = await factory.SeedUserAsync(tenantId: world.Tenant, permissions: DeskPermissions, displayName: "Front Desk", branchIds: [world.Branch]);
        await GrantAsync(world.Tenant, desk, SpecialRights.All, world.Chemistry);
        var client = await SignedInClientAsync(desk);

        var inLab = await RegisterAsync(client, world, world.Patient, world.Glucose);
        var panic = await RegisterAsync(client, world, world.Patient, world.Glucose, urgent: true);
        var ready = await RegisterAsync(client, world, world.Patient, world.Glucose);
        var delivered = await RegisterAsync(client, world, world.Patient, world.Glucose);
        var hidden = await RegisterAsync(client, world, world.VipPatient, world.Cbc);
        var cancelled = await RegisterAsync(client, world, world.Patient, world.Glucose);
        await DispatchVisitEventsAsync(world.Tenant);

        await EnterAsync(client, panic.Accession, 7.5m);                     // critical high: panic, waits for verification
        await EnterAsync(client, ready.Accession, 4.2m, verify: true);
        await EnterAsync(client, delivered.Accession, 4.4m, verify: true, print: true);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/billing/payments", new RecordPaymentRequest(delivered.Id, 100m, "Cash", null))).StatusCode);
        var line = (await client.GetFromJsonAsync<VisitDetail>($"{Visits}/{cancelled.Id}"))!.Lines.Single().Id;
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"{Visits}/{cancelled.Id}/lines/cancel", new CancelLinesRequest([line], null, "Ordered by mistake"))).StatusCode);

        var day = (await client.GetFromJsonAsync<LabTrackerPage>(Tracker))!;
        Assert.Equal(new LabTrackerCounters(6, 1, 0, 2, 1, 1, 1, 0, 1, 0, 1), day.Counters);
        Assert.Equal((6L, world.Branch), (day.TotalCount, day.BranchId));

        // default order: urgent first, then the minutes left (24 h turnaround), then the visits without a clock
        Assert.Equal([panic.Id, inLab.Id, hidden.Id, ready.Id, delivered.Id, cancelled.Id], day.Items.Select(item => item.VisitId).ToArray());
        var byId = day.Items.ToDictionary(item => item.VisitId);
        Assert.Equal(("InLab", "EnterResults", "OnTime"), (byId[inLab.Id].Stage, byId[inLab.Id].NextStep, byId[inLab.Id].DelayState));
        Assert.InRange(byId[inLab.Id].MinutesLeft!.Value, 1439, 1440);   // 24 h turnaround from registration (rounded down)
        Assert.Equal(("AwaitingVerification", "VerifyResults", true), (byId[panic.Id].Stage, byId[panic.Id].NextStep, byId[panic.Id].Urgent));
        Assert.Equal((1, "GLU"), (byId[panic.Id].Results!.Panic, byId[panic.Id].Results!.FirstPanicTest));
        Assert.Equal(("Ready", "TakePayment", "Done"), (byId[ready.Id].Stage, byId[ready.Id].NextStep, byId[ready.Id].DelayState));
        Assert.Equal(("Unpaid", 100m, 0m, 100m), (byId[ready.Id].Payment!.Status, byId[ready.Id].Payment!.PatientShare, byId[ready.Id].Payment!.Paid, byId[ready.Id].Payment!.Remaining));
        Assert.Equal(("Delivered", "None", "Paid"), (byId[delivered.Id].Stage, byId[delivered.Id].NextStep, byId[delivered.Id].Payment!.Status));
        Assert.Equal(("Cancelled", "None", 0, 1), (byId[cancelled.Id].Stage, byId[cancelled.Id].NextStep, byId[cancelled.Id].Registration.Lines, byId[cancelled.Id].Registration.CancelledLines));

        // the CBC visit holds results outside the desk's rights: counted as hidden, the stage follows them
        var cbc = byId[hidden.Id];
        Assert.Equal(("InLab", 0, 1), (cbc.Stage, cbc.Results!.Total, cbc.Results.Hidden));
        Assert.Equal(("Karim Adel", false), (cbc.PatientName, cbc.PatientMasked));   // the desk holds See-VIP
        Assert.Equal((1, 0, 0, 1), (cbc.Samples!.Total, cbc.Samples.Ordered, cbc.Samples.Rejected, cbc.Samples.Received));
        var age = $"{Platform.Domain.Patients.PatientAge.Between(new DateOnly(1990, 1, 1), DateOnly.FromDateTime(factory.Clock.UtcNow.UtcDateTime)).Years}y";
        Assert.Equal(("Mona Hassan", "Female", age, "Cash"),(byId[inLab.Id].PatientName, byId[inLab.Id].Gender, byId[inLab.Id].AgeText, byId[inLab.Id].PayerName));
        Assert.Equal("Front Desk", byId[inLab.Id].RegisteredByName);

        // the filters narrow the rows, never the counters
        await ExpectAsync(client, "?stage=Ready", [ready.Id], day.Counters);
        await ExpectAsync(client, "?attention=Panic", [panic.Id], day.Counters);
        await ExpectAsync(client, "?attention=ReadyUnpaid", [ready.Id], day.Counters);
        await ExpectAsync(client, "?view=Verification", [panic.Id], day.Counters);
        await ExpectAsync(client, "?view=FrontDesk", [ready.Id], day.Counters);
        await ExpectAsync(client, "?view=Reception", [panic.Id, inLab.Id, hidden.Id, ready.Id], day.Counters);
        await ExpectAsync(client, "?sort=accession&direction=desc&pageSize=2&page=1", [cancelled.Id, hidden.Id], day.Counters);
        await ExpectAsync(client, "?sort=stage", [inLab.Id, hidden.Id, panic.Id, ready.Id, delivered.Id, cancelled.Id], day.Counters);

        // the search narrows the day itself: the counters follow it
        var found = (await client.GetFromJsonAsync<LabTrackerPage>($"{Tracker}?search={inLab.Accession}"))!;
        Assert.Equal([inLab.Id], found.Items.Select(item => item.VisitId).ToArray());
        Assert.Equal(1, found.Counters.Total);
        Assert.Equal([hidden.Id], (await client.GetFromJsonAsync<LabTrackerPage>($"{Tracker}?search=karim"))!.Items.Select(item => item.VisitId).ToArray());
        Assert.Equal(5, (await client.GetFromJsonAsync<LabTrackerPage>($"{Tracker}?search=01001234567"))!.Counters.Total);

        // another day and another branch hold nothing
        var yesterday = day.Day.AddDays(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(0, (await client.GetFromJsonAsync<LabTrackerPage>($"{Tracker}?day={yesterday}"))!.Counters.Total);
        Assert.Equal(0, (await client.GetFromJsonAsync<LabTrackerPage>($"{Tracker}?branchId={Guid.CreateVersion7()}"))!.Counters.Total);

        // a from / to range (laboratory wall-clock, inclusive to the minute) replaces the day: the whole day as a range holds
        // everything, a range that ends yesterday holds nothing, and the page echoes the window it read
        var dayText = day.Day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var ranged = (await client.GetFromJsonAsync<LabTrackerPage>($"{Tracker}?from={dayText}T00:00&to={dayText}T23:59"))!;
        Assert.Equal(day.Counters.Total, ranged.Counters.Total);
        Assert.Equal(day.Day, ranged.Day);
        Assert.True(ranged.ToUtc > ranged.FromUtc);
        Assert.Equal(0, (await client.GetFromJsonAsync<LabTrackerPage>($"{Tracker}?from={yesterday}T00:00&to={yesterday}T23:59"))!.Counters.Total);
    }

    [DatabaseFact]
    public async Task Sections_follow_the_module_permissions_and_a_vip_name_is_masked_without_the_right()
    {
        var world = await SeedAsync();
        var (desk, _, _) = await factory.SeedUserAsync(tenantId: world.Tenant, permissions: DeskPermissions, branchIds: [world.Branch]);
        await GrantAsync(world.Tenant, desk, SpecialRights.All, world.Chemistry);
        var vip = await RegisterAsync(await SignedInClientAsync(desk), world, world.VipPatient, world.Glucose);
        await DispatchVisitEventsAsync(world.Tenant);

        var (reader, _, _) = await factory.SeedUserAsync(tenantId: world.Tenant, permissions: ["registration.view"], branchIds: [world.Branch]);
        var client = await SignedInClientAsync(reader);
        var page = (await client.GetFromJsonAsync<LabTrackerPage>(Tracker))!;
        var item = Assert.Single(page.Items);
        Assert.Equal((vip.Id, "InLab", "EnterResults"), (item.VisitId, item.Stage, item.NextStep));   // computed from everything
        Assert.Null(item.Samples);
        Assert.Null(item.Results);
        Assert.Null(item.Payment);
        Assert.Equal(("VIP patient", (string?)null, true), (item.PatientName, item.PatientNameAr, item.PatientMasked));
        Assert.Empty((await client.GetFromJsonAsync<LabTrackerPage>($"{Tracker}?search=karim"))!.Items);           // A-03
        Assert.Single((await client.GetFromJsonAsync<LabTrackerPage>($"{Tracker}?search={vip.Accession}"))!.Items);

        // the raw JSON carries the contract's camelCase names and null sections
        var json = JsonDocument.Parse(await client.GetStringAsync(Tracker)).RootElement;
        var first = json.GetProperty("items")[0];
        Assert.Equal(JsonValueKind.Null, first.GetProperty("samples").ValueKind);
        Assert.Equal("InLab", first.GetProperty("stage").GetString());
        Assert.True(json.GetProperty("counters").TryGetProperty("readyUnpaid", out _));
    }

    [DatabaseFact]
    public async Task Permission_branch_and_shape_are_enforced()
    {
        var world = await SeedAsync();
        var (stranger, _, _) = await factory.SeedUserAsync(tenantId: world.Tenant, permissions: ["results.view"], branchIds: [world.Branch]);
        var forbidden = await (await SignedInClientAsync(stranger)).GetAsync(Tracker);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(ProblemCodes.Forbidden, await AuthClient.CodeOf(forbidden));

        var (branchless, _, _) = await factory.SeedUserAsync(permissions: ["registration.view"]);   // a laboratory without branches: no session branch
        var noBranch = await (await SignedInClientAsync(branchless)).GetAsync(Tracker);
        Assert.Equal(HttpStatusCode.Forbidden, noBranch.StatusCode);
        Assert.Equal(ProblemCodes.BranchRequired, await AuthClient.CodeOf(noBranch));

        var (reader, _, _) = await factory.SeedUserAsync(tenantId: world.Tenant, permissions: ["registration.view"], branchIds: [world.Branch]);
        var client = await SignedInClientAsync(reader);
        foreach (var query in new[] { "?sort=password", "?direction=sideways", "?stage=Lost", "?attention=Bored", "?view=Lab", "?pageSize=500", "?page=0", $"?search={new string('x', 61)}", "?day=yesterday", "?from=2026-10-20T08:00", "?to=2026-10-20T08:00", "?from=2026-10-20T10:00&to=2026-10-20T08:00", "?from=2026-01-01T00:00&to=2026-03-01T00:00", "?from=soon&to=later" })
        {
            var refused = await client.GetAsync(Tracker + query);
            Assert.True(refused.StatusCode == HttpStatusCode.BadRequest, $"{query}: {(int)refused.StatusCode}");
        }
    }

    [DatabaseFact]
    public async Task Service_rows_follow_each_line_on_its_own_while_the_counters_stay_the_visits()
    {
        // LIS-CR-014 T3: ?rows=services answers one row per live top-level line with the line's own stage, next step and
        // samples / results; the visit's identity and balance travel along; the counters are the visits' as before.
        var world = await SeedAsync();
        var (desk, _, _) = await factory.SeedUserAsync(tenantId: world.Tenant, permissions: DeskPermissions, displayName: "Front Desk", branchIds: [world.Branch]);
        await GrantAsync(world.Tenant, desk, SpecialRights.All, world.Chemistry);
        var client = await SignedInClientAsync(desk);

        var response = await client.PostAsJsonAsync(Visits, new RegisterVisitRequest(world.Patient, null, null, world.Payer, world.Tier, null, null, null, false, false, false, [new OrderLineRequest(world.Glucose, false), new OrderLineRequest(world.Cbc, false)], null, null, false, 120));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var both = (await response.Content.ReadFromJsonAsync<VisitRegisteredResponse>())!;
        var single = await RegisterAsync(client, world, world.Patient, world.Glucose);
        await DispatchVisitEventsAsync(world.Tenant);
        await EnterAsync(client, both.Accession, 4.2m);   // the glucose of the first visit waits for verification

        var visits = (await client.GetFromJsonAsync<LabTrackerPage>(Tracker))!;
        Assert.Equal((LabTrackerRows.Visits, (IReadOnlyList<LabTrackerServiceItem>?)null), (visits.Rows, visits.Services));

        var page = (await client.GetFromJsonAsync<LabTrackerPage>($"{Tracker}?rows=services"))!;
        Assert.Equal(LabTrackerRows.Services, page.Rows);
        Assert.Empty(page.Items);
        Assert.Equal(visits.Counters, page.Counters);   // counters stay visit-based
        Assert.Equal(new LabTrackerCounters(2, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0), page.Counters);
        Assert.Equal(3L, page.TotalCount);
        var rows = page.Services!;
        var glucose = rows.Single(row => row.VisitId == both.Id && row.ServiceCode == "GLU");
        var cbc = rows.Single(row => row.VisitId == both.Id && row.ServiceCode == "CBC");
        var alone = rows.Single(row => row.VisitId == single.Id);

        Assert.Equal(("AwaitingVerification", "VerifyResults", "AwaitingVerification"), (glucose.Stage, glucose.NextStep, glucose.VisitStage));
        Assert.Equal((1, 1, 0), (glucose.Results!.Total, glucose.Results.Complete, glucose.Results.Hidden));
        Assert.Equal((1, 1), (glucose.Samples!.Total, glucose.Samples.Received));
        Assert.Equal(("GLU service", "Service", both.Accession, "Mona Hassan"), (glucose.ServiceName, glucose.ServiceKind, glucose.Accession, glucose.PatientName));
        Assert.Equal(("Unpaid", 150m), (glucose.Payment!.Status, glucose.Payment.Remaining));   // the visit's balance on every line
        Assert.Equal(glucose.Payment, cbc.Payment);

        // the CBC line holds results outside the desk's rights: followed on all its results, counted as hidden
        Assert.Equal(("InLab", "EnterResults", 0, 1), (cbc.Stage, cbc.NextStep, cbc.Results!.Total, cbc.Results.Hidden));
        Assert.InRange(cbc.MinutesLeft!.Value, 1439, 1440);
        Assert.Equal(("InLab", "EnterResults"), (alone.Stage, alone.NextStep));

        // the line's own stage filters the rows; sort by the service code, then accession and sequence
        var inLab = (await client.GetFromJsonAsync<LabTrackerPage>($"{Tracker}?rows=services&stage=InLab"))!;
        Assert.Equal(new[] { cbc.LineId, alone.LineId }.Order(), inLab.Services!.Select(row => row.LineId).Order());
        Assert.Equal((2L, page.Counters), (inLab.TotalCount, inLab.Counters));
        var byCode = (await client.GetFromJsonAsync<LabTrackerPage>($"{Tracker}?rows=services&sort=service"))!;
        Assert.Equal([cbc.LineId, glucose.LineId, alone.LineId], byCode.Services!.Select(row => row.LineId).ToArray());
        var byCodeDesc = (await client.GetFromJsonAsync<LabTrackerPage>($"{Tracker}?rows=Services&sort=service&direction=desc&pageSize=2"))!;
        Assert.Equal([glucose.LineId, alone.LineId], byCodeDesc.Services!.Select(row => row.LineId).ToArray());
        Assert.Equal(3L, byCodeDesc.TotalCount);
        foreach (var key in TrackerSortKeys.Services.Names)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{Tracker}?rows=services&sort={key}&direction=desc")).StatusCode);
        }

        // the raw JSON: camelCase names, the rows mode and the service rows
        var json = JsonDocument.Parse(await client.GetStringAsync($"{Tracker}?rows=services")).RootElement;
        Assert.Equal("Services", json.GetProperty("rows").GetString());
        Assert.True(json.GetProperty("services")[0].TryGetProperty("serviceCode", out _));
        Assert.True(json.GetProperty("services")[0].TryGetProperty("lineId", out _));

        // the shape: a rows mode of its own, the service key only with service rows
        foreach (var query in new[] { "?rows=lines", "?sort=service", "?rows=services&sort=password" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Tracker + query)).StatusCode);
        }
    }

    // ---- world --------------------------------------------------------------------------------------------------------

    private sealed record World(Guid Tenant, Guid Branch, Guid Chemistry, Guid Patient, Guid VipPatient, Guid Payer, Guid Tier, Guid Glucose, Guid Cbc);

    private async Task ExpectAsync(HttpClient client, string query, Guid[] visitIds, LabTrackerCounters counters)
    {
        var page = (await client.GetFromJsonAsync<LabTrackerPage>(Tracker + query))!;
        Assert.Equal(visitIds, page.Items.Select(item => item.VisitId).ToArray());
        Assert.Equal(counters, page.Counters);
    }

    private static async Task<VisitRegisteredResponse> RegisterAsync(HttpClient client, World world, Guid patient, Guid service, bool urgent = false)
    {
        var response = await client.PostAsJsonAsync(Visits, new RegisterVisitRequest(patient, null, null, world.Payer, world.Tier, null, null, null, urgent, false, false, [new OrderLineRequest(service, false)], null, null, false, 120));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<VisitRegisteredResponse>())!;
    }

    private static async Task EnterAsync(HttpClient client, string accession, decimal value, bool verify = false, bool print = false)
    {
        var unit = Assert.Single((await client.GetFromJsonAsync<ResultCaseView>($"{Results}?accession={accession}"))!.Units);
        var row = Assert.Single(unit.Results);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"{Results}/{row.Id}", new EnterResultValueRequest(value, null, null, null))).StatusCode);
        if (verify)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"{Results}/{row.Id}/verify", null)).StatusCode);
        }

        if (print)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"{Results}/report-units/{unit.Key}/print", null)).StatusCode);
        }
    }

    /// <summary>Runs every handler of the tenant's pending <c>VisitRegistered</c> events, the way the outbox processor would (the factory keeps its loop off), and marks them processed.</summary>
    private async Task DispatchVisitEventsAsync(Guid tenant)
    {
        using var scope = factory.Services.CreateScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(tenant);
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var type = typeof(Platform.Domain.Registration.VisitRegistered).FullName;
        var pending = await context.Set<OutboxEvent>()
            .Where(row => row.TenantId == tenant && row.Type == type && row.ProcessedAtUtc == null)
            .OrderBy(row => row.EnqueuedAtUtc)
            .ToListAsync();
        foreach (var row in pending)
        {
            var registered = JsonSerializer.Deserialize<Platform.Domain.Registration.VisitRegistered>(row.Payload, OutboxEvent.SerializerOptions)!;
            foreach (var handler in scope.ServiceProvider.GetServices<IDomainEventHandler<Platform.Domain.Registration.VisitRegistered>>())
            {
                await handler.HandleAsync(registered, CancellationToken.None);
            }

            row.MarkProcessed(factory.Clock.UtcNow);
        }

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>A branch; chemistry GLU (24 h, range 3.5–5.0, panic above 6.5) and haematology CBC (24 h); the cash payer with prices; a patient and a VIP patient.</summary>
    private async Task<World> SeedAsync()
    {
        var tenant = Guid.CreateVersion7();
        var now = factory.Clock.UtcNow;
        using var scope = factory.Services.CreateScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(tenant);
        var provider = scope.ServiceProvider;
        var context = provider.GetRequiredService<AppDbContext>();
        var unitOfWork = provider.GetRequiredService<IUnitOfWork>();

        var branch = Branch.Create("MAIN", new LocalizedText("Main branch", "الفرع الرئيسي"), null, null, null, null, "EGP", now);
        context.Set<Branch>().Add(branch);
        var chemistry = ClinicalGroup.Create("CHEM", "C", new LocalizedText("Chemistry", "الكيمياء"), null, null, 1, now);
        var haematology = ClinicalGroup.Create("HEMA", "H", new LocalizedText("Haematology", "أمراض الدم"), null, null, 2, now);
        var serum = SampleType.Create("SERUM", "SER", new LocalizedText("Serum", "مصل"), [], now);
        var blood = SampleType.Create("WB", "WB", new LocalizedText("Whole blood", "دم كامل"), [], now);
        context.Set<ClinicalGroup>().AddRange(chemistry, haematology);
        context.Set<SampleType>().AddRange(serum, blood);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        Service Add(string code, int sequence, ClinicalGroup group, SampleType sample, bool panicBand)
        {
            var test = LabTest.Create(code, new LabTestDefinition(new LocalizedText(code, code), null, null, null, group.Id, LabTestType.Numeric, null, null, null, GenderRestriction.Any, null, 1, false, true, 1, null), now);
            if (panicBand)
            {
                test.ReplaceRangeBands([new RangeBandSpec(GenderRestriction.Any, 0, 150, RangeAgeUnit.Years, null, 3.5m, 5.0m, 2.5m, 6.5m, null, null, null)], now);
            }

            context.Set<LabTest>().Add(test);
            var service = Service.Create(code, ServiceType.Test, new ServiceDefinition(code, null, new LocalizedText(code + " service", "خدمة " + code), null, null, null, group.Id, sample.Id, null, null, [], null, GenderRestriction.Any, 24, 1, false, true, false, null), sequence, [new ResolvedServiceMember(ServiceMemberKind.Test, test.Id, 1, false, true, LabTestType.Numeric, null)], now);
            context.Set<Service>().Add(service);
            return service;
        }

        var glucose = Add("GLU", 1, chemistry, serum, panicBand: true);
        var cbc = Add("CBC", 2, haematology, blood, panicBand: false);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var cash = Payer.CreateDefaultCash("CASH", now);
        context.Set<Payer>().Add(cash);
        var list = BasePriceList.Create("L2026", new LocalizedText("List 2026", "قائمة"), 2026, now);
        list.UpsertLines([new PriceLineInput(glucose.Id, 100m, true), new PriceLineInput(cbc.Id, 50m, true)], now);
        list.Activate(now);
        context.Set<BasePriceList>().Add(list);

        var numbers = provider.GetRequiredService<IReferenceNumbers>();
        var mona = Patient.Register(
            await numbers.NextAsync(Patient.NumberKind, Patient.NumberPrefix, now, CancellationToken.None),
            new PatientDemographics(PatientTitle.Mrs, "Mona Hassan", null, PatientGender.Female, new DateOnly(1990, 1, 1), null, null, "01001234567", null, null, null, null, null, null, null),
            PatientFieldRules.Optional,
            false,
            now);
        var karim = Patient.Register(
            await numbers.NextAsync(Patient.NumberKind, Patient.NumberPrefix, now, CancellationToken.None),
            new PatientDemographics(PatientTitle.Mr, "Karim Adel", "كريم عادل", PatientGender.Male, new DateOnly(1980, 5, 5), null, null, "01007654321", null, null, null, null, null, null, null),
            PatientFieldRules.Optional,
            true,
            now);
        provider.GetRequiredService<IPatientRepository>().Add(mona);
        provider.GetRequiredService<IPatientRepository>().Add(karim);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        return new World(tenant, branch.Id, chemistry.Id, mona.Id, karim.Id, cash.Id, cash.Tiers.Single().Id, glucose.Id, cbc.Id);
    }

    /// <summary>Full result rights on the groups in every branch and the special rights, on the role the seeding gave the user.</summary>
    private async Task GrantAsync(Guid tenant, string username, SpecialRights special, params Guid[] groups)
    {
        using var scope = factory.Services.CreateScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(tenant);
        var role = (await scope.ServiceProvider.GetRequiredService<IRoleRepository>().FindByNameAsync($"role-{username}", CancellationToken.None))!;
        role.SetResultRights(groups.Select(group => ResultRight.Full(group)), factory.Clock.UtcNow);
        role.SetSpecialRights(special.Copy(), factory.Clock.UtcNow);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    private async Task<HttpClient> SignedInClientAsync(string username)
    {
        var client = factory.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(username, ApiFactory.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }
}
