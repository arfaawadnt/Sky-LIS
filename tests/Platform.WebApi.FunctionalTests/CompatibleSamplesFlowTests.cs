using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Catalogue;
using Platform.Application.Patients;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;
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
/// Compatibility groups of samples (change request LIS-CR-015, note M4) over the real API: create, read, edit, export,
/// (de)activate under the catalogue permissions; a sample in one active group only (CAT-284); every allow-listed sort key is
/// served. At registration a Fasting and a Random service of one unit share one sample named "Fasting" (dry run, visit,
/// registration and sample labels); Random alone keeps "Random"; the same pairs in two units stay two samples.
/// </summary>
public sealed class CompatibleSamplesFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Groups = "/api/v1/catalogue/sample-compatibility-groups";
    private const string Visits = "/api/v1/registration/visits";

    private static readonly string[] CataloguePermissions = ["catalogue.view", "catalogue.create", "catalogue.edit", "catalogue.export"];

    private static readonly string[] DeskPermissions = ["registration.view", "registration.create", "registration.edit", "catalogue.view", "samples.view", "samples.create"];

    [DatabaseFact]
    public async Task Groups_are_created_listed_edited_exported_and_switched_under_their_rules()
    {
        var world = await SeedAsync();
        var (editor, _, _) = await factory.SeedUserAsync(tenantId: world.Tenant, permissions: CataloguePermissions);
        var (viewer, _, _) = await factory.SeedUserAsync(tenantId: world.Tenant, permissions: ["catalogue.view"]);
        var client = await SignedInClientAsync(editor);

        var created = await client.PostAsJsonAsync(Groups, Create("SUGAR", new CompatibilityMemberItem(world.Serum, world.Fasting), new CompatibilityMemberItem(world.Serum, world.Random)));
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<CreatedResource>())!.Id;

        var view = (await client.GetFromJsonAsync<SampleCompatibilityGroupView>($"{Groups}/{id}"))!;
        Assert.Equal(["FAST", "RAND"], view.Members.Select(member => member.SampleConditionCode));
        Assert.Equal("Serum", view.Members[0].SampleTypeNameEn);

        var edited = await client.PutAsJsonAsync($"{Groups}/{id}", new UpdateSampleCompatibilityGroupRequest("Sugar", "السكر", [new(world.Serum, world.Random), new(world.Serum, world.Fasting), new(world.Serum, null)]));
        Assert.Equal(HttpStatusCode.NoContent, edited.StatusCode);
        var page = (await client.GetFromJsonAsync<Paged<SampleCompatibilityGroupView>>($"{Groups}?search=random"))!;
        Assert.Equal([1, 2, 3], Assert.Single(page.Items).Members.Select(member => member.Priority));
        Assert.Equal("RAND", page.Items[0].Members[0].SampleConditionCode);
        Assert.Single((await client.GetFromJsonAsync<List<SampleCompatibilityGroupView>>($"{Groups}/export"))!);

        async Task<string?> RefusedAsync(HttpResponseMessage response, HttpStatusCode status)
        {
            Assert.Equal(status, response.StatusCode);
            return await AuthClient.CodeOf(response);
        }

        Assert.Equal("CAT-280", await RefusedAsync(await client.PostAsJsonAsync(Groups, Create("ONE", new CompatibilityMemberItem(world.Serum, world.Fasting))), HttpStatusCode.UnprocessableEntity));
        Assert.Equal("CAT-282", await RefusedAsync(await client.PostAsJsonAsync(Groups, Create("BAD", new CompatibilityMemberItem(world.Blood, null), new CompatibilityMemberItem(world.Serum, world.Edta))), HttpStatusCode.UnprocessableEntity));
        Assert.Equal("CAT-283", await RefusedAsync(await client.PostAsJsonAsync(Groups, Create("TWICE", new CompatibilityMemberItem(world.Blood, null), new CompatibilityMemberItem(world.Blood, null))), HttpStatusCode.UnprocessableEntity));
        Assert.Equal("CAT-284", await RefusedAsync(await client.PostAsJsonAsync(Groups, Create("OTHER", new CompatibilityMemberItem(world.Blood, world.Edta), new CompatibilityMemberItem(world.Serum, world.Random))), HttpStatusCode.Conflict));
        Assert.Equal("CAT-285", await RefusedAsync(await client.PostAsJsonAsync(Groups, Create("SUGAR", new CompatibilityMemberItem(world.Blood, world.Edta), new CompatibilityMemberItem(world.Blood, null))), HttpStatusCode.Conflict));
        Assert.Equal("CAT-286", await RefusedAsync(await client.GetAsync($"{Groups}/{Guid.CreateVersion7()}"), HttpStatusCode.NotFound));

        // a deactivated group frees its samples; reactivating it while another active group holds one is refused
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"{Groups}/{id}/deactivate", null)).StatusCode);
        var second = await client.PostAsJsonAsync(Groups, Create("RANDOM", new CompatibilityMemberItem(world.Serum, world.Random), new CompatibilityMemberItem(world.Blood, world.Edta)));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal("CAT-284", await RefusedAsync(await client.PostAsync($"{Groups}/{id}/reactivate", null), HttpStatusCode.Conflict));

        var readOnly = await SignedInClientAsync(viewer);
        Assert.Equal(HttpStatusCode.OK, (await readOnly.GetAsync(Groups)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await readOnly.PostAsJsonAsync(Groups, Create("NOPE", new CompatibilityMemberItem(world.Blood, world.Edta), new CompatibilityMemberItem(world.Blood, null)))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await readOnly.GetAsync($"{Groups}/export")).StatusCode);
    }

    [DatabaseFact]
    public async Task Every_allow_listed_sort_key_is_served()
    {
        var world = await SeedAsync();
        var (editor, _, _) = await factory.SeedUserAsync(tenantId: world.Tenant, permissions: CataloguePermissions);
        var client = await SignedInClientAsync(editor);
        foreach (var key in CatalogueSortKeys.SampleCompatibilityGroups.Names)
        {
            foreach (var direction in new[] { "asc", "desc" })
            {
                var response = await client.GetAsync($"{Groups}?sort={key}&direction={direction}");
                Assert.True(response.StatusCode == HttpStatusCode.OK, $"{key} {direction}: {await response.Content.ReadAsStringAsync()}");
            }
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{Groups}?sort=members")).StatusCode);
    }

    [DatabaseFact]
    public async Task A_Fasting_and_a_Random_service_share_one_Fasting_sample_alone_or_across_units_they_do_not()
    {
        var world = await SeedAsync();
        var (editor, _, _) = await factory.SeedUserAsync(tenantId: world.Tenant, permissions: CataloguePermissions);
        var setup = await SignedInClientAsync(editor);
        Assert.Equal(HttpStatusCode.Created, (await setup.PostAsJsonAsync(Groups, Create("SUGAR", new CompatibilityMemberItem(world.Serum, world.Fasting), new CompatibilityMemberItem(world.Serum, world.Random)))).StatusCode);

        var (desk, _, _) = await factory.SeedUserAsync(tenantId: world.Tenant, permissions: DeskPermissions, branchIds: [world.Branch]);
        var client = await SignedInClientAsync(desk);
        List<OrderLineRequest> lines = [new(world.RandomSugar, false), new(world.FastingSugar, false)];

        var check = await client.PostAsJsonAsync($"{Visits}/check", new CheckOrderRequest(null, world.Patient, world.Payer, world.Tier, null, lines, 120));
        Assert.True(check.IsSuccessStatusCode, await check.Content.ReadAsStringAsync());
        var dryRun = (await check.Content.ReadFromJsonAsync<OrderCheckResult>())!;
        var previewTube = Assert.Single(dryRun.Tubes);
        Assert.Equal(("Fasting", 2), (previewTube.SampleConditionNameEn, previewTube.ServiceIds.Count));
        Assert.All(dryRun.Lines, line => Assert.Equal(previewTube.Key, line.TubeKey));

        var registered = await RegisterAsync(client, world, lines);
        var detail = (await client.GetFromJsonAsync<VisitDetail>($"{Visits}/{registered.Id}"))!;
        var tube = Assert.Single(detail.Tubes);
        Assert.Equal("Fasting", tube.SampleConditionNameEn);
        Assert.All(detail.Lines, line => Assert.Equal(tube.Id, line.TubeId));
        var labels = (await client.GetFromJsonAsync<RegistrationLabels>($"{Visits}/{registered.Id}/documents/labels"))!;
        Assert.Equal("Fasting", Assert.Single(labels.Labels).SampleConditionNameEn);

        await DispatchVisitEventsAsync(world.Tenant);
        var sample = Assert.Single((await client.GetFromJsonAsync<SampleLabels>($"/api/v1/samples/visits/{registered.Id}/documents/labels"))!.Labels);
        Assert.Equal((world.Fasting, "Fasting", 2), (sample.SampleConditionId, sample.SampleConditionNameEn, sample.Services.Count));

        var randomOnly = await RegisterAsync(client, world, [new(world.RandomSugar, false)]);
        Assert.Equal("Random", Assert.Single((await client.GetFromJsonAsync<VisitDetail>($"{Visits}/{randomOnly.Id}"))!.Tubes).SampleConditionNameEn);

        var twoUnits = await RegisterAsync(client, world, [new(world.FastingSugar, false), new(world.RandomInsulin, false)]);
        Assert.Equal(2, (await client.GetFromJsonAsync<VisitDetail>($"{Visits}/{twoUnits.Id}"))!.Tubes.Count);
    }

    // ---- world --------------------------------------------------------------------------------------------------------

    private sealed record World(
        Guid Tenant, Guid Branch, Guid Patient, Guid Payer, Guid Tier, Guid Serum, Guid Fasting, Guid Random, Guid Blood, Guid Edta, Guid FastingSugar, Guid RandomSugar, Guid RandomInsulin);

    private static CreateSampleCompatibilityGroupRequest Create(string code, params CompatibilityMemberItem[] members) =>
        new(code, code + " samples", "عينات " + code, members);

    private static async Task<VisitRegisteredResponse> RegisterAsync(HttpClient client, World world, IReadOnlyList<OrderLineRequest> lines)
    {
        var created = await client.PostAsJsonAsync(Visits, new RegisterVisitRequest(world.Patient, null, null, world.Payer, world.Tier, null, null, null, false, false, false, lines, null, null, false, 120));
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<VisitRegisteredResponse>())!;
    }

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

    /// <summary>
    /// A branch; chemistry FBS (serum · fasting) and RBS (serum · random); immunology INS (serum · random); serum conditions
    /// FAST, RAND, PP (inactive); whole blood with EDTA; prices; a patient.
    /// </summary>
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
        var immunology = ClinicalGroup.Create("IMM", "I", new LocalizedText("Immunology", "المناعة"), null, null, 2, now);
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
        context.Set<ClinicalGroup>().AddRange(chemistry, immunology);
        context.Set<SampleType>().AddRange(serum, blood);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        Guid Condition(SampleType type, string code) => type.Conditions.Single(condition => condition.Code == code).Id;

        Service Add(string code, int sequence, ClinicalGroup group, Guid condition)
        {
            var test = LabTest.Create(code, new LabTestDefinition(new LocalizedText(code, code), null, null, null, group.Id, LabTestType.Numeric, null, null, null, GenderRestriction.Any, null, 1, false, true, 1, null), now);
            context.Set<LabTest>().Add(test);
            var service = Service.Create(code, ServiceType.Test, new ServiceDefinition(code, null, new LocalizedText(code + " service", "خدمة " + code), null, null, null, group.Id, serum.Id, condition, null, [], null, GenderRestriction.Any, 24, 1, false, true, false, null), sequence, [new ResolvedServiceMember(ServiceMemberKind.Test, test.Id, 1, false, true, LabTestType.Numeric, null)], now);
            context.Set<Service>().Add(service);
            return service;
        }

        var fastingSugar = Add("FBS", 1, chemistry, Condition(serum, "FAST"));
        var randomSugar = Add("RBS", 2, chemistry, Condition(serum, "RAND"));
        var randomInsulin = Add("INS", 3, immunology, Condition(serum, "RAND"));
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var cash = Payer.CreateDefaultCash("CASH", now);
        context.Set<Payer>().Add(cash);
        var list = BasePriceList.Create("L2026", new LocalizedText("List 2026", "قائمة"), 2026, now);
        list.UpsertLines([new PriceLineInput(fastingSugar.Id, 60m, true), new PriceLineInput(randomSugar.Id, 50m, true), new PriceLineInput(randomInsulin.Id, 90m, true)], now);
        list.Activate(now);
        context.Set<BasePriceList>().Add(list);

        var number = await provider.GetRequiredService<IReferenceNumbers>().NextAsync(Patient.NumberKind, Patient.NumberPrefix, now, CancellationToken.None);
        var patient = Patient.Register(number, new PatientDemographics(PatientTitle.Mrs, "Mona Hassan", null, PatientGender.Female, new DateOnly(1990, 1, 1), null, null, "01001234567", null, null, null, null, null, null, null), PatientFieldRules.Optional, false, now);
        provider.GetRequiredService<IPatientRepository>().Add(patient);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        return new World(
            tenant, branch.Id, patient.Id, cash.Id, cash.Tiers.Single().Id, serum.Id,
            Condition(serum, "FAST"), Condition(serum, "RAND"), blood.Id, Condition(blood, "EDTA"), fastingSugar.Id, randomSugar.Id, randomInsulin.Id);
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
