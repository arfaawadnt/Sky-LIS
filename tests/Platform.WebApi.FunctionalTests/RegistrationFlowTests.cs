using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Catalogue;
using Platform.Application.Patients;
using Platform.Contracts.Common;
using Platform.Contracts.Registration;
using Platform.Domain.Catalogue;
using Platform.Domain.Organization;
using Platform.Domain.Patients;
using Platform.Domain.Pricing;
using Platform.Infrastructure.Persistence;
using Platform.SharedKernel.Localization;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// The Release 1 registration workflow over the real API (intake LIS-INT-REG-001 §16, WF-02): settings, service entry,
/// dry run, register, search, detail, documents, price query and cancellation; permissions (D-04), the session branch
/// (TENANT-004), the special Cancel right and shape violations answer their codes.
/// </summary>
public sealed class RegistrationFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Visits = "/api/v1/registration/visits";
    private const string Registration = "/api/v1/registration";

    private static readonly string[] ClerkPermissions = ["registration.view", "registration.create", "registration.edit", "registration.cancel", "registration.export", "catalogue.view"];

    [DatabaseFact]
    public async Task A_clerk_registers_a_walk_in_finds_it_prints_its_documents_and_cancels_a_service()
    {
        var tenant = Guid.CreateVersion7();
        var branch = await SeedBranchAsync(tenant);
        var (clerk, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ClerkPermissions, displayName: "Reception Clerk", branchIds: [branch]);
        await GrantRightsAsync(tenant, clerk, SpecialRights.All);
        var world = await SeedAsync(tenant);
        var client = await SignedInClientAsync(clerk);

        var settings = (await client.GetFromJsonAsync<RegistrationPageSettings>($"{Registration}/settings"))!;
        Assert.True(settings.AutoPrintLabels);
        Assert.True(settings.CanCancel);
        Assert.Equal("Main branch", settings.BranchNameEn);   // the session's branch for the page header (validation 2026-10-11)

        var entry = (await client.GetFromJsonAsync<ServiceEntryResult>($"{Registration}/service-entry?text=12"))!;
        Assert.Equal(("Service", world.Glucose), (entry.Kind, Assert.Single(entry.Services).Id));

        var lines = new List<OrderLineRequest> { new(world.Glucose, false), new(world.Cbc, true) };
        var check = await client.PostAsJsonAsync($"{Visits}/check", new CheckOrderRequest(null, world.Patient, world.Payer, world.Tier, null, lines, 120));
        Assert.Equal(HttpStatusCode.OK, check.StatusCode);
        var dryRun = (await check.Content.ReadFromJsonAsync<OrderCheckResult>())!;
        Assert.Equal((2, 1, 150m), (dryRun.Lines.Count, dryRun.Tubes.Count, dryRun.Totals.PatientShareTotal));
        Assert.Contains(dryRun.Warnings, warning => warning.Code == "REG-203");

        var created = await client.PostAsJsonAsync(Visits, Register(world, lines) with { Biohazard = true });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var registered = (await created.Content.ReadFromJsonAsync<VisitRegisteredResponse>())!;
        Assert.StartsWith("ACC-", registered.Accession, StringComparison.Ordinal);

        var found = (await client.GetFromJsonAsync<Paged<VisitListItem>>($"{Visits}?name=mona&status=Open"))!;
        var row = Assert.Single(found.Items);
        Assert.Equal((registered.Accession, "Mona Hassan", 150m), (row.Accession, row.PatientName, row.PatientShareTotal));
        Assert.Equal(["Ordered", "Reserved"], row.Lines.Select(line => line.Status).ToArray());

        var detail = (await client.GetFromJsonAsync<VisitDetail>($"{Visits}/{registered.Id}"))!;
        Assert.Equal(("Open", "Walkin", 2, 1), (detail.Status, detail.LocationType, detail.Lines.Count, detail.Tubes.Count));

        var labels = (await client.GetFromJsonAsync<VisitLabelsDocument>($"{Visits}/{registered.Id}/documents/labels"))!;
        Assert.Equal(("C", "GLU"), (Assert.Single(labels.Labels).UnitCode, labels.Labels[0].ServiceCodes.Single()));
        Assert.Equal((true, false), (labels.Labels[0].Biohazard, labels.Labels[0].Urgent));   // the label marks a biohazard visit (validation 2026-10-11)
        var jobOrder = (await client.GetFromJsonAsync<JobOrderDocument>($"{Visits}/{registered.Id}/documents/job-order"))!;
        Assert.Equal((registered.Accession, 2, 1), (jobOrder.Accession, jobOrder.Lines.Count, jobOrder.VisitOrdinal));

        var quote = await client.PostAsJsonAsync($"{Registration}/quote", new PriceQuoteRequest(world.Payer, world.Tier, [world.Glucose, world.Urea], "Female", 120));
        var quoted = (await quote.Content.ReadFromJsonAsync<PriceQuoteDocument>())!;
        Assert.Equal((180m, 2), (quoted.TotalPatientShare, quoted.Lines.Count));

        var exported = (await client.GetFromJsonAsync<List<VisitListItem>>($"{Visits}/export?accession={registered.Accession}"))!;
        Assert.Single(exported);

        var glucoseLine = detail.Lines.Single(line => line.ServiceId == world.Glucose).Id;
        var noReason = await client.PostAsJsonAsync($"{Visits}/{registered.Id}/lines/cancel", new CancelLinesRequest([glucoseLine], null, null));
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal(ProblemCodes.ValidationFailed, await AuthClient.CodeOf(noReason));

        var cancelled = await client.PostAsJsonAsync($"{Visits}/{registered.Id}/lines/cancel", new CancelLinesRequest([glucoseLine], null, "Ordered by mistake"));
        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        var after = (await client.GetFromJsonAsync<VisitDetail>($"{Visits}/{registered.Id}"))!;
        Assert.Equal(("Cancelled", "Ordered by mistake", 50m), (after.Lines.Single(line => line.Id == glucoseLine).Status, after.Lines.Single(line => line.Id == glucoseLine).CancellationReason, after.PatientShareTotal));

        var again = await client.PostAsJsonAsync($"{Visits}/{registered.Id}/lines/cancel", new CancelLinesRequest([glucoseLine], null, "again"));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("REG-104", await AuthClient.CodeOf(again));
    }

    [DatabaseFact]
    public async Task Refusals_answer_their_codes()
    {
        var tenant = Guid.CreateVersion7();
        var (branchA, branchB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var (clerk, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ClerkPermissions, branchIds: [branchA]);
        var (twoBranches, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ClerkPermissions, branchIds: [branchA, branchB]);
        var (outsider, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ["catalogue.view"], branchIds: [branchA]);
        var world = await SeedAsync(tenant);
        var client = await SignedInClientAsync(clerk);

        var forbidden = await (await SignedInClientAsync(outsider)).GetAsync(Visits);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(ProblemCodes.Forbidden, await AuthClient.CodeOf(forbidden));

        var noBranch = await (await SignedInClientAsync(twoBranches)).PostAsJsonAsync(Visits, Register(world, [new(world.Glucose, false)]));
        Assert.Equal(HttpStatusCode.Forbidden, noBranch.StatusCode);
        Assert.Equal(ProblemCodes.BranchRequired, await AuthClient.CodeOf(noBranch));

        var empty = await client.PostAsJsonAsync(Visits, Register(world, []));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(ProblemCodes.ValidationFailed, await AuthClient.CodeOf(empty));

        var unknown = await client.PostAsJsonAsync(Visits, Register(world, [new(Guid.CreateVersion7(), false)]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknown.StatusCode);
        Assert.Equal("REG-012", await AuthClient.CodeOf(unknown));

        var inactive = await client.PostAsJsonAsync(Visits, Register(world, [new(world.Inactive, false)]));
        Assert.Equal("REG-013", await AuthClient.CodeOf(inactive));

        var duplicate = await client.PostAsJsonAsync(Visits, Register(world, [new(world.Glucose, false), new(world.Glucose, true)]));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("REG-100", await AuthClient.CodeOf(duplicate));

        var created = (await (await client.PostAsJsonAsync(Visits, Register(world, [new(world.Glucose, false)]))).Content.ReadFromJsonAsync<VisitRegisteredResponse>())!;
        var detail = (await client.GetFromJsonAsync<VisitDetail>($"{Visits}/{created.Id}"))!;
        var noRight = await client.PostAsJsonAsync($"{Visits}/{created.Id}/lines/cancel", new CancelLinesRequest([detail.Lines[0].Id], null, "x"));
        Assert.Equal(HttpStatusCode.Forbidden, noRight.StatusCode);
        Assert.Equal("REG-061", await AuthClient.CodeOf(noRight));

        var missing = await client.GetAsync($"{Visits}/{Guid.CreateVersion7()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("REG-404", await AuthClient.CodeOf(missing));
    }

    private static RegisterVisitRequest Register(World world, IReadOnlyList<OrderLineRequest> lines) =>
        new(world.Patient, null, null, world.Payer, world.Tier, null, null, null, false, false, false, lines, null, null, false, 120);

    private sealed record World(Guid Patient, Guid Payer, Guid Tier, Guid Glucose, Guid Urea, Guid Cbc, Guid Inactive);

    /// <summary>The session's branch as an Organization row, so the page settings can name it.</summary>
    private async Task<Guid> SeedBranchAsync(Guid tenant)
    {
        using var scope = factory.Services.CreateScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(tenant);
        var branch = Branch.Create("MAIN", new LocalizedText("Main branch", "الفرع الرئيسي"), null, null, null, null, "EGP", factory.Clock.UtcNow);
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<Branch>().Add(branch);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return branch.Id;
    }

    /// <summary>Chemistry (serum) GLU (shortcut 12, precaution) + UREA, haematology (whole blood) CBC, an inactive OLD; the cash payer with the default list; a patient.</summary>
    private async Task<World> SeedAsync(Guid tenant)
    {
        var now = factory.Clock.UtcNow;
        using var scope = factory.Services.CreateScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(tenant);
        var provider = scope.ServiceProvider;
        var chemistry = ClinicalGroup.Create("CHEM", "C", new LocalizedText("Chemistry", "الكيمياء"), null, null, 1, now);
        var haematology = ClinicalGroup.Create("HEMA", "H", new LocalizedText("Haematology", "أمراض الدم"), null, null, 2, now);
        var serum = SampleType.Create("SERUM", "SER", new LocalizedText("Serum", "مصل"), [], now);
        var blood = SampleType.Create("WB", "WB", new LocalizedText("Whole blood", "دم كامل"), [], now);
        provider.GetRequiredService<IClinicalGroupRepository>().Add(chemistry);
        provider.GetRequiredService<IClinicalGroupRepository>().Add(haematology);
        provider.GetRequiredService<ISampleTypeRepository>().Add(serum);
        provider.GetRequiredService<ISampleTypeRepository>().Add(blood);

        var tests = provider.GetRequiredService<ILabTestRepository>();
        var services = provider.GetRequiredService<IServiceRepository>();
        Service Add(string code, int sequence, int? shortcut, ClinicalGroup group, SampleType sample, string? precaution = null)
        {
            var test = LabTest.Create(code + "T", new LabTestDefinition(new LocalizedText(code, code), null, null, null, group.Id, LabTestType.Numeric, null, null, null, GenderRestriction.Any, null, 0, false, true, 1, null), now);
            tests.Add(test);
            var service = Service.Create(code, ServiceType.Test, new ServiceDefinition(code, shortcut, new LocalizedText(code + " service", "خدمة " + code), null, null, null, group.Id, sample.Id, null, null, [], precaution, GenderRestriction.Any, 0, 1, false, true, false, null), sequence, [new ResolvedServiceMember(ServiceMemberKind.Test, test.Id, 1, false, true, LabTestType.Numeric, null)], now);
            services.Add(service);
            return service;
        }

        var glucose = Add("GLU", 1, 12, chemistry, serum, "Fasting 8 hours");
        var urea = Add("UREA", 2, null, chemistry, serum);
        var cbc = Add("CBC", 3, null, haematology, blood);
        var old = Add("OLD", 4, null, chemistry, serum);
        old.Deactivate(now);

        var context = provider.GetRequiredService<AppDbContext>();
        var cash = Payer.CreateDefaultCash("CASH", now);
        context.Set<Payer>().Add(cash);
        var list = BasePriceList.Create("L2026", new LocalizedText("List 2026", "قائمة"), 2026, now);
        list.UpsertLines([new PriceLineInput(glucose.Id, 100m, true), new PriceLineInput(urea.Id, 80m, true), new PriceLineInput(cbc.Id, 50m, true)], now);
        list.Activate(now);
        context.Set<BasePriceList>().Add(list);

        var number = await provider.GetRequiredService<IReferenceNumbers>().NextAsync(Patient.NumberKind, Patient.NumberPrefix, now, CancellationToken.None);
        var patient = Patient.Register(number, new PatientDemographics(PatientTitle.Mrs, "Mona Hassan", null, PatientGender.Female, new DateOnly(1990, 1, 1), null, null, "01001234567", null, null, null, null, null, null, null), PatientFieldRules.Optional, false, now);
        provider.GetRequiredService<IPatientRepository>().Add(patient);

        await provider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return new World(patient.Id, cash.Id, cash.Tiers.Single().Id, glucose.Id, urea.Id, cbc.Id, old.Id);
    }

    /// <summary>The special rights of the role the factory gave the user (CP-37): Cancel, Transfer … for this test.</summary>
    private async Task GrantRightsAsync(Guid tenant, string username, SpecialRights rights)
    {
        using var scope = factory.Services.CreateScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(tenant);
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var role = await context.Set<Role>().SingleAsync(candidate => candidate.Name.En == $"role-{username}");
        role.SetSpecialRights(rights, factory.Clock.UtcNow);
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
