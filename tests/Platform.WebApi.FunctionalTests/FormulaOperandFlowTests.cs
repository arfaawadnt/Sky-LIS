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
using Platform.Contracts.Billing;
using Platform.Contracts.Registration;
using Platform.Domain.Catalogue;
using Platform.Domain.Organization;
using Platform.Domain.Patients;
using Platform.Domain.Pricing;
using Platform.Domain.Results;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Persistence.Outbox;
using Platform.SharedKernel.Localization;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// LIS-CR-015 M3b / M3c over the real API: registering a formula service orders the service chosen for each test the formula
/// reads as a free line under it (here a formula saved before the change: its operand is the test's own single-test service,
/// off the receipt), the operand line gets its tube and results, follows its formula when that is cancelled, is never
/// cancelled alone (REG-026), shows in the dry run and the price query at 0 with its formula, and stays off the Receipt /
/// invoice while the job order lists it. Needs package P-FCT's <c>FormulaOperandLookup</c> (the seam answers no operand).
/// </summary>
public sealed class FormulaOperandFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Visits = "/api/v1/registration/visits";
    private const string Registration = "/api/v1/registration";

    private static readonly string[] DeskPermissions =
    [
        "registration.view", "registration.create", "registration.edit", "registration.cancel", "catalogue.view", "billing.view",
    ];

    [DatabaseFact]
    public async Task A_formula_service_orders_its_operand_free_with_tube_and_results_and_keeps_it_off_the_receipt()
    {
        var world = await SeedAsync();
        var client = await DeskClientAsync(world);
        List<OrderLineRequest> lines = [new(world.Gfr, false)];

        var check = (await (await client.PostAsJsonAsync($"{Visits}/check", new CheckOrderRequest(null, world.Patient, world.Payer, world.Tier, null, lines, 120))).Content.ReadFromJsonAsync<OrderCheckResult>())!;
        var planned = Assert.Single(check.Lines, line => line.Kind == "FormulaOperand");
        Assert.Equal(("CREA", "GFR", false, 0m), (planned.Code, planned.FormulaServiceCode, planned.VisibleOnReceipt, planned.PatientShare));
        Assert.Equal(100m, check.Totals.PatientShareTotal);
        Assert.Single(check.Tubes);

        var quote = (await (await client.PostAsJsonAsync($"{Registration}/quote", new PriceQuoteRequest(world.Payer, world.Tier, [world.Gfr], "Female", 120))).Content.ReadFromJsonAsync<PriceQuoteDocument>())!;
        Assert.Equal(["GFR", "CREA"], quote.Lines.Select(line => line.Code).ToArray());
        Assert.Equal(("FormulaOperand", "GFR", 0m), (quote.Lines[1].Kind, quote.Lines[1].FormulaServiceCode, quote.Lines[1].PatientShare));
        Assert.Equal(100m, quote.TotalPatientShare);

        var created = await client.PostAsJsonAsync(Visits, Register(world, lines));
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var registered = (await created.Content.ReadFromJsonAsync<VisitRegisteredResponse>())!;

        var detail = (await client.GetFromJsonAsync<VisitDetail>($"{Visits}/{registered.Id}"))!;
        var gfr = detail.Lines.Single(line => line.ServiceId == world.Gfr);
        var crea = detail.Lines.Single(line => line.ServiceId == world.Crea);
        Assert.Equal(("FormulaOperand", "Formula", (Guid?)gfr.Id, false), (crea.Kind, crea.Origin, crea.ParentLineId, crea.VisibleOnReceipt));
        Assert.Equal((0m, 0m), (crea.ListPrice, crea.PatientShare));
        Assert.Equal((100m, 100m), (detail.TotalListPrice, detail.PatientShareTotal));
        Assert.Equal(gfr.TubeId, crea.TubeId);
        Assert.Single(detail.Tubes);

        await DispatchVisitRegisteredAsync(world.Tenant);
        Assert.Contains(await ResultTestsAsync(world.Tenant, registered.Id, crea.Id), testId => testId == world.CreaTest);

        var invoice = (await client.GetFromJsonAsync<InvoiceDocument>($"/api/v1/billing/visits/{registered.Id}/documents/invoice"))!;
        Assert.Equal([world.Gfr], invoice.Lines.Select(line => line.ServiceId).ToArray());
        Assert.Equal(100m, invoice.PatientShareTotal);

        var jobOrder = (await client.GetFromJsonAsync<JobOrderDocument>($"{Visits}/{registered.Id}/documents/job-order"))!;
        var operandRow = jobOrder.Lines.Single(line => line.Code == "CREA");
        Assert.Equal(("FormulaOperand", "GFR"), (operandRow.Kind, operandRow.FormulaServiceCode));
    }

    [DatabaseFact]
    public async Task The_operand_follows_its_formula_and_is_never_cancelled_alone()
    {
        var world = await SeedAsync();
        var client = await DeskClientAsync(world);
        var registered = (await (await client.PostAsJsonAsync(Visits, Register(world, [new(world.Gfr, false)]))).Content.ReadFromJsonAsync<VisitRegisteredResponse>())!;
        var detail = (await client.GetFromJsonAsync<VisitDetail>($"{Visits}/{registered.Id}"))!;
        var gfr = detail.Lines.Single(line => line.ServiceId == world.Gfr);
        var crea = detail.Lines.Single(line => line.ServiceId == world.Crea);

        var alone = await client.PostAsJsonAsync($"{Visits}/{registered.Id}/lines/cancel", new CancelLinesRequest([crea.Id], null, "Not needed"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, alone.StatusCode);
        Assert.Equal("REG-026", await AuthClient.CodeOf(alone));

        var cancelled = await client.PostAsJsonAsync($"{Visits}/{registered.Id}/lines/cancel", new CancelLinesRequest([gfr.Id], null, "Ordered by mistake"));
        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        var after = (await client.GetFromJsonAsync<VisitDetail>($"{Visits}/{registered.Id}"))!;
        Assert.All(after.Lines, line => Assert.Equal("Cancelled", line.Status));
        Assert.Equal(0m, after.PatientShareTotal);
    }

    [DatabaseFact]
    public async Task An_operand_already_on_the_visit_is_not_ordered_again()
    {
        var world = await SeedAsync();
        var client = await DeskClientAsync(world);
        var registered = (await (await client.PostAsJsonAsync(Visits, Register(world, [new(world.Crea, false), new(world.Gfr, false)]))).Content.ReadFromJsonAsync<VisitRegisteredResponse>())!;

        var detail = (await client.GetFromJsonAsync<VisitDetail>($"{Visits}/{registered.Id}"))!;
        Assert.DoesNotContain(detail.Lines, line => line.Kind == "FormulaOperand");
        Assert.Equal(140m, detail.PatientShareTotal);
    }

    // ---- world --------------------------------------------------------------------------------------------------------

    private sealed record World(Guid Tenant, Guid Branch, Guid Patient, Guid Payer, Guid Tier, Guid Gfr, Guid Crea, Guid CreaTest);

    private static RegisterVisitRequest Register(World world, IReadOnlyList<OrderLineRequest> lines) =>
        new(world.Patient, null, null, world.Payer, world.Tier, null, null, null, false, false, false, lines, null, null, false, 120);

    private async Task<HttpClient> DeskClientAsync(World world)
    {
        var (desk, _, _) = await factory.SeedUserAsync(tenantId: world.Tenant, permissions: DeskPermissions, displayName: "Front Desk", branchIds: [world.Branch]);
        using (var scope = factory.Services.CreateScope())
        using (scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(world.Tenant))
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var role = await context.Set<Role>().SingleAsync(candidate => candidate.Name.En == $"role-{desk}");
            role.SetSpecialRights(SpecialRights.All, factory.Clock.UtcNow);   // Cancel and Print invoice
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        var client = factory.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(desk, ApiFactory.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }

    private async Task<List<Guid>> ResultTestsAsync(Guid tenant, Guid visitId, Guid lineId)
    {
        using var scope = factory.Services.CreateScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(tenant);
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.Set<Result>().AsNoTracking().Where(result => result.VisitId == visitId && result.LineId == lineId).Select(result => result.TestId).ToListAsync();
    }

    /// <summary>Runs every handler of the tenant's pending <c>VisitRegistered</c> events, the way the outbox processor would (the factory keeps its loop off).</summary>
    private async Task DispatchVisitRegisteredAsync(Guid tenant)
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
    /// A branch; chemistry on serum: CREA (single-test service, 40) and GFR (a formula test reading [CREA], saved without operands
    /// as before the change, 100); the cash payer with prices; a patient.
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
        var serum = SampleType.Create("SERUM", "SER", new LocalizedText("Serum", "مصل"), [], now);
        context.Set<ClinicalGroup>().Add(chemistry);
        context.Set<SampleType>().Add(serum);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        (Service Service, LabTest Test) Add(string code, int sequence, LabTestType type)
        {
            var test = LabTest.Create(code, new LabTestDefinition(new LocalizedText(code, code), null, null, null, chemistry.Id, type, null, null, null, GenderRestriction.Any, null, 1, false, true, 1, null), now);
            context.Set<LabTest>().Add(test);
            var service = Service.Create(code, ServiceType.Test, new ServiceDefinition(code, null, new LocalizedText(code + " service", "خدمة " + code), null, null, null, chemistry.Id, serum.Id, null, null, [], null, GenderRestriction.Any, 24, 1, false, true, false, null), sequence, [new ResolvedServiceMember(ServiceMemberKind.Test, test.Id, 1, false, true, type, null)], now);
            context.Set<Service>().Add(service);
            return (service, test);
        }

        var crea = Add("CREA", 1, LabTestType.Numeric);
        var gfr = Add("GFR", 2, LabTestType.Formula);
        gfr.Test.SetFormula("[CREA]", ["CREA"], now);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var cash = Payer.CreateDefaultCash("CASH", now);
        context.Set<Payer>().Add(cash);
        var list = BasePriceList.Create("L2026", new LocalizedText("List 2026", "قائمة"), 2026, now);
        list.UpsertLines([new PriceLineInput(crea.Service.Id, 40m, true), new PriceLineInput(gfr.Service.Id, 100m, true)], now);
        list.Activate(now);
        context.Set<BasePriceList>().Add(list);

        var number = await provider.GetRequiredService<IReferenceNumbers>().NextAsync(Patient.NumberKind, Patient.NumberPrefix, now, CancellationToken.None);
        var patient = Patient.Register(number, new PatientDemographics(PatientTitle.Mrs, "Mona Hassan", null, PatientGender.Female, new DateOnly(1990, 1, 1), null, null, "01001234567", null, null, null, null, null, null, null), PatientFieldRules.Optional, false, now);
        provider.GetRequiredService<IPatientRepository>().Add(patient);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        return new World(tenant, branch.Id, patient.Id, cash.Id, cash.Tiers.Single().Id, gfr.Service.Id, crea.Service.Id, crea.Test.Id);
    }
}
