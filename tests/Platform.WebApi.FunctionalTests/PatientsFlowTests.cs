using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;
using Platform.Contracts.Patients;
using Platform.Contracts.Settings;
using Platform.Application.PlatformAdmin;
using Platform.Domain.Organization;
using Platform.Domain.PlatformAdmin;
using Platform.Infrastructure.Persistence;
using Platform.SharedKernel.Localization;
using Platform.WebApi.FunctionalTests.Support;
using Platform.TestSupport;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// The patient file workflows over the real API (intake LIS-INT-PAT-001 §16, SRS CP-11 / WF-02 steps 1–2): permissions
/// decide who may call what (D-04), the aggregate decides the rest, and every refusal arrives as its code.
/// </summary>
public sealed class PatientsFlowTests : IClassFixture<ApiFactory>
{
    private const string Patients = "/api/v1/patients";
    private static readonly string FilesRoot = Path.Combine(Path.GetTempPath(), "lis-functional-files");
    private static readonly byte[] PdfBytes = "%PDF-1.7\n1 0 obj << >> endobj\n%%EOF"u8.ToArray();
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];

    private static readonly string[] ClerkPermissions = ["patients.view", "patients.create", "patients.edit", "files.view", "files.create"];
    private static readonly string[] AdministratorPermissions =
        ["patients.view", "patients.create", "patients.edit", "patients.manage", "patients.viewConfidential", "patients.export", "files.view", "files.create", "files.manage", "settings.view", "settings.manage"];

    private readonly ApiFactory _factory;

    public PatientsFlowTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Overrides["Lis:Files:Root"] = FilesRoot;   // documents of the tests never land in the development folder
    }

    [DatabaseFact]
    public async Task A_clerk_registers_finds_and_edits_a_file_and_the_outsider_is_refused()
    {
        var tenant = Guid.CreateVersion7();
        var clerk = await ClientAsync(tenant, ClerkPermissions);
        var outsider = await ClientAsync(tenant, null);

        // T1: the number comes from the counter; the mobile is stored normalised.
        var created = await clerk.PostAsJsonAsync(Patients, Register("Mrs", "Mona Hassan", "منى حسن", "Female", mobile: "0100 123 4567"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var patient = (await created.Content.ReadFromJsonAsync<CreatedPatient>())!;
        Assert.Matches("^PAT-\\d{4}-\\d{5}$", patient.Number);

        var detail = (await clerk.GetFromJsonAsync<PatientDetail>($"{Patients}/{patient.Id}"))!;
        Assert.Equal(("Mona Hassan", "01001234567", "Active", patient.Number), (detail.FullName, detail.Mobile, detail.Status, detail.Number));
        Assert.Null(detail.IsVip);   // masked without patients.viewConfidential

        // Search by name (Arabic), number and mobile; the similar query answers with the tenant's warning flag.
        Assert.Equal(patient.Id, Assert.Single((await clerk.GetFromJsonAsync<Paged<PatientListItem>>($"{Patients}?q=%D9%85%D9%86%D9%89"))!.Items).Id);
        var listed = Assert.Single((await clerk.GetFromJsonAsync<Paged<PatientListItem>>($"{Patients}?q={patient.Number}"))!.Items);
        Assert.Equal((patient.Id, 0, (DateTimeOffset?)null), (listed.Id, listed.VisitCount, listed.LastVisitAtUtc));   // CP-11 figures (UAT A-09): no visit yet
        var similar = (await clerk.GetFromJsonAsync<SimilarPatientsResponse>($"{Patients}/similar?name=mona%20hassan&mobile=01001234567"))!;
        Assert.True(similar.WarningEnabled);
        Assert.Equal(("Exact", patient.Id), (Assert.Single(similar.Candidates).Match, similar.Candidates[0].Id));

        // T2 with the row version the clerk saw; a stale version is a concurrency conflict.
        var update = Update("Mrs", "Mona Hassan Ali", "Female", mobile: "01112223333", rowVersion: detail.RowVersion);
        Assert.Equal(HttpStatusCode.NoContent, (await clerk.PutAsJsonAsync($"{Patients}/{patient.Id}", update)).StatusCode);
        var stale = await clerk.PutAsJsonAsync($"{Patients}/{patient.Id}", update);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(ProblemCodes.ConcurrencyConflict, await AuthClient.CodeOf(stale));
        Assert.Equal("01112223333", (await clerk.GetFromJsonAsync<PatientDetail>($"{Patients}/{patient.Id}"))!.Mobile);

        // Refusals: shape (400), domain (422), confidential field (403), no permission (403).
        var badShape = await clerk.PostAsJsonAsync(Patients, Register("Mr", "Ali", null, "Robot"));
        Assert.Equal((HttpStatusCode.BadRequest, ProblemCodes.ValidationFailed), (badShape.StatusCode, await AuthClient.CodeOf(badShape)));

        var noAge = await clerk.PostAsJsonAsync(Patients, Register("Mr", "Ali", null, "Male", withBirthDate: false));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "PAT-007"), (noAge.StatusCode, await AuthClient.CodeOf(noAge)));

        var vip = await clerk.PostAsJsonAsync(Patients, Register("Mr", "Ali", null, "Male", isVip: true));
        Assert.Equal((HttpStatusCode.Forbidden, "PAT-030"), (vip.StatusCode, await AuthClient.CodeOf(vip)));

        var vipFilter = await clerk.GetAsync($"{Patients}?vip=true");
        Assert.Equal((HttpStatusCode.Forbidden, "PAT-031"), (vipFilter.StatusCode, await AuthClient.CodeOf(vipFilter)));

        var forbidden = await outsider.GetAsync(Patients);
        Assert.Equal((HttpStatusCode.Forbidden, ProblemCodes.Forbidden), (forbidden.StatusCode, await AuthClient.CodeOf(forbidden)));
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.GetAsync($"{Patients}/export")).StatusCode);

        var missing = await clerk.GetAsync($"{Patients}/{Guid.CreateVersion7()}");
        Assert.Equal((HttpStatusCode.NotFound, "PAT-404"), (missing.StatusCode, await AuthClient.CodeOf(missing)));
    }

    [DatabaseFact]
    public async Task The_mandatory_settings_shape_the_form_and_the_administrator_sees_and_sets_VIP()
    {
        var tenant = Guid.CreateVersion7();
        // The See-VIP special right: since validation A-03 a VIP file is found by name only with it (the export below searches "karim").
        var admin = await ClientAsync(tenant, AdministratorPermissions, SeeVip);

        var settings = await admin.PutAsJsonAsync("/api/v1/settings", new UpdateTenantSettingsRequest([new SettingValueItem("mandatoryMobile", "true"), new SettingValueItem("similarNameWarning", "false")]));
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        var form = (await admin.GetFromJsonAsync<PatientFormSettings>($"{Patients}/form-settings"))!;
        Assert.Equal((true, false, false), (form.MandatoryMobile, form.MandatoryNationalId, form.SimilarNameWarning));

        var noMobile = await admin.PostAsJsonAsync(Patients, Register("Mr", "Karim Adel", null, "Male", mobile: null));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "PAT-009"), (noMobile.StatusCode, await AuthClient.CodeOf(noMobile)));

        var created = (await (await admin.PostAsJsonAsync(Patients, Register("Mr", "Karim Adel", null, "Male", mobile: "01005556666", isVip: true))).Content.ReadFromJsonAsync<CreatedPatient>())!;
        Assert.True((await admin.GetFromJsonAsync<PatientDetail>($"{Patients}/{created.Id}"))!.IsVip);
        Assert.Equal(created.Id, Assert.Single((await admin.GetFromJsonAsync<Paged<PatientListItem>>($"{Patients}?vip=true"))!.Items).Id);
        Assert.False((await admin.GetFromJsonAsync<SimilarPatientsResponse>($"{Patients}/similar?name=karim%20adel"))!.WarningEnabled);

        var export = await admin.GetAsync($"{Patients}/export?q=karim");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("text/csv", export.Content.Headers.ContentType!.MediaType);
        var csv = await export.Content.ReadAsByteArrayAsync();
        Assert.Equal([0xEF, 0xBB, 0xBF], csv[..3]);
        var text = System.Text.Encoding.UTF8.GetString(csv, 3, csv.Length - 3);
        Assert.StartsWith("Number,Title,Name,", text, StringComparison.Ordinal);
        Assert.Contains(created.Number, text, StringComparison.Ordinal);
        Assert.EndsWith(",yes\r\n", text, StringComparison.Ordinal);
    }

    [DatabaseFact]
    public async Task Without_the_See_VIP_right_the_register_the_picker_and_the_export_mask_a_VIP_file()
    {
        // Validation A-03 (2026-10-11): Search registrations masked the VIP patient, the Patients list showed everything.
        var tenant = Guid.CreateVersion7();
        var admin = await ClientAsync(tenant, AdministratorPermissions, SeeVip);
        var reception = await ClientAsync(tenant, [.. ClerkPermissions, "patients.export"]);
        var request = Register("Mrs", "Hala Nabil", "هالة نبيل", "Female", mobile: "01007778888", isVip: true) with { NationalId = "29505050101234" };
        var vip = await CreateAsync(admin, request);

        var listed = Assert.Single((await reception.GetFromJsonAsync<Paged<PatientListItem>>(Patients))!.Items);
        Assert.Equal((vip.Id, vip.Number, true), (listed.Id, listed.Number, listed.IdentityMasked));
        Assert.True(listed is { FullName: "", NameAr: null, Mobile: null, NationalId: null });

        // Name and mobile never find the file; the full number and the full national id do (masked), so reception can register it.
        Assert.Empty((await reception.GetFromJsonAsync<Paged<PatientListItem>>($"{Patients}?q=hala&status=Active"))!.Items);
        Assert.Empty((await reception.GetFromJsonAsync<Paged<PatientListItem>>($"{Patients}?q=0100777&status=Active"))!.Items);
        Assert.Empty((await reception.GetFromJsonAsync<Paged<PatientListItem>>($"{Patients}?q=29505050"))!.Items);
        Assert.True(Assert.Single((await reception.GetFromJsonAsync<Paged<PatientListItem>>($"{Patients}?q={vip.Number}&status=Active"))!.Items).IdentityMasked);
        Assert.Equal(vip.Id, Assert.Single((await reception.GetFromJsonAsync<Paged<PatientListItem>>($"{Patients}?q=29505050101234"))!.Items).Id);

        // Validation R-06: the similar check still warns about the VIP file (no duplicate), masked, with its match kept.
        var masked = Assert.Single((await reception.GetFromJsonAsync<SimilarPatientsResponse>($"{Patients}/similar?mobile=01007778888"))!.Candidates);
        Assert.Equal((vip.Id, vip.Number, "Mobile", true), (masked.Id, masked.Number, masked.Match, masked.IdentityMasked));
        Assert.True(masked is { FullName: "", NameAr: null, Mobile: null, NationalId: null, Gender: "Female" });
        Assert.Equal("Exact", Assert.Single((await reception.GetFromJsonAsync<SimilarPatientsResponse>($"{Patients}/similar?name=hala%20nabil&mobile=01007778888"))!.Candidates).Match);
        var byName = Assert.Single((await reception.GetFromJsonAsync<SimilarPatientsResponse>($"{Patients}/similar?name={Uri.EscapeDataString("هالة نبيل")}"))!.Candidates);
        Assert.Equal(("Name", true, ""), (byName.Match, byName.IdentityMasked, byName.FullName));

        // Opening the file keeps the real identity (operational need, REG intake Q12).
        Assert.Equal("Hala Nabil", (await reception.GetFromJsonAsync<PatientDetail>($"{Patients}/{vip.Id}"))!.FullName);

        var csv = await reception.GetStringAsync($"{Patients}/export?q={vip.Number}");
        Assert.Contains($"{vip.Number},Mrs,VIP patient,,Female", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("Hala", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("01007778888", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("29505050101234", csv, StringComparison.Ordinal);

        // With the right: everything, and the name search finds the file.
        var full = Assert.Single((await admin.GetFromJsonAsync<Paged<PatientListItem>>($"{Patients}?q=hala"))!.Items);
        Assert.Equal(("Hala Nabil", "01007778888", "29505050101234", false), (full.FullName, full.Mobile, full.NationalId, full.IdentityMasked));
        var similar = Assert.Single((await admin.GetFromJsonAsync<SimilarPatientsResponse>($"{Patients}/similar?mobile=01007778888"))!.Candidates);
        Assert.Equal(("Hala Nabil", "هالة نبيل", "01007778888", "29505050101234", false), (similar.FullName, similar.NameAr, similar.Mobile, similar.NationalId, similar.IdentityMasked));
    }

    [DatabaseFact]
    public async Task Sorted_by_name_a_masked_VIP_file_sits_after_the_named_files_in_either_direction()
    {
        // Validation R-05: the masked rows sat at their real names' place ("V11 Walk-in One", "VIP patient", "V12 Retest One").
        var tenant = Guid.CreateVersion7();
        var admin = await ClientAsync(tenant, AdministratorPermissions, SeeVip);
        var reception = await ClientAsync(tenant, ClerkPermissions);
        var adel = await CreateAsync(admin, Register("Mr", "Adel Fahmy", null, "Male", mobile: "01001110001"));
        var bassem = await CreateAsync(admin, Register("Mr", "Bassem Ali", null, "Male", mobile: "01001110002", isVip: true));
        var camelia = await CreateAsync(admin, Register("Mrs", "Camelia Saad", null, "Female", mobile: "01001110003"));
        var aaron = await CreateAsync(admin, Register("Mr", "Aaron Zaki", null, "Male", mobile: "01001110004", isVip: true));
        var zeinab = await CreateAsync(admin, Register("Mrs", "Zeinab Omar", null, "Female", mobile: "01001110005"));

        async Task<string[]> NumbersAsync(HttpClient client, string direction) =>
            [.. (await client.GetFromJsonAsync<Paged<PatientListItem>>($"{Patients}?sort=name&direction={direction}"))!.Items.Select(item => item.Number)];

        // Masked rows last, by number (Bassem was registered before Aaron), whatever the direction.
        Assert.Equal([adel.Number, camelia.Number, zeinab.Number, bassem.Number, aaron.Number], await NumbersAsync(reception, "asc"));
        Assert.Equal([zeinab.Number, camelia.Number, adel.Number, bassem.Number, aaron.Number], await NumbersAsync(reception, "desc"));

        // With the right every name is real and sorts in place.
        Assert.Equal([aaron.Number, adel.Number, bassem.Number, camelia.Number, zeinab.Number], await NumbersAsync(admin, "asc"));
        Assert.Equal([zeinab.Number, camelia.Number, bassem.Number, adel.Number, aaron.Number], await NumbersAsync(admin, "desc"));

        // Page 2 continues page 1 with the masked rows at the end.
        var second = (await reception.GetFromJsonAsync<Paged<PatientListItem>>($"{Patients}?sort=name&direction=desc&page=2&pageSize=2"))!.Items;
        Assert.Equal([adel.Number, bassem.Number], second.Select(item => item.Number).ToArray());
    }

    [DatabaseFact]
    public async Task A_file_registered_by_age_is_dated_by_the_laboratory_day_not_the_UTC_day()
    {
        // Validation R-11: created at 01:57 in Cairo on 12 Oct (22:57 UTC on 11 Oct) the age date was 2026-10-11.
        var cairoLab = await ProvisionLaboratoryAsync("Africa/Cairo");
        var clerk = await ClientAsync(cairoLab, ClerkPermissions);
        var noRecord = await ClientAsync(Guid.CreateVersion7(), ClerkPermissions);   // no client record: the platform default zone (R2-01)
        var byAge = Register("Mrs", "Samia Fouad", null, "Female", withBirthDate: false) with { Age = new PatientAgeView(52, 0, 0) };

        // Instants before the machine's now only: the session the client signed in with stays valid.
        var real = _factory.Clock.UtcNow;
        try
        {
            _factory.Clock.UtcNow = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
            var restated = await CreateAsync(clerk, byAge with { FullName = "Rasha Fouad", Age = new PatientAgeView(40, 0, 0) });
            var before = (await clerk.GetFromJsonAsync<PatientDetail>($"{Patients}/{restated.Id}"))!;
            Assert.Equal(new DateOnly(2026, 10, 10), before.AgeRecordedOn);

            _factory.Clock.UtcNow = new DateTimeOffset(2026, 10, 11, 22, 57, 0, TimeSpan.Zero);
            var local = await CreateAsync(clerk, byAge);
            var demo = await CreateAsync(noRecord, byAge);

            var detail = (await clerk.GetFromJsonAsync<PatientDetail>($"{Patients}/{local.Id}"))!;
            Assert.Equal((new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 12), new DateOnly(1974, 10, 12)), (detail.AgeRecordedOn!.Value, detail.RegisteredOn, detail.DateOfBirth!.Value));
            // Round 2 R2-01: a laboratory without a client record (the demo laboratory) dates by Africa/Cairo, not by the UTC day.
            Assert.Equal(new DateOnly(2026, 10, 12), (await noRecord.GetFromJsonAsync<PatientDetail>($"{Patients}/{demo.Id}"))!.AgeRecordedOn);

            // T2 restates an age on the laboratory's day as well (the same instant).
            var edit = new UpdatePatientRequest("Mrs", "Rasha Fouad", null, "Female", null, new PatientAgeView(41, 0, 0), null, "01001234567", null, "EG", null, null, "Cairo", null, null, null, before.RowVersion);
            Assert.Equal(HttpStatusCode.NoContent, (await clerk.PutAsJsonAsync($"{Patients}/{restated.Id}", edit)).StatusCode);
            Assert.Equal(new DateOnly(2026, 10, 12), (await clerk.GetFromJsonAsync<PatientDetail>($"{Patients}/{restated.Id}"))!.AgeRecordedOn);
        }
        finally
        {
            _factory.Clock.UtcNow = real;
        }
    }

    [DatabaseFact]
    public async Task The_client_record_country_dates_the_laboratory_and_wins_over_the_time_zone_parameter()
    {
        // Round 2 R2-01, in order: the client record's country; the tenant parameter timeZone; the platform default Africa/Cairo.
        // LIS-CR-005: sign-in is by laboratory address, so every laboratory reachable over the API has a client record; the
        // parameter's fallback (a tenant without one) is no longer reachable here.
        var demoLab = await ProvisionLaboratoryAsync("America/New_York");
        var demoAdmin = await ClientAsync(demoLab, AdministratorPermissions);
        var cairoLab = await ProvisionLaboratoryAsync("Africa/Cairo");
        var cairoAdmin = await ClientAsync(cairoLab, AdministratorPermissions);
        var byAge = Register("Mrs", "Huda Zaki", null, "Female", withBirthDate: false) with { Age = new PatientAgeView(30, 0, 0) };

        // A zone this host does not know is refused with its own code (SET-003, LIS-VR-002 round 4); a known IANA zone is stored.
        var unknown = await demoAdmin.PutAsJsonAsync("/api/v1/settings", new UpdateTenantSettingsRequest([new SettingValueItem("timeZone", "Mars/Olympus")]));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "SET-003"), (unknown.StatusCode, await AuthClient.CodeOf(unknown)));
        foreach (var admin in new[] { demoAdmin, cairoAdmin })
        {
            var saved = await admin.PutAsJsonAsync("/api/v1/settings", new UpdateTenantSettingsRequest([new SettingValueItem("timeZone", "America/New_York")]));
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        }

        var real = _factory.Clock.UtcNow;
        try
        {
            // 22:57 UTC on 11 Oct: 18:57 on 11 Oct in New York, 01:57 on 12 Oct in Cairo.
            _factory.Clock.UtcNow = new DateTimeOffset(2026, 10, 11, 22, 57, 0, TimeSpan.Zero);
            var inDemo = await CreateAsync(demoAdmin, byAge);
            var inCairo = await CreateAsync(cairoAdmin, byAge);

            var demoFile = (await demoAdmin.GetFromJsonAsync<PatientDetail>($"{Patients}/{inDemo.Id}"))!;
            Assert.Equal((new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 11)), (demoFile.AgeRecordedOn!.Value, demoFile.RegisteredOn));
            Assert.Equal(new DateOnly(2026, 10, 12), (await cairoAdmin.GetFromJsonAsync<PatientDetail>($"{Patients}/{inCairo.Id}"))!.AgeRecordedOn);
        }
        finally
        {
            _factory.Clock.UtcNow = real;
        }
    }

    [DatabaseFact]
    public async Task A_patients_reader_without_the_catalogue_right_reads_the_active_conditions_from_the_patients_API()
    {
        // Round 2 R2-04: the Patients page read /catalogue/patient-conditions and got 403 for a user without catalogue.view.
        var tenant = Guid.CreateVersion7();
        var curator = await ClientAsync(tenant, ["catalogue.view", "catalogue.create", "catalogue.manage"]);
        var reader = await ClientAsync(tenant, ["patients.view", "patients.export"]);
        var outsider = await ClientAsync(tenant, null);
        var otherLab = await ClientAsync(Guid.CreateVersion7(), ["patients.view"]);

        var smokerId = await CreateConditionAsync(curator, new PatientConditionRequest("Smoker", "مدخن"));
        var pregnantId = await CreateConditionAsync(curator, new PatientConditionRequest("Pregnant", "حامل"));
        var retiredId = await CreateConditionAsync(curator, new PatientConditionRequest("Retired condition", "حالة موقوفة"));
        Assert.Equal(HttpStatusCode.NoContent, (await curator.PostAsync($"/api/v1/catalogue/patient-conditions/{retiredId}/deactivate", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync("/api/v1/catalogue/patient-conditions?isActive=true")).StatusCode);
        var conditions = (await reader.GetFromJsonAsync<List<PatientConditionOption>>($"{Patients}/conditions"))!;
        Assert.Equal(new[] { new PatientConditionOption(pregnantId, "Pregnant", "حامل"), new PatientConditionOption(smokerId, "Smoker", "مدخن") }, conditions);

        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync($"{Patients}/conditions")).StatusCode);
        Assert.Empty((await otherLab.GetFromJsonAsync<List<PatientConditionOption>>($"{Patients}/conditions"))!);   // tenant-bounded
    }

    private static async Task<Guid> CreateConditionAsync(HttpClient client, PatientConditionRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/v1/catalogue/patient-conditions", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
    }

    [DatabaseFact]
    public async Task An_administrator_merges_a_duplicate_and_the_merged_file_is_frozen()
    {
        var tenant = Guid.CreateVersion7();
        var admin = await ClientAsync(tenant, AdministratorPermissions);
        var clerk = await ClientAsync(tenant, ClerkPermissions);

        var survivor = await CreateAsync(admin, Register("Mr", "Omar Said", null, "Male", mobile: "01001112222"));
        var duplicate = await CreateAsync(admin, Register("Mr", "Omar Saeed", null, "Male", mobile: "01001112222"));
        var otherSex = await CreateAsync(admin, Register("Mrs", "Omar Said", null, "Female"));
        var documentId = await UploadAsync(admin, duplicate.Id, "referral.pdf", PdfBytes);

        // The clerk lacks patients.manage.
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.PostAsJsonAsync($"{Patients}/{survivor.Id}/merge", new MergePatientsRequest(duplicate.Id, "same person", false))).StatusCode);

        var noReason = await admin.PostAsJsonAsync($"{Patients}/{survivor.Id}/merge", new MergePatientsRequest(duplicate.Id, "", false));
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        var self = await admin.PostAsJsonAsync($"{Patients}/{survivor.Id}/merge", new MergePatientsRequest(survivor.Id, "same", false));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "PAT-021"), (self.StatusCode, await AuthClient.CodeOf(self)));

        var gender = await admin.PostAsJsonAsync($"{Patients}/{survivor.Id}/merge", new MergePatientsRequest(otherSex.Id, "same person", false));
        Assert.Equal((HttpStatusCode.Conflict, "PAT-100"), (gender.StatusCode, await AuthClient.CodeOf(gender)));

        // T3: the duplicate is Merged, points to the survivor, and its document shows on the survivor.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"{Patients}/{survivor.Id}/merge", new MergePatientsRequest(duplicate.Id, "registered twice", false))).StatusCode);
        var merged = (await admin.GetFromJsonAsync<PatientDetail>($"{Patients}/{duplicate.Id}"))!;
        Assert.Equal(("Merged", survivor.Id, survivor.Number, "registered twice"), (merged.Status, merged.Merge!.SurvivorId, merged.Merge.SurvivorNumber, merged.Merge.Reason));
        var kept = (await admin.GetFromJsonAsync<PatientDetail>($"{Patients}/{survivor.Id}"))!;
        Assert.Equal((survivor.Number, duplicate.Id), (kept.Number, Assert.Single(kept.MergedFiles).Id));
        var documents = (await admin.GetFromJsonAsync<List<PatientDocumentItem>>($"{Patients}/{survivor.Id}/documents"))!;
        Assert.Equal((documentId, duplicate.Number), (Assert.Single(documents).Id, documents[0].PatientNumber));

        var again = await admin.PostAsJsonAsync($"{Patients}/{otherSex.Id}/merge", new MergePatientsRequest(duplicate.Id, "again", true));
        Assert.Equal((HttpStatusCode.Conflict, "PAT-020"), (again.StatusCode, await AuthClient.CodeOf(again)));
        var edit = await admin.PutAsJsonAsync($"{Patients}/{duplicate.Id}", Update("Mr", "Omar", "Male"));
        Assert.Equal((HttpStatusCode.Conflict, "PAT-020"), (edit.StatusCode, await AuthClient.CodeOf(edit)));
        var upload = await UploadRawAsync(admin, duplicate.Id, "late.pdf", PdfBytes);
        Assert.Equal((HttpStatusCode.Conflict, "PAT-020"), (upload.StatusCode, await AuthClient.CodeOf(upload)));

        Assert.DoesNotContain((await admin.GetFromJsonAsync<SimilarPatientsResponse>($"{Patients}/similar?mobile=01001112222"))!.Candidates, candidate => candidate.Id == duplicate.Id);

        var confirmed = await admin.PostAsJsonAsync($"{Patients}/{survivor.Id}/merge", new MergePatientsRequest(otherSex.Id, "same person, sex corrected", true));
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
    }

    [DatabaseFact]
    public async Task Documents_are_attached_streamed_and_deactivated_never_deleted()
    {
        var tenant = Guid.CreateVersion7();
        var admin = await ClientAsync(tenant, AdministratorPermissions);
        var clerk = await ClientAsync(tenant, ClerkPermissions);
        var patient = await CreateAsync(clerk, Register("Mr", "Hany Fawzy", null, "Male"));

        // T4 by the clerk; the content type is the detected one; the bytes come back intact.
        var documentId = await UploadAsync(clerk, patient.Id, "C:\\scans\\referral.pdf", PdfBytes, "from Dr. Adel");
        var item = Assert.Single((await clerk.GetFromJsonAsync<List<PatientDocumentItem>>($"{Patients}/{patient.Id}/documents"))!);
        Assert.Equal(("referral.pdf", "application/pdf", (long)PdfBytes.Length, "from Dr. Adel", true), (item.FileName, item.ContentType, item.SizeBytes, item.Note, item.IsActive));

        var download = await clerk.GetAsync($"{Patients}/documents/{documentId}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/pdf", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal(PdfBytes, await download.Content.ReadAsByteArrayAsync());

        var exe = await UploadRawAsync(clerk, patient.Id, "setup.exe", "MZ\0\0"u8.ToArray());
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "PAT-042"), (exe.StatusCode, await AuthClient.CodeOf(exe)));
        var renamed = await UploadRawAsync(clerk, patient.Id, "scan.pdf", PngBytes);
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "PAT-043"), (renamed.StatusCode, await AuthClient.CodeOf(renamed)));
        var tooLarge = await UploadRawAsync(clerk, patient.Id, "big.pdf", [.. PdfBytes, .. new byte[10 * 1024 * 1024]]);
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "PAT-041"), (tooLarge.StatusCode, await AuthClient.CodeOf(tooLarge)));

        // T5 needs files.manage and a reason; afterwards the row stays and its content is for managers only.
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.PostAsJsonAsync($"{Patients}/documents/{documentId}/deactivate", new DeactivatePatientDocumentRequest("wrong file"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"{Patients}/documents/{documentId}/deactivate", new DeactivatePatientDocumentRequest(" "))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"{Patients}/documents/{documentId}/deactivate", new DeactivatePatientDocumentRequest("wrong file"))).StatusCode);
        var twice = await admin.PostAsJsonAsync($"{Patients}/documents/{documentId}/deactivate", new DeactivatePatientDocumentRequest("again"));
        Assert.Equal((HttpStatusCode.Conflict, "PAT-046"), (twice.StatusCode, await AuthClient.CodeOf(twice)));

        var inactive = Assert.Single((await clerk.GetFromJsonAsync<List<PatientDocumentItem>>($"{Patients}/{patient.Id}/documents?includeInactive=true"))!);
        Assert.Equal((false, "wrong file"), (inactive.IsActive, inactive.DeactivationReason));
        Assert.Empty((await clerk.GetFromJsonAsync<List<PatientDocumentItem>>($"{Patients}/{patient.Id}/documents?includeInactive=false"))!);
        var hidden = await clerk.GetAsync($"{Patients}/documents/{documentId}");
        Assert.Equal((HttpStatusCode.Forbidden, "PAT-049"), (hidden.StatusCode, await AuthClient.CodeOf(hidden)));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"{Patients}/documents/{documentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await admin.DeleteAsync($"{Patients}/documents/{documentId}")).StatusCode);
    }

    private static RegisterPatientRequest Register(string title, string name, string? nameAr, string gender, string? mobile = "01001234567", bool? isVip = null, bool withBirthDate = true) =>
        new(title, name, nameAr, gender, withBirthDate ? new DateOnly(1990, 5, 1) : null, null, null, mobile, null, "EG", null, null, "Cairo", null, null, isVip);

    private static UpdatePatientRequest Update(string title, string name, string gender, string? mobile = "01001234567", uint? rowVersion = null) =>
        new(title, name, null, gender, new DateOnly(1990, 5, 1), null, null, mobile, null, "EG", null, null, "Cairo", null, null, null, rowVersion);

    private static async Task<CreatedPatient> CreateAsync(HttpClient client, RegisterPatientRequest request)
    {
        var response = await client.PostAsJsonAsync(Patients, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedPatient>())!;
    }

    private static async Task<Guid> UploadAsync(HttpClient client, Guid patientId, string fileName, byte[] bytes, string? note = null)
    {
        var response = await UploadRawAsync(client, patientId, fileName, bytes, note);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
    }

    private static Task<HttpResponseMessage> UploadRawAsync(HttpClient client, Guid patientId, string fileName, byte[] bytes, string? note = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        if (note is not null)
        {
            form.Add(new StringContent(note), "note");
        }

        return client.PostAsync($"{Patients}/{patientId}/documents", form);
    }

    /// <summary>A laboratory with a client record in a country of <paramref name="timeZone"/> (country codes from TestCountryCodes).</summary>
    private async Task<Guid> ProvisionLaboratoryAsync(string timeZone)
    {
        var iso = TestCountryCodes.Next();
        var suffix = Guid.CreateVersion7().ToString("N");
        var country = Country.Create(new CountryData("Zoneland " + iso, "أرض المنطقة", iso, "+20", "Pound", "EGP", "ar", timeZone, 10, null, null));
        var governorate = Governorate.Create(country, "Gov " + suffix[..8], "محافظة", null, null);
        var plan = Plan.Create("P" + suffix[^8..].ToUpperInvariant(), new PlanTerms(new LocalizedText("Plan " + suffix[..6], "باقة"), 100m, "EGP", 1, 5, 100, 1));
        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ICountryRepository>().Add(country);
            scope.ServiceProvider.GetRequiredService<IGovernorateRepository>().Add(governorate);
            scope.ServiceProvider.GetRequiredService<IPlanRepository>().Add(plan);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        var slug = "zone-" + suffix[^10..];
        var profile = ClientProfile.Create("Lab " + slug, "مختبر " + slug, country, governorate.Id, "Street 1", "01012345670", null, "Manager", "01012345671", null);
        var tenant = Tenant.Provision(profile, country, governorate, plan, slug, TenantSource.Manual, "en", 14, _factory.Clock.UtcNow);
        using (var scope = _factory.Services.CreateScope())
        using (scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(tenant.Id))
        {
            scope.ServiceProvider.GetRequiredService<ITenantRepository>().Add(tenant);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        return tenant.Id;
    }


    private static readonly SpecialRights SeeVip = new(null, false, false, false, false, false, false, false, canSeeVip: true);

    private async Task<HttpClient> ClientAsync(Guid tenant, IReadOnlyList<string>? permissions, SpecialRights? rights = null)
    {
        var (username, _, _) = await _factory.SeedUserAsync(tenantId: tenant, permissions: permissions, displayName: permissions is null ? "No Role" : "Patients Tester");
        if (rights is not null)
        {
            // The special rights of the role the factory gave the user (CP-37).
            using var scope = _factory.Services.CreateScope();
            using var tenantScope = scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().ForTenant(tenant);
            var role = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<Role>().SingleAsync(candidate => candidate.Name.En == $"role-{username}");
            role.SetSpecialRights(rights, _factory.Clock.UtcNow);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        var client = _factory.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(username, ApiFactory.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }
}
