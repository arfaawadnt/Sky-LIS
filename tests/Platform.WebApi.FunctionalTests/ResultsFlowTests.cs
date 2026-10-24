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
using Platform.Contracts.Common;
using Platform.Contracts.Results;
using Platform.Domain.Catalogue;
using Platform.Domain.Organization;
using Platform.Domain.Registration;
using Platform.Infrastructure.Persistence;
using Platform.SharedKernel.Localization;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// WF-06 over the real API (intake LIS-INT-RES-001 §16): results arrive through the registration policy on a received
/// tube, are entered, flagged, verified, printed and amended with the result rights of the user's role; refusals arrive as
/// their codes; permissions (D-04), the session branch (TENANT-004), result rights and tenants are enforced.
/// </summary>
public sealed class ResultsFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Results = "/api/v1/results";

    private static readonly string[] BenchPermissions = ["results.view", "results.create", "results.edit", "results.approve", "results.cancel", "results.export"];
    private static readonly string[] ViewerPermissions = ["results.view", "results.create"];

    [DatabaseFact]
    public async Task A_value_is_entered_flagged_verified_printed_and_amended_on_the_report_unit()
    {
        var lab = await SeedLabAsync();
        var (bench, _, _) = await factory.SeedUserAsync(tenantId: lab.Tenant, permissions: BenchPermissions, displayName: "Dr. Bench", branchIds: [lab.Branch]);
        await GrantResultRightsAsync(lab.Tenant, bench, SpecialRights.All, lab.Group);
        var client = await SignedInClientAsync(bench);
        var visit = await RegisterVisitAsync(lab);

        var opened = (await client.GetFromJsonAsync<ResultCaseView>($"{Results}?accession={visit.Accession}"))!;
        var unit = Assert.Single(opened.Units);
        var row = Assert.Single(unit.Results);
        Assert.Equal(("Ordered", "Received", true), (row.Status, row.SampleStatus, unit.Rights.CanVerify));
        Assert.Equal(0, opened.HiddenUnits);

        var entered = await client.PutAsJsonAsync($"{Results}/{row.Id}", new EnterResultValueRequest(6.84m, null, null, null));
        Assert.Equal(HttpStatusCode.NoContent, entered.StatusCode);
        row = await RowAsync(client, visit.Accession);
        Assert.Equal(("Complete", "6.8", "CriticalHigh", true), (row.Status, row.TextValue, row.Flag, row.IsPanic));
        Assert.Equal("3.5 – 5.0", row.Range!.Display);

        var bulk = await client.PutAsJsonAsync($"{Results}/values", new SaveResultValuesRequest([new ResultValueItem(row.Id, null, "abc", null, null)]));
        Assert.Equal(HttpStatusCode.OK, bulk.StatusCode);
        Assert.Equal("RES-003", Assert.Single((await bulk.Content.ReadFromJsonAsync<BulkResultOutcome>())!.Refused).Code);

        var badNumber = await client.PutAsJsonAsync($"{Results}/{row.Id}", new EnterResultValueRequest(null, "abc", null, null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badNumber.StatusCode);
        Assert.Equal("RES-003", await AuthClient.CodeOf(badNumber));

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"{Results}/{row.Id}/comment", new ResultTextRequest("Repeat advised."))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"{Results}/{row.Id}/note", new ResultTextRequest("Called the ward."))).StatusCode);

        var preview = (await client.GetFromJsonAsync<LaboratoryReportDocument>($"{Results}/report-units/{unit.Key}/documents/report?preview=true"))!;
        Assert.True(preview.IsPreliminary);
        Assert.Equal("Repeat advised.", Assert.Single(Assert.Single(preview.Sections).Lines).Comment);
        var notYet = await client.PostAsync($"{Results}/report-units/{unit.Key}/print", null);
        Assert.Equal(HttpStatusCode.Conflict, notYet.StatusCode);
        Assert.Equal("RES-021", await AuthClient.CodeOf(notYet));

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"{Results}/{row.Id}/verify", null)).StatusCode);
        var twice = await client.PostAsync($"{Results}/{row.Id}/verify", null);
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Equal("RES-013", await AuthClient.CodeOf(twice));
        row = await RowAsync(client, visit.Accession);
        Assert.Equal(("Verified", "Dr. Bench"), (row.Status, row.VerifiedByName));

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"{Results}/report-units/{unit.Key}/print", null)).StatusCode);
        var printed = (await client.GetFromJsonAsync<LaboratoryReportDocument>($"{Results}/report-units/{unit.Key}/documents/report?preview=false"))!;
        Assert.Equal((false, 1, "Dr. Bench", visit.Accession), (printed.IsPreliminary, printed.PrintCount, printed.Signature.VerifiedByName, printed.QrValue));

        var undo = await client.PostAsJsonAsync($"{Results}/{row.Id}/undo", new UndoResultRequest("Recheck."));
        Assert.Equal(HttpStatusCode.Conflict, undo.StatusCode);
        Assert.Equal("RES-018", await AuthClient.CodeOf(undo));

        var noReason = await client.PostAsJsonAsync($"{Results}/{row.Id}/amend", new AmendResultRequest(4.6m, null, null, null, ""));
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal(ProblemCodes.ValidationFailed, await AuthClient.CodeOf(noReason));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"{Results}/{row.Id}/amend", new AmendResultRequest(4.6m, null, null, null, "Transcription error."))).StatusCode);
        row = await RowAsync(client, visit.Accession);
        Assert.Equal(("Complete", 1, 1, (string?)null), (row.Status, row.AmendmentCount, row.PrintCount, row.Flag == "Normal" ? null : row.Flag));

        var log = (await client.GetFromJsonAsync<List<ResultLogItem>>($"{Results}/{row.Id}/log"))!;
        Assert.Equal("Ordered", log[0].Kind);
        Assert.Contains(log, item => item.Kind == "Value" && item.ValueAfter == "4.6" && item.Detail == "Transcription error.");
        Assert.Empty((await client.GetFromJsonAsync<List<PreviousResultItem>>($"{Results}/{row.Id}/previous"))!);

        var worklist = (await client.GetFromJsonAsync<WorklistPage>($"{Results}/worklist?status=Complete&search={visit.Accession}"))!;
        Assert.Equal((1L, 1L), (worklist.TotalCount, worklist.Counters.Requests));
        Assert.Equal(row.Id, Assert.Single(worklist.Items).ResultId);
        var bench_list = (await client.GetFromJsonAsync<WorklistDocument>($"{Results}/worklist/documents/worklist?search={visit.Accession}"))!;
        Assert.Single(bench_list.Rows);

        var visits = (await client.GetFromJsonAsync<Paged<ResultVisitListItem>>($"{Results}/visits?accession={visit.Accession}"))!;
        Assert.Equal(("Complete", 1), (Assert.Single(visits.Items).Status, visits.Items[0].Units));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{Results}/lookups/comment-templates?search=%25rep")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{Results}/lookups/microbiology")).StatusCode);
    }

    [DatabaseFact]
    public async Task C08_an_ordered_line_shows_the_patient_s_range_before_entry_and_the_stored_range_after()
    {
        // LIS-CR-007 C-08: before the first save the case answers the band BR-08 picks (provisional); once a value exists the
        // stored range is served, even after the catalogue band changes, while a new visit shows the new band.
        var lab = await SeedLabAsync();
        var (bench, _, _) = await factory.SeedUserAsync(tenantId: lab.Tenant, permissions: BenchPermissions, branchIds: [lab.Branch]);
        await GrantResultRightsAsync(lab.Tenant, bench, SpecialRights.All, lab.Group);
        var client = await SignedInClientAsync(bench);
        var visit = await RegisterVisitAsync(lab);

        var ordered = await RowAsync(client, visit.Accession);
        Assert.Equal(("Ordered", "3.5 – 5.0", 3.5m, 5.0m, true), (ordered.Status, ordered.Range!.Display, ordered.Range.Low!.Value, ordered.Range.High!.Value, ordered.RangeProvisional));

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"{Results}/{ordered.Id}", new EnterResultValueRequest(4.2m, null, null, null))).StatusCode);
        await ReplaceBandAsync(lab, 3.0m, 4.0m);

        var entered = await RowAsync(client, visit.Accession);
        Assert.Equal(("Complete", "3.5 – 5.0", false), (entered.Status, entered.Range!.Display, entered.RangeProvisional));
        var next = await RowAsync(client, (await RegisterVisitAsync(lab)).Accession);
        Assert.Equal(("3.0 – 4.0", true), (next.Range!.Display, next.RangeProvisional));
    }

    [DatabaseFact]
    public async Task Permissions_branch_result_rights_and_tenants_are_enforced()
    {
        var lab = await SeedLabAsync();
        var visit = await RegisterVisitAsync(lab);
        var (viewer, _, _) = await factory.SeedUserAsync(tenantId: lab.Tenant, permissions: ViewerPermissions, branchIds: [lab.Branch]);
        await GrantResultRightsAsync(lab.Tenant, viewer, SpecialRights.None, lab.Group);
        var (noRights, _, _) = await factory.SeedUserAsync(tenantId: lab.Tenant, permissions: BenchPermissions, branchIds: [lab.Branch]);
        var (branchless, _, _) = await factory.SeedUserAsync(tenantId: lab.Tenant, permissions: BenchPermissions);
        await GrantResultRightsAsync(lab.Tenant, branchless, SpecialRights.All, lab.Group);
        var (stranger, _, _) = await factory.SeedUserAsync(permissions: BenchPermissions, branchIds: [Guid.CreateVersion7()]);

        var viewerClient = await SignedInClientAsync(viewer);
        var row = await RowAsync(viewerClient, visit.Accession);
        var verify = await viewerClient.PostAsync($"{Results}/{row.Id}/verify", null);
        Assert.Equal(HttpStatusCode.Forbidden, verify.StatusCode);
        Assert.Equal(ProblemCodes.Forbidden, await AuthClient.CodeOf(verify));

        var blind = (await (await SignedInClientAsync(noRights)).GetFromJsonAsync<ResultCaseView>($"{Results}?accession={visit.Accession}"))!;
        Assert.Equal((0, 1), (blind.Units.Count, blind.HiddenUnits));
        var noRightEntry = await (await SignedInClientAsync(noRights)).PutAsJsonAsync($"{Results}/{row.Id}", new EnterResultValueRequest(4m, null, null, null));
        Assert.Equal(HttpStatusCode.Forbidden, noRightEntry.StatusCode);
        Assert.Equal("RES-050", await AuthClient.CodeOf(noRightEntry));

        var noBranch = await (await SignedInClientAsync(branchless)).PutAsJsonAsync($"{Results}/{row.Id}", new EnterResultValueRequest(4m, null, null, null));
        Assert.Equal(HttpStatusCode.Forbidden, noBranch.StatusCode);
        Assert.Equal(ProblemCodes.BranchRequired, await AuthClient.CodeOf(noBranch));

        var foreign = await (await SignedInClientAsync(stranger)).GetAsync($"{Results}?accession={visit.Accession}");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal("RES-408", await AuthClient.CodeOf(foreign));

        var empty = await viewerClient.PutAsJsonAsync($"{Results}/values", new SaveResultValuesRequest([]));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(ProblemCodes.ValidationFailed, await AuthClient.CodeOf(empty));
        var noQuery = await viewerClient.GetAsync(Results);
        Assert.Equal(HttpStatusCode.BadRequest, noQuery.StatusCode);
    }

    [DatabaseFact]
    public async Task A_unit_names_the_results_outside_the_users_rights_and_their_clinical_groups()
    {
        // Change request 2026-10-14: a print-alone service spanning two clinical groups (Urine C/S with a chemistry member)
        // shows a user with rights on one group the unit, the visible rows and a note of what is hidden and why.
        var (lab, micro, cultureTest) = await SeedPrintAloneLabAsync();
        var visit = await RegisterVisitAsync(lab, cultureTest);
        var (chemist, _, _) = await factory.SeedUserAsync(tenantId: lab.Tenant, permissions: ViewerPermissions, branchIds: [lab.Branch]);
        await GrantResultRightsAsync(lab.Tenant, chemist, SpecialRights.None, lab.Group);
        var (both, _, _) = await factory.SeedUserAsync(tenantId: lab.Tenant, permissions: ViewerPermissions, branchIds: [lab.Branch]);
        await GrantResultRightsAsync(lab.Tenant, both, SpecialRights.None, lab.Group, micro);

        var partial = (await (await SignedInClientAsync(chemist)).GetFromJsonAsync<ResultCaseView>($"{Results}?accession={visit.Accession}"))!;
        var unit = Assert.Single(partial.Units);
        Assert.Equal((0, 1, 1), (partial.HiddenUnits, partial.HiddenResults, unit.HiddenResults));
        Assert.Equal(lab.Test, Assert.Single(unit.Results).TestId);
        var hiddenGroup = Assert.Single(unit.HiddenGroups);
        Assert.Equal((micro, "MICRO", "Microbiology"), (hiddenGroup.Id, hiddenGroup.Code, hiddenGroup.NameEn));

        var full = (await (await SignedInClientAsync(both)).GetFromJsonAsync<ResultCaseView>($"{Results}?accession={visit.Accession}"))!;
        var whole = Assert.Single(full.Units);
        Assert.Equal((0, 0, 0, 2), (full.HiddenUnits, full.HiddenResults, whole.HiddenResults, whole.Results.Count));
        Assert.Empty(whole.HiddenGroups);
    }

    private sealed record Lab(Guid Tenant, Guid Branch, Guid Group, Guid SampleType, Guid Test, Guid Service);

    /// <summary>Two clinical groups, a potassium test in the first, a culture-like test in the second, and one print-alone profile holding both.</summary>
    private async Task<(Lab Lab, Guid SecondGroup, Guid SecondTest)> SeedPrintAloneLabAsync()
    {
        var tenant = Guid.CreateVersion7();
        using var scope = factory.Services.CreateScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(tenant);
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var now = factory.Clock.UtcNow;

        var chemistry = ClinicalGroup.Create("CHEM", "C", new LocalizedText("Chemistry", "الكيمياء"), "Clinical chemistry", null, 1, now);
        var micro = ClinicalGroup.Create("MICRO", "M", new LocalizedText("Microbiology", "الميكروبيولوجي"), "Microbiology", null, 2, now);
        var sampleType = SampleType.Create("URN", "Urine", new LocalizedText("Urine", "بول"), [], now);
        context.Set<ClinicalGroup>().AddRange(chemistry, micro);
        context.Set<SampleType>().Add(sampleType);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var potassium = LabTest.Create("K", new LabTestDefinition(new LocalizedText("Potassium", "بوتاسيوم"), "Potassium", null, null, chemistry.Id, LabTestType.Numeric, null, null, null, GenderRestriction.Any, null, 1, false, true, 1, null), now);
        var culture = LabTest.Create("UCS", new LabTestDefinition(new LocalizedText("Urine culture", "مزرعة بول"), "Urine culture", null, null, micro.Id, LabTestType.Numeric, null, null, null, GenderRestriction.Any, null, 0, false, true, 1, null), now);
        context.Set<LabTest>().AddRange(potassium, culture);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var service = Service.Create(
            "UCS",
            ServiceType.Profile,
            new ServiceDefinition("UCS", null, new LocalizedText("Urine C/S", "مزرعة بول"), null, null, null, micro.Id, sampleType.Id, null, null, [], null, GenderRestriction.Any, 48, 1, true, true, false, null),
            20,
            [
                new ResolvedServiceMember(ServiceMemberKind.Test, culture.Id, 1, false, true, LabTestType.Numeric, null),
                new ResolvedServiceMember(ServiceMemberKind.Test, potassium.Id, 2, false, true, LabTestType.Numeric, null),
            ],
            now);
        context.Set<Service>().Add(service);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        return (new Lab(tenant, Guid.CreateVersion7(), chemistry.Id, sampleType.Id, potassium.Id, service.Id), micro.Id, culture.Id);
    }

    /// <summary>A clinical group, a sample type, a numeric potassium test with its band and a single-test service in a new tenant.</summary>
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

    /// <summary>Replaces the test's band with one Any-sex band of the given limits (a catalogue change after entry).</summary>
    private async Task ReplaceBandAsync(Lab lab, decimal low, decimal high)
    {
        using var scope = factory.Services.CreateScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(lab.Tenant);
        var test = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<LabTest>().SingleAsync(item => item.Id == lab.Test);
        test.ReplaceRangeBands([new RangeBandSpec(GenderRestriction.Any, 0, 150, RangeAgeUnit.Years, null, low, high, null, null, null, null, null)], factory.Clock.UtcNow);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Runs the Samples and Results policies for a registration the way the outbox processor would (Release 1 defaults: the tube is received at once).</summary>
    private async Task<VisitRegistered> RegisterVisitAsync(Lab lab, Guid? secondTest = null)
    {
        var suffix = Guid.CreateVersion7().ToString("N")[^6..].ToUpperInvariant();
        var tube = Guid.CreateVersion7();
        var line = new RegisteredLine(Guid.CreateVersion7(), lab.Service, tube, "Ordered", secondTest is { } extra ? [lab.Test, extra] : [lab.Test]);
        var registered = new VisitRegistered(
            Guid.CreateVersion7(),
            $"ACC-{suffix}",
            lab.Branch,
            Guid.CreateVersion7(),
            null,
            null,
            false,
            Guid.CreateVersion7(),
            [new RegisteredTube(tube, $"SMP-{suffix}", lab.Group, lab.SampleType, null, [lab.Service])],
            [line],
            factory.Clock.UtcNow);

        using var scope = factory.Services.CreateScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(lab.Tenant);
        var handlers = scope.ServiceProvider.GetServices<IDomainEventHandler<VisitRegistered>>().ToList();
        await handlers.OfType<CreateSamplesOnVisitRegisteredPolicy>().Single().HandleAsync(registered, CancellationToken.None);
        await handlers.OfType<CreateResultsOnVisitRegisteredPolicy>().Single().HandleAsync(registered, CancellationToken.None);
        return registered;
    }

    /// <summary>Full result rights on the groups in every branch and the given special rights, on the role the seeding gave the user.</summary>
    private async Task GrantResultRightsAsync(Guid tenant, string username, SpecialRights special, params Guid[] groups)
    {
        using var scope = factory.Services.CreateScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(tenant);
        var role = (await scope.ServiceProvider.GetRequiredService<IRoleRepository>().FindByNameAsync($"role-{username}", CancellationToken.None))!;
        role.SetResultRights(groups.Select(group => ResultRight.Full(group)), factory.Clock.UtcNow);
        role.SetSpecialRights(special.Copy(), factory.Clock.UtcNow);
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
