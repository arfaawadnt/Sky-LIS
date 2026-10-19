using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Patients;
using Platform.Contracts.Common;
using Platform.Domain.Patients;
using Platform.Infrastructure.Persistence;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Primitives;

namespace Platform.IntegrationTests;

/// <summary>
/// The engine side of intake LIS-INT-PAT-001: tenant invisibility of patients and documents (S-TENANT), the unique
/// number (INV-PAT-001), the CHECK constraints of the merge, the register search in both scripts, the similar
/// pre-filter (S-SIM), the merge family of the documents register and the <see cref="IPatientLookup"/> read port.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PatientsModuleTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);
    private static readonly byte[] PdfHeader = "%PDF-1.7"u8.ToArray();

    [DatabaseFact]
    public async Task Patients_and_documents_of_another_tenant_are_invisible()
    {
        var (tenantA, tenantB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var patientId = await RegisterAsync(tenantA, "Isolated Person", "معزول", "01000000001");
        var documentId = await AttachAsync(tenantA, patientId);

        using var scopeB = fixture.AppScope(tenantB, Guid.CreateVersion7());
        var context = scopeB.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await context.Set<Patient>().IgnoreQueryFilters().CountAsync(p => p.Id == patientId));             // RLS, not only the filter
        Assert.Equal(0, await context.Set<PatientDocument>().IgnoreQueryFilters().CountAsync(d => d.Id == documentId));
        Assert.Null(await scopeB.ServiceProvider.GetRequiredService<IPatientRepository>().GetRowAsync(patientId, CancellationToken.None));
        Assert.Null(await scopeB.ServiceProvider.GetRequiredService<IPatientLookup>().GetAsync(patientId, CancellationToken.None));
        Assert.Null(await scopeB.ServiceProvider.GetRequiredService<IPatientDocumentRepository>().GetItemAsync(documentId, CancellationToken.None));
    }

    [DatabaseFact]
    public async Task The_number_is_unique_per_tenant_and_comes_from_the_patient_counter()
    {
        var tenant = Guid.CreateVersion7();
        using (var scope = fixture.AppScope(tenant, Guid.CreateVersion7()))
        {
            var number = await scope.ServiceProvider.GetRequiredService<IReferenceNumbers>().NextAsync(Patient.NumberKind, Patient.NumberPrefix, Now, CancellationToken.None);
            Assert.Matches("^PAT-2026-\\d{5}$", number);
        }

        await RegisterAsync(tenant, "First", null, null, number: "PAT-2026-90001");
        var refusal = await Assert.ThrowsAsync<DomainException>(() => RegisterAsync(tenant, "Second", null, null, number: "PAT-2026-90001"));
        Assert.Equal(ProblemCodes.UniqueViolation, refusal.Code);

        await RegisterAsync(Guid.CreateVersion7(), "Other lab", null, null, number: "PAT-2026-90001");   // another tenant may reuse it
    }

    [DatabaseFact]
    public async Task The_register_searches_numbers_mobiles_national_ids_and_names_in_both_scripts()
    {
        var tenant = Guid.CreateVersion7();
        var mohamed = await RegisterAsync(tenant, "Mohamed Ahmed Ali", "محمد أحمد علي", "01001234567", nationalId: "29001010101234");
        var mona = await RegisterAsync(tenant, "Mona Hassan", "منى حسن", "01112223333", gender: PatientGender.Female);

        using var scope = fixture.AppScope(tenant, Guid.CreateVersion7());
        var repository = scope.ServiceProvider.GetRequiredService<IPatientRepository>();

        async Task<IReadOnlyList<Guid>> Search(string text, PatientGender? gender = null) =>
            (await repository.SearchAsync(new PatientSearchFilter(PatientSearchTerms.Parse(text), gender, null, PatientStatus.Active), PageRequest.Normalized(1, 50), CancellationToken.None))
                .Items.Select(row => row.Id).ToList();

        Assert.Equal([mohamed], await Search("moh ali"));
        Assert.Equal([mohamed], await Search("احمد"));
        Assert.Equal([mona], await Search("مني"));
        Assert.Equal([mona], await Search("0111 222"));
        Assert.Equal([mohamed], await Search("29001010101234"));
        Assert.Equal([mona, mohamed], await Search("PAT-"));
        Assert.Equal([mona], await Search("PAT-", PatientGender.Female));
        Assert.Empty(await Search("nobody here"));

        var row = (await repository.GetRowAsync(mohamed, CancellationToken.None))!;
        Assert.Equal(("mohamed ahmed ali", "محمد احمد علي", "01001234567"), (row.NameKey, row.NameArKey, row.Mobile));
        Assert.Equal("Integration tester", row.CreatedByName);
    }

    [DatabaseFact]
    public async Task Without_the_See_VIP_right_a_VIP_file_matches_only_its_full_number_or_national_id()
    {
        // Validation A-03 (2026-10-11): the register and the picker found a VIP file by name and mobile.
        var tenant = Guid.CreateVersion7();
        var vip = await RegisterAsync(tenant, "Mona Hassan", "منى حسن", "01112223333", PatientGender.Female, nationalId: "29505050101234", number: "PAT-2026-90010", vip: true);
        var plain = await RegisterAsync(tenant, "Mona Ali", null, "01112224444", PatientGender.Female, number: "PAT-2026-90011");

        using var scope = fixture.AppScope(tenant, Guid.CreateVersion7());
        var repository = scope.ServiceProvider.GetRequiredService<IPatientRepository>();

        async Task<IReadOnlyList<Guid>> Search(string? text, bool includeVip) =>
            (await repository.SearchAsync(new PatientSearchFilter(PatientSearchTerms.Parse(text), null, null, PatientStatus.Active, includeVip), PageRequest.Normalized(1, 50), CancellationToken.None))
                .Items.Select(row => row.Id).ToList();

        Assert.Equal([plain], await Search("mona", includeVip: false));
        Assert.Empty(await Search("منى حسن", includeVip: false));
        Assert.Equal([plain], await Search("0111 222", includeVip: false));
        Assert.Equal([plain], await Search("PAT-2026-9001", includeVip: false));
        Assert.Empty(await Search("2950505", includeVip: false));
        Assert.Equal([vip], await Search("pat-2026-90010", includeVip: false));
        Assert.Equal([vip], await Search("29505050101234", includeVip: false));
        Assert.Equal([plain, vip], await Search(null, includeVip: false));   // listed (masked by the projection), never searched by identity

        Assert.Equal([plain, vip], await Search("mona", includeVip: true));
        Assert.Equal([vip], await Search("0111 2223", includeVip: true));
    }

    [DatabaseFact]
    public async Task The_similar_prefilter_finds_mobiles_and_two_token_names_of_active_files_only()
    {
        var tenant = Guid.CreateVersion7();
        var exact = await RegisterAsync(tenant, "Mohamed Ahmed Ali", null, "01001234567");
        var byMobile = await RegisterAsync(tenant, "Sara Ali", null, "01009999999");
        var byName = await RegisterAsync(tenant, "Ahmed Mohamed", "أحمد محمد", "01112223333");
        await RegisterAsync(tenant, "Mohamed Hassan", null, "01200000000");
        var merged = await RegisterAsync(tenant, "Mohamed Ahmed", null, "01001234567");
        await MergeAsync(tenant, exact, merged);

        using var scope = fixture.AppScope(tenant, Guid.CreateVersion7());
        var repository = scope.ServiceProvider.GetRequiredService<IPatientRepository>();

        var latin = await repository.FindSimilarCandidatesAsync(["mohamed", "ahmed"], ["01009999999"], 200, CancellationToken.None);
        Assert.Equal(new[] { exact, byMobile, byName }.Order(), latin.Select(row => row.Id).Order());

        var arabic = await repository.FindSimilarCandidatesAsync(["محمد", "احمد"], [], 200, CancellationToken.None);
        Assert.Equal([byName], arabic.Select(row => row.Id));

        Assert.Empty(await repository.FindSimilarCandidatesAsync([], [], 200, CancellationToken.None));
    }

    [DatabaseFact]
    public async Task Merge_constraints_hold_and_documents_and_lookups_follow_the_merge_chain()
    {
        var tenant = Guid.CreateVersion7();
        var survivor = await RegisterAsync(tenant, "Final File", null, null);
        var middle = await RegisterAsync(tenant, "Middle File", null, null);
        var first = await RegisterAsync(tenant, "First File", null, null);
        var survivorDocument = await AttachAsync(tenant, survivor);
        var firstDocument = await AttachAsync(tenant, first);

        await MergeAsync(tenant, middle, first);      // first → middle
        await MergeAsync(tenant, survivor, middle);   // middle → survivor

        using var scope = fixture.AppScope(tenant, Guid.CreateVersion7());
        var patients = scope.ServiceProvider.GetRequiredService<IPatientRepository>();
        Assert.Equal(new[] { survivor, middle, first }.Order(), (await patients.MergedFamilyAsync(survivor, CancellationToken.None)).Order());
        Assert.Equal([middle], (await patients.ListMergedIntoAsync(survivor, CancellationToken.None)).Select(file => file.Id));

        var documents = await scope.ServiceProvider.GetRequiredService<IPatientDocumentRepository>().ListAsync(await patients.MergedFamilyAsync(survivor, CancellationToken.None), true, CancellationToken.None);
        Assert.Equal(new[] { survivorDocument, firstDocument }.Order(), documents.Select(document => document.Id).Order());
        Assert.Contains(documents, document => document.Id == firstDocument && document.PatientId == first);

        var lookup = scope.ServiceProvider.GetRequiredService<IPatientLookup>();
        var summaries = await lookup.GetManyAsync([first, survivor, Guid.CreateVersion7()], CancellationToken.None);
        Assert.Equal(2, summaries.Count);
        Assert.Equal(("Merged", survivor), (summaries[first].Status, summaries[first].SurvivorPatientId));
        Assert.Equal(("Active", (Guid?)null), (summaries[survivor].Status, summaries[survivor].SurvivorPatientId));

        var detail = (await patients.GetRowAsync(middle, CancellationToken.None))!;
        Assert.Equal(PatientStatus.Merged, detail.Status);
        Assert.NotNull(detail.MergedIntoNumber);

        // The CHECK constraint refuses a half-merged row written behind the aggregate's back (INV-PAT-004).
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync("UPDATE app.patients SET status = 'Merged' WHERE id = {0}", survivor));
    }

    [DatabaseFact]
    public async Task Files_and_documents_are_never_deleted_numbers_never_change_and_frozen_rows_stay_frozen()
    {
        var tenant = Guid.CreateVersion7();
        var survivor = await RegisterAsync(tenant, "Kept File", null, null);
        var merged = await RegisterAsync(tenant, "Absorbed File", null, null);
        var documentId = await AttachAsync(tenant, survivor);
        await MergeAsync(tenant, survivor, merged);

        using var scope = fixture.AppScope(tenant, Guid.CreateVersion7());
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database;

        // Patients.migration.sql: the guards hold for the runtime role, whatever the code path (BR-15, INV-PAT-001/006/009).
        await Assert.ThrowsAnyAsync<Exception>(() => database.ExecuteSqlRawAsync("DELETE FROM app.patients WHERE id = {0}", survivor));
        await Assert.ThrowsAnyAsync<Exception>(() => database.ExecuteSqlRawAsync("UPDATE app.patients SET number = 'PAT-2026-99999' WHERE id = {0}", survivor));
        await Assert.ThrowsAnyAsync<Exception>(() => database.ExecuteSqlRawAsync("UPDATE app.patients SET city = 'Giza' WHERE id = {0}", merged));
        await Assert.ThrowsAnyAsync<Exception>(() => database.ExecuteSqlRawAsync("DELETE FROM app.patient_documents WHERE id = {0}", documentId));
        await Assert.ThrowsAnyAsync<Exception>(() => database.ExecuteSqlRawAsync("UPDATE app.patient_documents SET storage_key = 'x' WHERE id = {0}", documentId));

        Assert.Equal(1, await database.ExecuteSqlRawAsync("UPDATE app.patients SET city = 'Giza' WHERE id = {0}", survivor));   // an Active file stays editable
    }

    [DatabaseFact]
    public async Task Document_projections_carry_the_uploader_and_the_deactivation()
    {
        var tenant = Guid.CreateVersion7();
        var patient = await RegisterAsync(tenant, "Document Owner", null, null);
        var documentId = await AttachAsync(tenant, patient, "Uploader Person");

        using (var scope = fixture.AppScope(tenant, Guid.CreateVersion7(), "Files Manager"))
        {
            var document = (await scope.ServiceProvider.GetRequiredService<IPatientDocumentRepository>().GetForUpdateAsync(documentId, CancellationToken.None))!;
            document.Deactivate(Guid.CreateVersion7(), Now, "wrong patient");
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using var reader = fixture.AppScope(tenant, Guid.CreateVersion7());
        var repository = reader.ServiceProvider.GetRequiredService<IPatientDocumentRepository>();
        var item = (await repository.GetItemAsync(documentId, CancellationToken.None))!;
        Assert.Equal(("Uploader Person", false, "Files Manager", "wrong patient"), (item.UploadedByName, item.IsActive, item.DeactivatedByName, item.DeactivationReason));
        Assert.Empty(await repository.ListAsync([patient], includeInactive: false, CancellationToken.None));
        Assert.Single(await repository.ListAsync([patient], includeInactive: true, CancellationToken.None));
        Assert.StartsWith(tenant.ToString("N"), await repository.GetStorageKeyAsync(documentId, CancellationToken.None), StringComparison.Ordinal);
    }

    [DatabaseFact]
    public async Task The_register_figures_count_the_visits_of_each_file_and_their_last_date_within_the_tenant()
    {
        // UAT A-09 (CP-11 "visit count, last visit"): Registration's adapter of the register's read port.
        var tenant = Guid.CreateVersion7();
        var frequent = await RegisterAsync(tenant, "Frequent Visitor", null, null);
        var once = await RegisterAsync(tenant, "Single Visitor", null, null);
        var never = await RegisterAsync(tenant, "Never Visited", null, null);
        await InsertVisitAsync(tenant, frequent, Now.AddDays(-30));
        await InsertVisitAsync(tenant, frequent, Now.AddDays(-2));
        await InsertVisitAsync(tenant, frequent, Now.AddDays(-10));
        await InsertVisitAsync(tenant, once, Now.AddHours(-1));
        await InsertVisitAsync(Guid.CreateVersion7(), frequent, Now);   // another laboratory's row never counts

        using var scope = fixture.AppScope(tenant, Guid.CreateVersion7());
        var figures = await scope.ServiceProvider.GetRequiredService<IPatientVisitStatistics>().ForPatientsAsync([frequent, once, never], CancellationToken.None);

        Assert.Equal(new PatientVisitFigures(3, Now.AddDays(-2)), figures[frequent]);
        Assert.Equal(new PatientVisitFigures(1, Now.AddHours(-1)), figures[once]);
        Assert.False(figures.ContainsKey(never));
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IPatientVisitStatistics>().ForPatientsAsync([], CancellationToken.None));
    }

    /// <summary>A walk-in visit row of Registration: raw SQL with the NOT NULL columns of app.visits (no DB defaults).</summary>
    private async Task InsertVisitAsync(Guid tenant, Guid patientId, DateTimeOffset registeredAtUtc)
    {
        var id = Guid.CreateVersion7();
        using var scope = fixture.AppScope(tenant, Guid.CreateVersion7());
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync(
            """
            INSERT INTO app.visits (id, tenant_id, accession, patient_id, branch_id, location_type, payer_id, contract_tier_id, batch_id, urgent, biohazard,
                                    collected_outside, utc_offset_minutes, status, registered_at_utc, registered_by_user_id, total_list_price, discount_total,
                                    patient_share_total, payer_share_total, discount_value, revision, created_at_utc)
            VALUES ({0}, {1}, {2}, {3}, {4}, 'Walkin', {5}, {6}, NULL, false, false, false, 0, 'Open', {7}, {8}, 0, 0, 0, 0, 0, 1, {7})
            """,
            id, tenant, $"ACC-IT-{id.ToString("N")[^10..].ToUpperInvariant()}", patientId, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), registeredAtUtc, Guid.CreateVersion7());
    }

    private async Task<Guid> RegisterAsync(Guid tenant, string fullName, string? nameAr, string? mobile, PatientGender gender = PatientGender.Male, string? nationalId = null, string? number = null, bool vip = false)
    {
        using var scope = fixture.AppScope(tenant, Guid.CreateVersion7());
        var assigned = number ?? await scope.ServiceProvider.GetRequiredService<IReferenceNumbers>().NextAsync(Patient.NumberKind, Patient.NumberPrefix, Now, CancellationToken.None);
        var patient = Patient.Register(
            assigned,
            new PatientDemographics(PatientTitle.Mr, fullName, nameAr, gender, new DateOnly(1990, 1, 1), null, null, mobile, nationalId, null, null, null, null, null, null),
            PatientFieldRules.Optional,
            vip,
            Now);
        scope.ServiceProvider.GetRequiredService<IPatientRepository>().Add(patient);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return patient.Id;
    }

    private async Task MergeAsync(Guid tenant, Guid survivorId, Guid mergedId)
    {
        using var scope = fixture.AppScope(tenant, Guid.CreateVersion7());
        var repository = scope.ServiceProvider.GetRequiredService<IPatientRepository>();
        var survivor = (await repository.GetForUpdateAsync(survivorId, CancellationToken.None))!;
        var merged = (await repository.GetForUpdateAsync(mergedId, CancellationToken.None))!;
        merged.MergeInto(survivor, Guid.CreateVersion7(), "duplicate file", true, Now);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>A document row with a well-formed key of the tenant; the bytes are not needed for the projections.</summary>
    private async Task<Guid> AttachAsync(Guid tenant, Guid patientId, string uploaderName = "Integration tester")
    {
        using var scope = fixture.AppScope(tenant, Guid.CreateVersion7(), uploaderName);
        var patient = (await scope.ServiceProvider.GetRequiredService<IPatientRepository>().GetForUpdateAsync(patientId, CancellationToken.None))!;
        var key = $"{tenant:N}/patients/{patientId:N}/{Guid.CreateVersion7():N}";
        var document = PatientDocument.Attach(patient, PatientDocumentFiles.Inspect("scan.pdf", 1024, PdfHeader), key, new string('a', 64), "note", Guid.CreateVersion7(), Now);
        scope.ServiceProvider.GetRequiredService<IPatientDocumentRepository>().Add(document);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return document.Id;
    }
}
