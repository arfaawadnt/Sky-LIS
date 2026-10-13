using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Patients;
using Platform.Application.Results;
using Platform.Domain.Catalogue;
using Platform.Domain.Organization;
using Platform.Domain.Patients;
using Platform.Domain.Results;
using Platform.IntegrationTests.Support;

namespace Platform.IntegrationTests;

/// <summary>
/// The read port of the demographics lock (change request 2026-10-13 item 1, intake LIS-INT-PAT-001 §17) over the real
/// <c>app.results</c>: the highest live status per visit and per patient, cancelled results ignored, a visit without
/// results (or an unknown one) judged patient-wide, and the tenant boundary.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PatientsDemographicsLockTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Technician = Guid.CreateVersion7();
    private static readonly ResultActor Bench = new(Technician, "Bench", ResultRightsGrant.All);

    [DatabaseFact]
    public async Task The_highest_live_status_is_answered_per_visit_and_per_patient()
    {
        var tenant = Guid.CreateVersion7();
        var (patient, otherPatient) = (await RegisterAsync(tenant, "Mona Hassan"), await RegisterAsync(tenant, "Ali Saad"));
        var (visitA, visitB, visitC) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        var verified = Order(visitA, patient);
        verified.Enter(ResultValueInput.Number(4m), ResultTestRules.For(LabTestType.Numeric), null, null, Bench, true, Now);
        verified.Verify(Bench, false, Now);
        var complete = Order(visitA, patient);
        complete.Enter(ResultValueInput.Number(5m), ResultTestRules.For(LabTestType.Numeric), null, null, Bench, true, Now);
        var printedButCancelled = Order(visitB, patient);
        printedButCancelled.Enter(ResultValueInput.Number(5m), ResultTestRules.For(LabTestType.Numeric), null, null, Bench, true, Now);
        printedButCancelled.Verify(Bench, false, Now);
        printedButCancelled.MarkPrinted(Bench, false, null, Now);
        Assert.True(printedButCancelled.Cancel(Technician, "wrong order", Now));
        var cancelledOnly = Order(visitC, otherPatient);
        cancelledOnly.Enter(ResultValueInput.Number(5m), ResultTestRules.For(LabTestType.Numeric), null, null, Bench, true, Now);
        Assert.True(cancelledOnly.Cancel(Technician, "wrong order", Now));
        await AddAsync(tenant, verified, complete, Order(visitA, patient), Order(visitB, patient), printedButCancelled, cancelledOnly);

        using var scope = fixture.AppScope(tenant, Technician);
        var lookup = scope.ServiceProvider.GetRequiredService<IPatientResultStatusLookup>();
        Assert.Equal(DemographicsEditLimit.Verified, await lookup.HighestStatusAsync(patient, visitA, CancellationToken.None));
        Assert.Equal(DemographicsEditLimit.Ordered, await lookup.HighestStatusAsync(patient, visitB, CancellationToken.None));   // the printed result of B is cancelled
        Assert.Equal(DemographicsEditLimit.Verified, await lookup.HighestStatusAsync(patient, null, CancellationToken.None));
        Assert.Equal(DemographicsEditLimit.Verified, await lookup.HighestStatusAsync(patient, Guid.CreateVersion7(), CancellationToken.None));   // an unknown visit never weakens the lock
        Assert.Equal(DemographicsEditLimit.Ordered, await lookup.HighestStatusAsync(otherPatient, visitC, CancellationToken.None));   // only a cancelled result
        Assert.Equal(DemographicsEditLimit.Ordered, await lookup.HighestStatusAsync(Guid.CreateVersion7(), null, CancellationToken.None));

        using var otherTenant = fixture.AppScope(Guid.CreateVersion7(), Technician);
        Assert.Equal(DemographicsEditLimit.Ordered, await otherTenant.ServiceProvider.GetRequiredService<IPatientResultStatusLookup>().HighestStatusAsync(patient, visitA, CancellationToken.None));
    }

    private async Task<Guid> RegisterAsync(Guid tenant, string fullName)
    {
        using var scope = fixture.AppScope(tenant, Technician);
        var number = await scope.ServiceProvider.GetRequiredService<IReferenceNumbers>().NextAsync(Patient.NumberKind, Patient.NumberPrefix, Now, CancellationToken.None);
        var patient = Patient.Register(
            number,
            new PatientDemographics(PatientTitle.Mr, fullName, null, PatientGender.Male, new DateOnly(1990, 1, 1), null, null, "01000000001", null, null, null, null, null, null, null),
            PatientFieldRules.Optional,
            false,
            Now);
        scope.ServiceProvider.GetRequiredService<IPatientRepository>().Add(patient);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return patient.Id;
    }

    private static Result Order(Guid visitId, Guid patientId)
    {
        var group = Guid.CreateVersion7();
        return Result.Order(new ResultOrder(
            visitId,
            "ACC-2026-70101",
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
            Now.AddHours(-3),
            Now.AddHours(20),
            null));
    }

    private async Task AddAsync(Guid tenant, params Result[] results)
    {
        using var scope = fixture.AppScope(tenant, Technician);
        var repository = scope.ServiceProvider.GetRequiredService<IResultRepository>();
        foreach (var result in results)
        {
            repository.Add(result);
        }

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }
}
