using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Organization;
using Platform.Application.Results;
using Platform.Contracts.Patients;
using Platform.Domain.Catalogue;
using Platform.Domain.Organization;
using Platform.Domain.Results;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// The demographics lock over the real API (change request 2026-10-13 item 1; intake LIS-INT-PAT-001 §17, LIS-INT-REG-001 §17):
/// once a result of the visit is Verified, a member whose right stops at Complete may change contact details but not the
/// identity (PAT-032, 403), a member whose right reaches Verified may, and <c>GET /patients/{id}/demographics-lock</c> tells
/// the form in advance what the server will decide.
/// </summary>
public sealed class PatientsDemographicsLockFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Patients = "/api/v1/patients";
    private static readonly string[] ClerkPermissions = ["patients.view", "patients.create", "patients.edit"];

    [DatabaseFact]
    public async Task Identity_changes_follow_the_result_status_and_the_right_while_contact_changes_stay_free()
    {
        var tenant = Guid.CreateVersion7();
        var clerk = await ClientAsync(tenant, DemographicsEditLimit.Complete);
        var supervisor = await ClientAsync(tenant, DemographicsEditLimit.Verified);

        var created = await clerk.PostAsJsonAsync(Patients, new RegisterPatientRequest("Mrs", "Mona Hassan", "منى حسن", "Female", new DateOnly(1990, 5, 1), null, null, "01001234567", null, "EG", null, null, "Cairo", null, null, null));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var patient = (await created.Content.ReadFromJsonAsync<CreatedPatient>())!;
        var visitId = Guid.CreateVersion7();

        // Nothing resulted yet: both may change the identity.
        var open = (await clerk.GetFromJsonAsync<PatientDemographicsLock>($"{Patients}/{patient.Id}/demographics-lock?visitId={visitId}"))!;
        Assert.Equal(new PatientDemographicsLock("Ordered", true, "Complete"), open);

        await SeedVerifiedResultAsync(tenant, visitId, patient.Id);

        // The affordance: the clerk's right stops at Complete, the supervisor's reaches Verified.
        var clerkLock = (await clerk.GetFromJsonAsync<PatientDemographicsLock>($"{Patients}/{patient.Id}/demographics-lock?visitId={visitId}"))!;
        Assert.Equal(new PatientDemographicsLock("Verified", false, "Complete"), clerkLock);
        var supervisorLock = (await supervisor.GetFromJsonAsync<PatientDemographicsLock>($"{Patients}/{patient.Id}/demographics-lock?visitId={visitId}"))!;
        Assert.Equal(new PatientDemographicsLock("Verified", true, "Verified"), supervisorLock);
        var fromFile = (await clerk.GetFromJsonAsync<PatientDemographicsLock>($"{Patients}/{patient.Id}/demographics-lock"))!;
        Assert.Equal("Verified", fromFile.HighestStatus);
        var missing = await clerk.GetAsync($"{Patients}/{Guid.CreateVersion7()}/demographics-lock");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        // The clerk: identity refused with PAT-032, contact details accepted.
        var detail = (await clerk.GetFromJsonAsync<PatientDetail>($"{Patients}/{patient.Id}"))!;
        var renamed = await clerk.PutAsJsonAsync($"{Patients}/{patient.Id}", Update("Mona Hassan Ali", "01001234567", detail.RowVersion, visitId));
        Assert.Equal(HttpStatusCode.Forbidden, renamed.StatusCode);
        Assert.Equal("PAT-032", await AuthClient.CodeOf(renamed));
        var mobileOnly = await clerk.PutAsJsonAsync($"{Patients}/{patient.Id}", Update("Mona Hassan", "01112223333", detail.RowVersion, visitId));
        Assert.Equal(HttpStatusCode.NoContent, mobileOnly.StatusCode);

        // The supervisor: the identity change goes through, and the file reflects both edits.
        detail = (await supervisor.GetFromJsonAsync<PatientDetail>($"{Patients}/{patient.Id}"))!;
        Assert.Equal("01112223333", detail.Mobile);
        var supervisorRename = await supervisor.PutAsJsonAsync($"{Patients}/{patient.Id}", Update("Mona Hassan Ali", "01112223333", detail.RowVersion, visitId));
        Assert.Equal(HttpStatusCode.NoContent, supervisorRename.StatusCode);
        var supervisorMobile = await supervisor.PutAsJsonAsync($"{Patients}/{patient.Id}", Update("Mona Hassan Ali", "01223334444", null, visitId));
        Assert.Equal(HttpStatusCode.NoContent, supervisorMobile.StatusCode);
        detail = (await clerk.GetFromJsonAsync<PatientDetail>($"{Patients}/{patient.Id}"))!;
        Assert.Equal(("Mona Hassan Ali", "01223334444"), (detail.FullName, detail.Mobile));

        // From the patient file (no visit) the same result locks the clerk as well.
        var fromFileRename = await clerk.PutAsJsonAsync($"{Patients}/{patient.Id}", Update("Mona H. Ali", "01223334444", detail.RowVersion, null));
        Assert.Equal((HttpStatusCode.Forbidden, "PAT-032"), (fromFileRename.StatusCode, await AuthClient.CodeOf(fromFileRename)));
    }

    private static UpdatePatientRequest Update(string name, string mobile, uint? rowVersion, Guid? visitId) =>
        new("Mrs", name, "منى حسن", "Female", new DateOnly(1990, 5, 1), null, null, mobile, null, "EG", null, null, "Cairo", null, null, null, rowVersion, visitId);

    /// <summary>One verified numeric result of <paramref name="visitId"/> for the patient, written the way the Results policies would (no API path exists for a bare result).</summary>
    private async Task SeedVerifiedResultAsync(Guid tenant, Guid visitId, Guid patientId)
    {
        var now = factory.Clock.UtcNow;
        var group = Guid.CreateVersion7();
        var result = Result.Order(new ResultOrder(
            visitId,
            "ACC-LOCK-0001",
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            LabTestType.Numeric,
            false,
            null,
            group,
            ReportUnits.ForGroup(visitId, group),
            patientId,
            Guid.CreateVersion7(),
            null,
            false,
            0,
            0,
            now,
            now.AddHours(24),
            null));
        var bench = new ResultActor(Guid.CreateVersion7(), "Bench", ResultRightsGrant.All);
        result.Enter(ResultValueInput.Number(4.2m), ResultTestRules.For(LabTestType.Numeric), null, null, bench, true, now);
        result.Verify(bench, false, now);

        using var scope = factory.Services.CreateScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(tenant);
        scope.ServiceProvider.GetRequiredService<IResultRepository>().Add(result);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>A signed-in clerk of the tenant whose role's special rights stop identity edits at <paramref name="limit"/> (CP-37).</summary>
    private async Task<HttpClient> ClientAsync(Guid tenant, DemographicsEditLimit limit)
    {
        var (username, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ClerkPermissions, displayName: $"Clerk {limit}");
        using (var scope = factory.Services.CreateScope())
        using (scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(tenant))
        {
            var role = (await scope.ServiceProvider.GetRequiredService<IRoleRepository>().FindByNameAsync($"role-{username}", CancellationToken.None))!;
            role.SetSpecialRights(new SpecialRights(null, false, false, false, false, false, false, false, false, limit, false, null), factory.Clock.UtcNow);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        var client = factory.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(username, ApiFactory.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }
}
