using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Samples.Policies;
using Platform.Contracts.Common;
using Platform.Contracts.Samples;
using Platform.Contracts.Settings;
using Platform.Domain.Catalogue;
using Platform.Domain.Registration;
using Platform.Infrastructure.Persistence;
using Platform.SharedKernel.Localization;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// WF-05 over the real API (intake LIS-INT-SMP-001 §16): samples arrive through the registration policy, move by scan
/// and by accession, are rejected, replaced or reinstated per the tenant policy (BR-18), and every refusal arrives as
/// its code; permissions (D-04) and the session branch (TENANT-004) are enforced before the aggregate runs.
/// </summary>
public sealed class SamplesFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Samples = "/api/v1/samples";
    private const string Settings = "/api/v1/settings";

    private static readonly string[] BenchPermissions = ["samples.view", "samples.create", "samples.edit", "settings.view", "settings.manage"];
    private static readonly string[] ViewerPermissions = ["samples.view"];

    [DatabaseFact]
    public async Task Tubes_are_collected_and_received_by_scan_and_by_accession_and_tracked()
    {
        var tenant = Guid.CreateVersion7();
        var branch = Guid.CreateVersion7();
        var (bench, _, benchId) = await factory.SeedUserAsync(tenantId: tenant, permissions: BenchPermissions, displayName: "Bench Technician", branchIds: [branch]);
        var client = await SignedInClientAsync(bench);
        await SetAsync(client, ("autoCollect", "false"), ("autoReceive", "false"));

        var registered = await RegisterVisitAsync(tenant, branch, benchId, 2);
        var (first, second) = (registered.Tubes[0].SampleNumber, registered.Tubes[1].SampleNumber);

        var list = await client.GetFromJsonAsync<Paged<SampleListItem>>($"{Samples}?accession={registered.Accession}");
        Assert.Equal(2, list!.TotalCount);
        Assert.All(list.Items, item => Assert.Equal("Ordered", item.Status));

        // T2 by scan; a second collect scan is a state conflict; an unknown mode is a shape violation.
        var collected = await client.PostAsJsonAsync($"{Samples}/scan", new ScanSampleRequest(first.ToLowerInvariant(), "Collect"));
        Assert.Equal(HttpStatusCode.OK, collected.StatusCode);
        var detail = (await collected.Content.ReadFromJsonAsync<SampleDetail>())!;
        Assert.Equal(("Collected", "Bench Technician", branch), (detail.Summary.Status, detail.Collected!.UserName, detail.Collected.BranchId!.Value));

        var twice = await client.PostAsJsonAsync($"{Samples}/scan", new ScanSampleRequest(first, "Collect"));
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Equal("SMP-010", await AuthClient.CodeOf(twice));

        var badMode = await client.PostAsJsonAsync($"{Samples}/scan", new ScanSampleRequest(first, "Fly"));
        Assert.Equal(HttpStatusCode.BadRequest, badMode.StatusCode);
        Assert.Equal(ProblemCodes.ValidationFailed, await AuthClient.CodeOf(badMode));

        var unknown = await client.PostAsJsonAsync($"{Samples}/scan", new ScanSampleRequest("SMP-1999-99999", "Receive"));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("SMP-404", await AuthClient.CodeOf(unknown));

        // T3 for the whole accession: the collected tube and the ordered one both reach Received.
        var receiveAll = await client.PostAsync($"{Samples}/visits/{registered.VisitId}/receive-all", null);
        Assert.Equal(HttpStatusCode.OK, receiveAll.StatusCode);
        var result = (await receiveAll.Content.ReadFromJsonAsync<VisitSamplesActionResult>())!;
        Assert.Equal((2, 0), (result.Changed, result.Unchanged));

        var again = await client.PostAsJsonAsync($"{Samples}/scan", new ScanSampleRequest(second, "Receive"));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("SMP-011", await AuthClient.CodeOf(again));

        var noVisit = await client.PostAsync($"{Samples}/visits/{Guid.CreateVersion7()}/collect-all", null);
        Assert.Equal(HttpStatusCode.NotFound, noVisit.StatusCode);
        Assert.Equal("SMP-405", await AuthClient.CodeOf(noVisit));

        // CP-17 and the documents.
        var byNumber = (await client.GetFromJsonAsync<SampleDetail>($"{Samples}/by-number/{first}"))!;
        Assert.True(byNumber.IsResultEntryAllowed);
        var timeline = (await client.GetFromJsonAsync<SampleTimeline>($"{Samples}/{byNumber.Summary.Id}/timeline"))!;
        Assert.Equal(["Ordered", "Collected", "Received"], timeline.Steps.Select(step => step.Action));
        Assert.NotNull(timeline.Durations.OrderedToReceivedSeconds);
        Assert.Null(timeline.Steps[0].SincePreviousSeconds);

        var label = (await client.GetFromJsonAsync<SampleLabelDocument>($"{Samples}/{byNumber.Summary.Id}/documents/label"))!;
        Assert.Equal((first, registered.Accession), (label.SampleNumber, label.Accession));
        Assert.True(label.Copies >= 1);
        var labels = (await client.GetFromJsonAsync<VisitLabelsDocument>($"{Samples}/visits/{registered.VisitId}/documents/labels"))!;
        Assert.Equal(2, labels.Labels.Count);

        var unknownStatus = await client.GetAsync($"{Samples}?status=flying");
        Assert.Equal(HttpStatusCode.BadRequest, unknownStatus.StatusCode);
    }

    [DatabaseFact]
    public async Task A_rejected_tube_is_replaced_under_the_default_policy_and_reinstated_when_the_tenant_allows_it()
    {
        var tenant = Guid.CreateVersion7();
        var branch = Guid.CreateVersion7();
        var (bench, _, benchId) = await factory.SeedUserAsync(tenantId: tenant, permissions: BenchPermissions, branchIds: [branch]);
        var client = await SignedInClientAsync(bench);
        var registered = await RegisterVisitAsync(tenant, branch, benchId, 1);   // Release 1 defaults: Received at registration
        var reasonId = await SeedRejectionReasonAsync(tenant);
        var sample = (await client.GetFromJsonAsync<SampleDetail>($"{Samples}/by-number/{registered.Tubes[0].SampleNumber}"))!;
        Assert.Equal("Received", sample.Summary.Status);
        var id = sample.Summary.Id;

        var noReason = await client.PostAsJsonAsync($"{Samples}/{id}/reject", new RejectSampleRequest(Guid.Empty, null));
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        var rejected = await client.PostAsJsonAsync($"{Samples}/{id}/reject", new RejectSampleRequest(reasonId, "clotted"));
        Assert.Equal(HttpStatusCode.NoContent, rejected.StatusCode);
        var today = (await client.GetFromJsonAsync<List<SampleListItem>>($"{Samples}/rejected-today?utcOffsetMinutes=0"))!;
        Assert.Equal((id, "clotted"), (Assert.Single(today).Id, today[0].RejectionNote));

        var settings = (await client.GetFromJsonAsync<SampleSettingsView>($"{Samples}/settings"))!;
        Assert.True(settings.NewBarcodeOnReject);
        var reinstated = await client.PostAsync($"{Samples}/{id}/unreject", null);
        Assert.Equal(HttpStatusCode.Forbidden, reinstated.StatusCode);
        Assert.Equal("SMP-016", await AuthClient.CodeOf(reinstated));

        var replaced = await client.PostAsJsonAsync($"{Samples}/{id}/replace", new ReplaceSampleRequest("recollected"));
        Assert.Equal(HttpStatusCode.Created, replaced.StatusCode);
        var replacementId = (await replaced.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
        var replacement = (await client.GetFromJsonAsync<SampleDetail>($"{Samples}/{replacementId}"))!;
        Assert.Equal(("Received", 1, id), (replacement.Summary.Status, replacement.Summary.Generation, replacement.Replaces!.SampleId));
        Assert.NotEqual(registered.Tubes[0].SampleNumber, replacement.Summary.SampleNumber);
        Assert.StartsWith("SMP-", replacement.Summary.SampleNumber, StringComparison.Ordinal);
        var old = (await client.GetFromJsonAsync<SampleDetail>($"{Samples}/{id}"))!;
        Assert.Equal(("Replaced", replacementId), (old.Summary.Status, old.ReplacedBy!.SampleId));

        // BR-18 relaxed: the same barcode may come back.
        await SetAsync(client, ("newBarcodeOnReject", "false"));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"{Samples}/{replacementId}/reject", new RejectSampleRequest(reasonId, null))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"{Samples}/{replacementId}/unreject", new UnrejectSampleRequest("same barcode"))).StatusCode);
        Assert.Equal("Received", (await client.GetFromJsonAsync<SampleDetail>($"{Samples}/{replacementId}"))!.Summary.Status);

        var replaceAgain = await client.PostAsync($"{Samples}/{replacementId}/replace", null);
        Assert.Equal(HttpStatusCode.Conflict, replaceAgain.StatusCode);
        Assert.Equal("SMP-013", await AuthClient.CodeOf(replaceAgain));
    }

    [DatabaseFact]
    public async Task Permissions_the_session_branch_and_the_tenant_bound_every_call()
    {
        var tenant = Guid.CreateVersion7();
        var branch = Guid.CreateVersion7();
        var (bench, _, benchId) = await factory.SeedUserAsync(tenantId: tenant, permissions: BenchPermissions, branchIds: [branch]);
        var (viewer, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ViewerPermissions, branchIds: [branch]);
        var (branchless, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: BenchPermissions);
        var (outsider, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: null, branchIds: [branch]);
        var (stranger, _, _) = await factory.SeedUserAsync(tenantId: Guid.CreateVersion7(), permissions: BenchPermissions, branchIds: [Guid.CreateVersion7()]);
        var benchClient = await SignedInClientAsync(bench);
        await SetAsync(benchClient, ("autoCollect", "false"), ("autoReceive", "false"));
        var registered = await RegisterVisitAsync(tenant, branch, benchId, 1);
        var number = registered.Tubes[0].SampleNumber;

        var viewerScan = await (await SignedInClientAsync(viewer)).PostAsJsonAsync($"{Samples}/scan", new ScanSampleRequest(number, "Collect"));
        Assert.Equal(HttpStatusCode.Forbidden, viewerScan.StatusCode);
        Assert.Equal(ProblemCodes.Forbidden, await AuthClient.CodeOf(viewerScan));

        var noBranch = await (await SignedInClientAsync(branchless)).PostAsJsonAsync($"{Samples}/scan", new ScanSampleRequest(number, "Collect"));
        Assert.Equal(HttpStatusCode.Forbidden, noBranch.StatusCode);
        Assert.Equal(ProblemCodes.BranchRequired, await AuthClient.CodeOf(noBranch));

        var outsiderList = await (await SignedInClientAsync(outsider)).GetAsync(Samples);
        Assert.Equal(HttpStatusCode.Forbidden, outsiderList.StatusCode);

        var foreign = await (await SignedInClientAsync(stranger)).GetAsync($"{Samples}/by-number/{number}");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal("SMP-404", await AuthClient.CodeOf(foreign));

        Assert.Equal("Ordered", (await benchClient.GetFromJsonAsync<SampleDetail>($"{Samples}/by-number/{number}"))!.Summary.Status);
    }

    /// <summary>Runs the Samples policy for a registration the way the outbox processor would: in a scope bound to the event's tenant.</summary>
    private async Task<VisitRegistered> RegisterVisitAsync(Guid tenant, Guid branch, Guid registrar, int tubes)
    {
        var suffix = Guid.CreateVersion7().ToString("N")[^6..].ToUpperInvariant();
        var registered = new VisitRegistered(
            Guid.CreateVersion7(),
            $"ACC-{suffix}",
            branch,
            Guid.CreateVersion7(),
            null,
            null,
            false,
            registrar,
            [.. Enumerable.Range(1, tubes).Select(index => new RegisteredTube(Guid.CreateVersion7(), $"SMP-{suffix}-{index}", Guid.CreateVersion7(), Guid.CreateVersion7(), null, [Guid.CreateVersion7()]))],
            [],
            factory.Clock.UtcNow);

        using var scope = factory.Services.CreateScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(tenant);
        var policy = scope.ServiceProvider.GetServices<IDomainEventHandler<VisitRegistered>>().OfType<CreateSamplesOnVisitRegisteredPolicy>().Single();
        await policy.HandleAsync(registered, CancellationToken.None);
        return registered;
    }

    /// <summary>A catalogue reason of kind Rejection when the catalogue table is installed; otherwise any id (the rejection keeps the id, intake §13).</summary>
    private async Task<Guid> SeedRejectionReasonAsync(Guid tenant)
    {
        using var scope = factory.Services.CreateScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(tenant);
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (context.Model.FindEntityType(typeof(Reason)) is null)
        {
            return Guid.CreateVersion7();
        }

        var reason = Reason.Create(ReasonKind.Rejection, new LocalizedText("Hemolysis", "تحلل الدم"), factory.Clock.UtcNow);
        context.Set<Reason>().Add(reason);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return reason.Id;
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
