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
using Platform.Contracts.Catalogue;
using Platform.Contracts.Registration;
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
/// Compatible sample conditions (change request LIS-CR-015, note M4) over the real API: the setup endpoints and their rules
/// (CAT-280…285, catalogue.view / catalogue.edit), then a visit with a Fasting and a Random service of one unit and sample
/// type gets one tube and one sample named "Fasting" carrying both lines (dry run, visit, registration and sample labels),
/// while a Random-only visit keeps its Random tube.
/// </summary>
public sealed class CompatibleConditionsFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string SampleTypes = "/api/v1/catalogue/sample-types";
    private const string Visits = "/api/v1/registration/visits";

    private static readonly string[] CataloguePermissions = ["catalogue.view", "catalogue.edit"];

    private static readonly string[] DeskPermissions = ["registration.view", "registration.create", "registration.edit", "catalogue.view", "samples.view", "samples.create"];

    [DatabaseFact]
    public async Task The_setup_lists_every_type_saves_a_map_and_refuses_what_breaks_the_rules()
    {
        var world = await SeedAsync();
        var (editor, _, _) = await factory.SeedUserAsync(tenantId: world.Tenant, permissions: CataloguePermissions);
        var (viewer, _, _) = await factory.SeedUserAsync(tenantId: world.Tenant, permissions: ["catalogue.view"]);
        var client = await SignedInClientAsync(editor);

        var before = (await client.GetFromJsonAsync<List<CompatibleSampleTypeView>>($"{SampleTypes}/compatible-conditions"))!;
        var serum = before.Single(type => type.Id == world.Serum);
        Assert.Equal(["FAST", "PP", "RAND"], serum.Conditions.Select(condition => condition.Code).ToArray());
        Assert.All(serum.Conditions, condition => Assert.Null(condition.MergesIntoConditionId));

        var saved = await client.PutAsJsonAsync($"{SampleTypes}/{world.Serum}/compatible-conditions", new SaveCompatibleConditionsRequest([new(world.Random, world.Fasting)]));
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var after = (await client.GetFromJsonAsync<List<CompatibleSampleTypeView>>($"{SampleTypes}/compatible-conditions"))!.Single(type => type.Id == world.Serum);
        Assert.Equal(world.Fasting, after.Conditions.Single(condition => condition.Id == world.Random).MergesIntoConditionId);

        async Task<string?> RefusedAsync(params SampleConditionPairItem[] pairs)
        {
            var response = await client.PutAsJsonAsync($"{SampleTypes}/{world.Serum}/compatible-conditions", new SaveCompatibleConditionsRequest(pairs));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            return await AuthClient.CodeOf(response);
        }

        Assert.Equal("CAT-281", await RefusedAsync(new SampleConditionPairItem(world.Random, world.Random)));
        Assert.Equal("CAT-280", await RefusedAsync(new SampleConditionPairItem(world.Random, world.Blood)));
        Assert.Equal("CAT-282", await RefusedAsync(new SampleConditionPairItem(world.Random, world.PostPrandial)));
        Assert.Equal("CAT-284", await RefusedAsync(new SampleConditionPairItem(world.Fasting, world.Random), new SampleConditionPairItem(world.Random, world.Fasting)));
        Assert.Equal("CAT-285", await RefusedAsync(new SampleConditionPairItem(world.Random, world.Fasting), new SampleConditionPairItem(world.Random, null)));

        var missing = await client.PutAsJsonAsync($"{SampleTypes}/{Guid.CreateVersion7()}/compatible-conditions", new SaveCompatibleConditionsRequest([]));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var readOnly = await SignedInClientAsync(viewer);
        Assert.Equal(HttpStatusCode.OK, (await readOnly.GetAsync($"{SampleTypes}/compatible-conditions")).StatusCode);
        var forbidden = await readOnly.PutAsJsonAsync($"{SampleTypes}/{world.Serum}/compatible-conditions", new SaveCompatibleConditionsRequest([]));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [DatabaseFact]
    public async Task A_Fasting_and_a_Random_service_share_one_Fasting_sample_and_Random_alone_keeps_its_own()
    {
        var world = await SeedAsync();
        var (editor, _, _) = await factory.SeedUserAsync(tenantId: world.Tenant, permissions: CataloguePermissions);
        var setup = await SignedInClientAsync(editor);
        var saved = await setup.PutAsJsonAsync($"{SampleTypes}/{world.Serum}/compatible-conditions", new SaveCompatibleConditionsRequest([new(world.Random, world.Fasting)]));
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);

        var (desk, _, _) = await factory.SeedUserAsync(tenantId: world.Tenant, permissions: DeskPermissions, branchIds: [world.Branch]);
        var client = await SignedInClientAsync(desk);
        List<OrderLineRequest> lines = [new(world.RandomSugar, false), new(world.FastingSugar, false)];

        var check = await client.PostAsJsonAsync($"{Visits}/check", new CheckOrderRequest(null, world.Patient, world.Payer, world.Tier, null, lines, 120));
        Assert.True(check.IsSuccessStatusCode, await check.Content.ReadAsStringAsync());
        var dryRun = (await check.Content.ReadFromJsonAsync<OrderCheckResult>())!;
        var previewTube = Assert.Single(dryRun.Tubes);
        Assert.Equal("Fasting", previewTube.SampleConditionNameEn);
        Assert.Equal(2, previewTube.ServiceIds.Count);
        Assert.All(dryRun.Lines, line => Assert.Equal(previewTube.Key, line.TubeKey));

        var created = await client.PostAsJsonAsync(Visits, Register(world, lines));
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var registered = (await created.Content.ReadFromJsonAsync<VisitRegisteredResponse>())!;
        var detail = (await client.GetFromJsonAsync<VisitDetail>($"{Visits}/{registered.Id}"))!;
        var tube = Assert.Single(detail.Tubes);
        Assert.Equal("Fasting", tube.SampleConditionNameEn);
        Assert.All(detail.Lines, line => Assert.Equal(tube.Id, line.TubeId));

        var labels = (await client.GetFromJsonAsync<RegistrationLabels>($"{Visits}/{registered.Id}/documents/labels"))!;
        Assert.Equal("Fasting", Assert.Single(labels.Labels).SampleConditionNameEn);

        await DispatchVisitEventsAsync(world.Tenant);
        var sampleLabels = (await client.GetFromJsonAsync<SampleLabels>($"/api/v1/samples/visits/{registered.Id}/documents/labels"))!;
        var sample = Assert.Single(sampleLabels.Labels);
        Assert.Equal((world.Fasting, "Fasting"), (sample.SampleConditionId, sample.SampleConditionNameEn));
        Assert.Equal(2, sample.Services.Count);

        var randomOnly = (await (await client.PostAsJsonAsync(Visits, Register(world, [new(world.RandomSugar, false)]))).Content.ReadFromJsonAsync<VisitRegisteredResponse>())!;
        var randomDetail = (await client.GetFromJsonAsync<VisitDetail>($"{Visits}/{randomOnly.Id}"))!;
        Assert.Equal("Random", Assert.Single(randomDetail.Tubes).SampleConditionNameEn);
    }

    // ---- world --------------------------------------------------------------------------------------------------------

    private sealed record World(
        Guid Tenant, Guid Branch, Guid Patient, Guid Payer, Guid Tier, Guid Serum, Guid Fasting, Guid Random, Guid PostPrandial, Guid Blood, Guid FastingSugar, Guid RandomSugar);

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

    /// <summary>A branch; chemistry FBS (serum, fasting) and RBS (serum, random); serum conditions FAST, RAND, PP (inactive); whole blood with one condition; prices; a patient.</summary>
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
        var serum = SampleType.Create(
            "SERUM",
            "SER",
            new LocalizedText("Serum", "مصل"),
            [
                new SampleConditionSpec(null, "FAST", new LocalizedText("Fasting", "صائم"), true),
                new SampleConditionSpec(null, "RAND", new LocalizedText("Random", "عشوائي"), true),
                new SampleConditionSpec(null, "PP", new LocalizedText("Post-prandial", "بعد الأكل"), false),
            ],
            now);
        var blood = SampleType.Create("WB", "WB", new LocalizedText("Whole blood", "دم كامل"), [new SampleConditionSpec(null, "EDTA", new LocalizedText("EDTA", "إديتا"), true)], now);
        context.Set<ClinicalGroup>().Add(chemistry);
        context.Set<SampleType>().AddRange(serum, blood);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        Guid Condition(SampleType type, string code) => type.Conditions.Single(condition => condition.Code == code).Id;

        Service Add(string code, int sequence, Guid condition)
        {
            var test = LabTest.Create(code, new LabTestDefinition(new LocalizedText(code, code), null, null, null, chemistry.Id, LabTestType.Numeric, null, null, null, GenderRestriction.Any, null, 1, false, true, 1, null), now);
            context.Set<LabTest>().Add(test);
            var service = Service.Create(code, ServiceType.Test, new ServiceDefinition(code, null, new LocalizedText(code + " service", "خدمة " + code), null, null, null, chemistry.Id, serum.Id, condition, null, [], null, GenderRestriction.Any, 24, 1, false, true, false, null), sequence, [new ResolvedServiceMember(ServiceMemberKind.Test, test.Id, 1, false, true, LabTestType.Numeric, null)], now);
            context.Set<Service>().Add(service);
            return service;
        }

        var fastingSugar = Add("FBS", 1, Condition(serum, "FAST"));
        var randomSugar = Add("RBS", 2, Condition(serum, "RAND"));
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var cash = Payer.CreateDefaultCash("CASH", now);
        context.Set<Payer>().Add(cash);
        var list = BasePriceList.Create("L2026", new LocalizedText("List 2026", "قائمة"), 2026, now);
        list.UpsertLines([new PriceLineInput(fastingSugar.Id, 60m, true), new PriceLineInput(randomSugar.Id, 50m, true)], now);
        list.Activate(now);
        context.Set<BasePriceList>().Add(list);

        var number = await provider.GetRequiredService<IReferenceNumbers>().NextAsync(Patient.NumberKind, Patient.NumberPrefix, now, CancellationToken.None);
        var patient = Patient.Register(number, new PatientDemographics(PatientTitle.Mrs, "Mona Hassan", null, PatientGender.Female, new DateOnly(1990, 1, 1), null, null, "01001234567", null, null, null, null, null, null, null), PatientFieldRules.Optional, false, now);
        provider.GetRequiredService<IPatientRepository>().Add(patient);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        return new World(
            tenant, branch.Id, patient.Id, cash.Id, cash.Tiers.Single().Id, serum.Id,
            Condition(serum, "FAST"), Condition(serum, "RAND"), Condition(serum, "PP"), Condition(blood, "EDTA"), fastingSugar.Id, randomSugar.Id);
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
