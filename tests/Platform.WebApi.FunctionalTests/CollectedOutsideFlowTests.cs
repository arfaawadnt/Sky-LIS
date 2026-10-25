using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Patients;
using Platform.Contracts.Common;
using Platform.Contracts.Registration;
using Platform.Contracts.Samples;
using Platform.Contracts.Settings;
using Platform.Contracts.Tracker;
using Platform.Domain.Catalogue;
using Platform.Domain.Organization;
using Platform.Domain.Patients;
using Platform.Domain.Pricing;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Persistence.Outbox;
using Platform.SharedKernel.Localization;
using Platform.WebApi.FunctionalTests.Support;
using RegistrationLabels = Platform.Contracts.Registration.VisitLabelsDocument;
using SampleLabels = Platform.Contracts.Samples.VisitLabelsDocument;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// "Collected outside the laboratory" per sample (change request LIS-CR-008, reviewer point C-02) over the real API: the
/// order marks tubes one by one (or every tube through the old visit-level field), the visit, its labels and the job order
/// say so, the Samples policy records the marked tubes as Collected at the typed time whatever auto-collect says, the
/// sample screens and labels carry the flag, and the Lab Tracker counts it; refusals answer REG-040 / REG-041.
/// </summary>
public sealed class CollectedOutsideFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Visits = "/api/v1/registration/visits";
    private const string Samples = "/api/v1/samples";
    private const string Tracker = "/api/v1/tracker/visits";
    private const string Settings = "/api/v1/settings";

    private static readonly string[] DeskPermissions =
    [
        "registration.view", "registration.create", "registration.edit", "catalogue.view",
        "samples.view", "samples.create", "settings.view", "settings.manage",
    ];

    [DatabaseFact]
    public async Task A_sample_collected_outside_is_marked_on_its_tube_arrives_Collected_and_is_shown_everywhere()
    {
        var world = await SeedAsync();
        var (desk, _, _) = await factory.SeedUserAsync(tenantId: world.Tenant, permissions: DeskPermissions, displayName: "Front Desk", branchIds: [world.Branch]);
        var client = await SignedInClientAsync(desk);
        await SetAsync(client, ("autoCollect", "false"), ("autoReceive", "false"));

        List<OrderLineRequest> lines = [new(world.Glucose, false), new(world.Cbc, false)];
        var check = await client.PostAsJsonAsync($"{Visits}/check", new CheckOrderRequest(null, world.Patient, world.Payer, world.Tier, null, lines, 120));
        var dryRun = (await check.Content.ReadFromJsonAsync<OrderCheckResult>())!;
        var serumKey = dryRun.Tubes.Single(tube => tube.SampleTypeNameEn == "Serum").Key;
        var bloodKey = dryRun.Tubes.Single(tube => tube.SampleTypeNameEn == "Whole blood").Key;
        var hourAgo = factory.Clock.UtcNow.AddHours(-1);
        var collectedAt = hourAgo.AddTicks(-(hourAgo.Ticks % TimeSpan.TicksPerSecond));   // whole seconds: the column keeps microseconds

        var created = await client.PostAsJsonAsync(Visits, Register(world, lines) with
        {
            Samples = [new OrderSampleRequest(serumKey, null, true, collectedAt), new OrderSampleRequest(bloodKey, null, false, null)],
        });
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var registered = (await created.Content.ReadFromJsonAsync<VisitRegisteredResponse>())!;

        var detail = (await client.GetFromJsonAsync<VisitDetail>($"{Visits}/{registered.Id}"))!;
        Assert.True(detail.CollectedOutside);   // derived: any tube outside
        var serumTube = detail.Tubes.Single(tube => tube.SampleTypeNameEn == "Serum");
        Assert.True(serumTube.CollectedOutside);
        Assert.Equal(collectedAt, serumTube.CollectedOutsideAtUtc);
        Assert.False(detail.Tubes.Single(tube => tube.SampleTypeNameEn == "Whole blood").CollectedOutside);

        var labels = (await client.GetFromJsonAsync<RegistrationLabels>($"{Visits}/{registered.Id}/documents/labels"))!;
        Assert.True(labels.Labels.Single(label => label.SampleTypeNameEn == "Serum").CollectedOutside);
        Assert.False(labels.Labels.Single(label => label.SampleTypeNameEn == "Whole blood").CollectedOutside);
        var jobOrder = (await client.GetFromJsonAsync<JobOrderDocument>($"{Visits}/{registered.Id}/documents/job-order"))!;
        Assert.True(jobOrder.CollectedOutside);

        await DispatchVisitEventsAsync(world.Tenant);

        var samples = (await client.GetFromJsonAsync<Paged<SampleListItem>>($"{Samples}?accession={registered.Accession}"))!;
        var outside = samples.Items.Single(item => item.SampleNumber == serumTube.SampleNumber);
        Assert.Equal(("Collected", true, (DateTimeOffset?)collectedAt), (outside.Status, outside.CollectedOutside, outside.CollectedAtUtc));
        var inside = samples.Items.Single(item => item.SampleNumber != serumTube.SampleNumber);
        Assert.Equal(("Ordered", false), (inside.Status, inside.CollectedOutside));

        // The Received gate still applies: the outside sample is received by scan like any other.
        var received = await client.PostAsJsonAsync($"{Samples}/scan", new ScanSampleRequest(outside.SampleNumber, "Receive"));
        Assert.Equal(HttpStatusCode.OK, received.StatusCode);

        var sampleLabels = (await client.GetFromJsonAsync<SampleLabels>($"{Samples}/visits/{registered.Id}/documents/labels"))!;
        Assert.True(sampleLabels.Labels.Single(label => label.SampleNumber == outside.SampleNumber).CollectedOutside);
        Assert.False(sampleLabels.Labels.Single(label => label.SampleNumber == inside.SampleNumber).CollectedOutside);

        var tracker = (await client.GetFromJsonAsync<LabTrackerPage>(Tracker))!;
        var row = tracker.Items.Single(item => item.VisitId == registered.Id);
        Assert.Equal((2, 1), (row.Samples!.Total, row.Samples.CollectedOutside));
    }

    [DatabaseFact]
    public async Task The_old_visit_level_field_marks_every_tube_and_a_sample_type_entry_marks_its_tubes()
    {
        var world = await SeedAsync();
        var (desk, _, _) = await factory.SeedUserAsync(tenantId: world.Tenant, permissions: DeskPermissions, branchIds: [world.Branch]);
        var client = await SignedInClientAsync(desk);
        List<OrderLineRequest> lines = [new(world.Glucose, false), new(world.Cbc, false)];

        var legacy = (await (await client.PostAsJsonAsync(Visits, Register(world, lines) with { CollectedOutside = true })).Content.ReadFromJsonAsync<VisitRegisteredResponse>())!;
        var legacyDetail = (await client.GetFromJsonAsync<VisitDetail>($"{Visits}/{legacy.Id}"))!;
        Assert.All(legacyDetail.Tubes, tube => Assert.True(tube.CollectedOutside));

        var byType = (await (await client.PostAsJsonAsync(Visits, Register(world, lines) with
        {
            CollectedOutside = true,   // ignored: the per-sample entries win
            Samples = [new OrderSampleRequest(null, world.WholeBlood, true, null)],
        })).Content.ReadFromJsonAsync<VisitRegisteredResponse>())!;
        var byTypeDetail = (await client.GetFromJsonAsync<VisitDetail>($"{Visits}/{byType.Id}"))!;
        Assert.Equal(["Whole blood"], byTypeDetail.Tubes.Where(tube => tube.CollectedOutside).Select(tube => tube.SampleTypeNameEn).ToArray());

        var plain = (await (await client.PostAsJsonAsync(Visits, Register(world, lines))).Content.ReadFromJsonAsync<VisitRegisteredResponse>())!;
        var plainDetail = (await client.GetFromJsonAsync<VisitDetail>($"{Visits}/{plain.Id}"))!;
        Assert.False(plainDetail.CollectedOutside);

        // Editing the details no longer touches the flag (an old body that still sends it is accepted).
        var edited = await client.PutAsJsonAsync($"{Visits}/{plain.Id}/details", new UpdateVisitDetailsRequest("T-1", null, null, false, false, true));
        Assert.Equal(HttpStatusCode.NoContent, edited.StatusCode);
        Assert.False((await client.GetFromJsonAsync<VisitDetail>($"{Visits}/{plain.Id}"))!.CollectedOutside);
    }

    [DatabaseFact]
    public async Task Refusals_answer_their_codes()
    {
        var world = await SeedAsync();
        var (desk, _, _) = await factory.SeedUserAsync(tenantId: world.Tenant, permissions: DeskPermissions, branchIds: [world.Branch]);
        var client = await SignedInClientAsync(desk);
        List<OrderLineRequest> lines = [new(world.Glucose, false)];

        var notAKey = await client.PostAsJsonAsync(Visits, Register(world, lines) with { Samples = [new OrderSampleRequest("not-a-key", null, true, null)] });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, notAKey.StatusCode);
        Assert.Equal("REG-040", await AuthClient.CodeOf(notAKey));

        var notOrdered = await client.PostAsJsonAsync(Visits, Register(world, lines) with { Samples = [new OrderSampleRequest(null, world.WholeBlood, true, null)] });
        Assert.Equal("REG-040", await AuthClient.CodeOf(notOrdered));

        var future = await client.PostAsJsonAsync(Visits, Register(world, lines) with { Samples = [new OrderSampleRequest(null, world.Serum, true, factory.Clock.UtcNow.AddHours(2))] });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, future.StatusCode);
        Assert.Equal("REG-041", await AuthClient.CodeOf(future));

        var shapeless = await client.PostAsJsonAsync(Visits, Register(world, lines) with { Samples = [new OrderSampleRequest(null, null, true, null)] });
        Assert.Equal(HttpStatusCode.BadRequest, shapeless.StatusCode);
        Assert.Equal(ProblemCodes.ValidationFailed, await AuthClient.CodeOf(shapeless));
    }

    // ---- world --------------------------------------------------------------------------------------------------------

    private sealed record World(Guid Tenant, Guid Branch, Guid Patient, Guid Payer, Guid Tier, Guid Glucose, Guid Cbc, Guid Serum, Guid WholeBlood);

    private static RegisterVisitRequest Register(World world, IReadOnlyList<OrderLineRequest> lines) =>
        new(world.Patient, null, null, world.Payer, world.Tier, null, null, null, false, false, false, lines, null, null, false, 120);

    /// <summary>Runs every handler of the tenant's pending <c>VisitRegistered</c> events, the way the outbox processor would (the factory keeps its loop off).</summary>
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

    /// <summary>A branch; chemistry GLU (serum) and haematology CBC (whole blood); the cash payer with prices; a patient.</summary>
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

        Service Add(string code, int sequence, ClinicalGroup group, SampleType sample)
        {
            var test = LabTest.Create(code, new LabTestDefinition(new LocalizedText(code, code), null, null, null, group.Id, LabTestType.Numeric, null, null, null, GenderRestriction.Any, null, 1, false, true, 1, null), now);
            context.Set<LabTest>().Add(test);
            var service = Service.Create(code, ServiceType.Test, new ServiceDefinition(code, null, new LocalizedText(code + " service", "خدمة " + code), null, null, null, group.Id, sample.Id, null, null, [], null, GenderRestriction.Any, 24, 1, false, true, false, null), sequence, [new ResolvedServiceMember(ServiceMemberKind.Test, test.Id, 1, false, true, LabTestType.Numeric, null)], now);
            context.Set<Service>().Add(service);
            return service;
        }

        var glucose = Add("GLU", 1, chemistry, serum);
        var cbc = Add("CBC", 2, haematology, blood);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var cash = Payer.CreateDefaultCash("CASH", now);
        context.Set<Payer>().Add(cash);
        var list = BasePriceList.Create("L2026", new LocalizedText("List 2026", "قائمة"), 2026, now);
        list.UpsertLines([new PriceLineInput(glucose.Id, 100m, true), new PriceLineInput(cbc.Id, 50m, true)], now);
        list.Activate(now);
        context.Set<BasePriceList>().Add(list);

        var number = await provider.GetRequiredService<IReferenceNumbers>().NextAsync(Patient.NumberKind, Patient.NumberPrefix, now, CancellationToken.None);
        var patient = Patient.Register(number, new PatientDemographics(PatientTitle.Mrs, "Mona Hassan", null, PatientGender.Female, new DateOnly(1990, 1, 1), null, null, "01001234567", null, null, null, null, null, null, null), PatientFieldRules.Optional, false, now);
        provider.GetRequiredService<IPatientRepository>().Add(patient);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        return new World(tenant, branch.Id, patient.Id, cash.Id, cash.Tiers.Single().Id, glucose.Id, cbc.Id, serum.Id, blood.Id);
    }

    private static async Task SetAsync(HttpClient client, params (string Key, string Value)[] values)
    {
        var response = await client.PutAsJsonAsync(Settings, new UpdateTenantSettingsRequest([.. values.Select(value => new SettingValueItem(value.Key, value.Value))]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
