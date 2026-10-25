using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Organization;
using Platform.Application.Results.Policies;
using Platform.Application.Samples.Policies;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;
using Platform.Contracts.Results;
using Platform.Contracts.Settings;
using Platform.Domain.Catalogue;
using Platform.Domain.Organization;
using Platform.Domain.Registration;
using Platform.Infrastructure.Persistence;
using Platform.SharedKernel.Localization;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// Report designs over the real API (LIS-CR-010, reviewer point C-14; intakes RES §18, CTB §19): the laboratory parameter
/// <c>reportDesign</c>, a report template's override (and its refusals), the resolved design on the case and on the report
/// document, and the previous values of the Cumulative design. The contract override is covered by
/// <see cref="PricingReportDesignFlowTests"/> (endpoint) and the Results use-case tests (precedence).
/// </summary>
public sealed class ResultsReportDesignFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Results = "/api/v1/results";
    private const string Templates = "/api/v1/catalogue/report-templates";
    private const string Settings = "/api/v1/settings";

    private static readonly string[] Permissions =
        ["results.view", "results.create", "results.edit", "results.approve", "settings.view", "settings.manage", "catalogue.view", "catalogue.create", "catalogue.edit"];

    [DatabaseFact]
    public async Task The_report_follows_the_laboratory_parameter_then_the_template_and_cumulative_prints_earlier_values()
    {
        var lab = await SeedLabAsync();
        var (bench, _, _) = await factory.SeedUserAsync(tenantId: lab.Tenant, permissions: Permissions, displayName: "Dr. Design", branchIds: [lab.Branch]);
        await GrantResultRightsAsync(lab.Tenant, bench, lab.Group);
        var client = await SignedInClientAsync(bench);
        var patient = Guid.CreateVersion7();

        // An earlier visit of the same patient, verified at 4.2; then today's visit at 4.8.
        var earlier = await RegisterVisitAsync(lab, patient, factory.Clock.UtcNow.AddDays(-10));
        var earlierRow = await RowAsync(client, earlier.Accession);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"{Results}/{earlierRow.Id}", new EnterResultValueRequest(4.2m, null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"{Results}/{earlierRow.Id}/verify", null)).StatusCode);
        var today = await RegisterVisitAsync(lab, patient, factory.Clock.UtcNow);
        var row = await RowAsync(client, today.Accession);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"{Results}/{row.Id}", new EnterResultValueRequest(4.8m, null, null, null))).StatusCode);

        // Default: the classic report, chosen by the laboratory.
        var unit = Assert.Single((await client.GetFromJsonAsync<ResultCaseView>($"{Results}?accession={today.Accession}"))!.Units);
        Assert.Equal(("Classic", "Laboratory"), (unit.Design, unit.DesignSource));
        var classic = await ReportAsync(client, unit.Key);
        Assert.Equal(("Classic", "Laboratory"), (classic.Design, classic.DesignSource));
        Assert.Null(Assert.Single(Assert.Single(classic.Sections).Lines).Previous);

        // Setup › Parameters › Printing: cumulative; an unknown value is SET-002.
        var invalid = await client.PutAsJsonAsync(Settings, new UpdateTenantSettingsRequest([new SettingValueItem("reportDesign", "fancy")]));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "SET-002"), (invalid.StatusCode, await AuthClient.CodeOf(invalid)));
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(Settings, new UpdateTenantSettingsRequest([new SettingValueItem("reportDesign", "cumulative")]))).StatusCode);
        var cumulative = await ReportAsync(client, unit.Key);
        Assert.Equal(("Cumulative", "Laboratory"), (cumulative.Design, cumulative.DesignSource));
        var previous = Assert.Single(Assert.Single(Assert.Single(cumulative.Sections).Lines).Previous!);
        Assert.Equal((earlier.Accession, "4.2"), (previous.Accession, previous.Value));

        // CP-54: a result template carries a design; a receipt template refuses one (CAT-273); an unknown design is a shape error.
        var receipt = await client.PostAsJsonAsync(Templates, new ReportTemplateRequest("Receipt", "إيصال", "Receipt", false, "Both", null, null, null, null, "Modern"));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, CatalogueBCodes.TemplateDesignNotApplicable), (receipt.StatusCode, await AuthClient.CodeOf(receipt)));
        var unknown = await client.PostAsJsonAsync(Templates, new ReportTemplateRequest("Results", "النتائج", "Results", true, "Both", null, null, null, null, "Fancy"));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        var created = await client.PostAsJsonAsync(Templates, new ReportTemplateRequest("Compact A4", "مختصر", "Results", true, "Both", null, null, null, null, "Compact"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var templateId = (await created.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
        Assert.Equal("Compact", (await client.GetFromJsonAsync<ReportTemplateListItem>($"{Templates}/{templateId}"))!.Design);
        await LinkTemplateAsync(lab, templateId);

        var compact = await ReportAsync(client, unit.Key);
        Assert.Equal(("Compact", "Template"), (compact.Design, compact.DesignSource));
        Assert.Null(Assert.Single(Assert.Single(compact.Sections).Lines).Previous);
        Assert.Equal("Compact", Assert.Single((await client.GetFromJsonAsync<ResultCaseView>($"{Results}?accession={today.Accession}"))!.Units).Design);

        // An edit without a design clears the override: the laboratory's cumulative design applies again.
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"{Templates}/{templateId}", new ReportTemplateRequest("Compact A4", "مختصر", "Results", true, "Both", null, null, null, null))).StatusCode);
        Assert.Null((await client.GetFromJsonAsync<ReportTemplateListItem>($"{Templates}/{templateId}"))!.Design);
        Assert.Equal("Cumulative", (await ReportAsync(client, unit.Key)).Design);
    }

    private static async Task<LaboratoryReportDocument> ReportAsync(HttpClient client, string key) =>
        (await client.GetFromJsonAsync<LaboratoryReportDocument>($"{Results}/report-units/{key}/documents/report?preview=true"))!;

    private sealed record Lab(Guid Tenant, Guid Branch, Guid Group, Guid SampleType, Guid Test, Guid Service);

    /// <summary>A clinical group, a sample type, a numeric potassium test (no delta) and its single-test service in a new tenant.</summary>
    private async Task<Lab> SeedLabAsync()
    {
        var tenant = Guid.CreateVersion7();
        using var scope = factory.Services.CreateScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(tenant);
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var now = factory.Clock.UtcNow;

        var group = ClinicalGroup.Create("CHEM", "C", new LocalizedText("Chemistry", "الكيمياء"), "Clinical chemistry", null, 1, now);
        var sampleType = SampleType.Create("SER", "Serum", new LocalizedText("Serum", "مصل"), [], now);
        context.Set<ClinicalGroup>().Add(group);
        context.Set<SampleType>().Add(sampleType);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var test = LabTest.Create("K", new LabTestDefinition(new LocalizedText("Potassium", "بوتاسيوم"), "Potassium", null, null, group.Id, LabTestType.Numeric, null, null, null, GenderRestriction.Any, null, 1, false, true, 1, null), now);
        test.ReplaceRangeBands([new RangeBandSpec(GenderRestriction.Any, 0, 150, RangeAgeUnit.Years, null, 3.5m, 5.0m, 2.5m, 6.5m, null, null, null)], now);
        context.Set<LabTest>().Add(test);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var service = Service.Create(
            "K",
            ServiceType.Test,
            new ServiceDefinition("K", null, new LocalizedText("Potassium", "بوتاسيوم"), null, null, null, group.Id, sampleType.Id, null, null, [], null, GenderRestriction.Any, 24, 1, false, true, false, null),
            10,
            [new ResolvedServiceMember(ServiceMemberKind.Test, test.Id, 1, false, true, LabTestType.Numeric, null)],
            now);
        context.Set<Service>().Add(service);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        return new Lab(tenant, Guid.CreateVersion7(), group.Id, sampleType.Id, test.Id, service.Id);
    }

    /// <summary>The clinical group prints with the template (CP-50 edit, done in the database here).</summary>
    private async Task LinkTemplateAsync(Lab lab, Guid templateId)
    {
        using var scope = factory.Services.CreateScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(lab.Tenant);
        var group = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<ClinicalGroup>().SingleAsync(item => item.Id == lab.Group);
        group.Change(group.UnitCode, group.Name, group.ReportName, templateId, group.DisplayOrder, factory.Clock.UtcNow);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Runs the Samples and Results policies for a registration of <paramref name="patient"/> at <paramref name="at"/> (the tube is received at once).</summary>
    private async Task<VisitRegistered> RegisterVisitAsync(Lab lab, Guid patient, DateTimeOffset at)
    {
        var suffix = Guid.CreateVersion7().ToString("N")[^6..].ToUpperInvariant();
        var tube = Guid.CreateVersion7();
        var line = new RegisteredLine(Guid.CreateVersion7(), lab.Service, tube, "Ordered", [lab.Test]);
        var registered = new VisitRegistered(
            Guid.CreateVersion7(), $"ACC-{suffix}", lab.Branch, patient, null, null, false, Guid.CreateVersion7(),
            [new RegisteredTube(tube, $"SMP-{suffix}", lab.Group, lab.SampleType, null, [lab.Service])], [line], at);

        using var scope = factory.Services.CreateScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(lab.Tenant);
        var handlers = scope.ServiceProvider.GetServices<IDomainEventHandler<VisitRegistered>>().ToList();
        await handlers.OfType<CreateSamplesOnVisitRegisteredPolicy>().Single().HandleAsync(registered, CancellationToken.None);
        await handlers.OfType<CreateResultsOnVisitRegisteredPolicy>().Single().HandleAsync(registered, CancellationToken.None);
        return registered;
    }

    private async Task GrantResultRightsAsync(Guid tenant, string username, params Guid[] groups)
    {
        using var scope = factory.Services.CreateScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(tenant);
        var role = (await scope.ServiceProvider.GetRequiredService<IRoleRepository>().FindByNameAsync($"role-{username}", CancellationToken.None))!;
        role.SetResultRights(groups.Select(group => ResultRight.Full(group)), factory.Clock.UtcNow);
        role.SetSpecialRights(SpecialRights.All.Copy(), factory.Clock.UtcNow);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    private static async Task<ResultRowView> RowAsync(HttpClient client, string accession) =>
        Assert.Single(Assert.Single((await client.GetFromJsonAsync<ResultCaseView>($"{Results}?accession={accession}"))!.Units).Results);

    private async Task<HttpClient> SignedInClientAsync(string username)
    {
        var client = factory.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(username, ApiFactory.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }
}
