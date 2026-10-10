using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// The Catalogue B pages over the real API (intake LIS-INT-CTB-001 §16): permissions from roles decide who may call
/// what (D-04), the aggregates and handlers decide the rest, every refusal arrives as its code.
/// </summary>
public sealed class CatalogueBFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Base = "/api/v1/catalogue";

    private static readonly string[] Editor = ["catalogue.view", "catalogue.create", "catalogue.edit", "catalogue.manage"];
    private static readonly string[] Exporter = ["catalogue.view", "catalogue.export"];
    private static readonly string[] Viewer = ["catalogue.view"];

    [DatabaseFact]
    public async Task Reasons_are_created_listed_by_kind_renamed_deactivated_and_reactivated()
    {
        var tenant = Guid.CreateVersion7();
        var editor = await ClientAsync(tenant, Editor);
        var viewer = await ClientAsync(tenant, Viewer);

        var created = await editor.PostAsJsonAsync($"{Base}/reasons", new CreateReasonRequest("Cancellation", "Patient request", "طلب المريض"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<CreatedResource>())!.Id;

        var duplicate = await editor.PostAsJsonAsync($"{Base}/reasons", new CreateReasonRequest("cancellation", "PATIENT REQUEST", "مكرر"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("CAT-301", await AuthClient.CodeOf(duplicate));

        var otherKind = await editor.PostAsJsonAsync($"{Base}/reasons", new CreateReasonRequest("Refund", "Patient request", "طلب المريض"));
        Assert.Equal(HttpStatusCode.Created, otherKind.StatusCode);

        var unknownKind = await editor.PostAsJsonAsync($"{Base}/reasons", new CreateReasonRequest("Weather", "Rain", "مطر"));
        Assert.Equal(HttpStatusCode.BadRequest, unknownKind.StatusCode);
        Assert.Equal(ProblemCodes.ValidationFailed, await AuthClient.CodeOf(unknownKind));

        var cancellations = await viewer.GetFromJsonAsync<Paged<ReasonListItem>>($"{Base}/reasons?kind=Cancellation&isActive=true");
        var item = Assert.Single(cancellations!.Items);
        Assert.Equal((id, "Cancellation", "طلب المريض"), (item.Id, item.Kind, item.NameAr));

        Assert.Equal(HttpStatusCode.NoContent, (await editor.PutAsJsonAsync($"{Base}/reasons/{id}", new UpdateReasonRequest("Patient asked", "طلب المريض"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await editor.PostAsync($"{Base}/reasons/{id}/deactivate", null)).StatusCode);
        Assert.Empty((await viewer.GetFromJsonAsync<Paged<ReasonListItem>>($"{Base}/reasons?kind=Cancellation&isActive=true"))!.Items);
        Assert.Equal(HttpStatusCode.NoContent, (await editor.PostAsync($"{Base}/reasons/{id}/reactivate", null)).StatusCode);
        var detail = await viewer.GetFromJsonAsync<ReasonListItem>($"{Base}/reasons/{id}");
        Assert.Equal(("Patient asked", true), (detail!.NameEn, detail.IsActive));

        var viewerCreates = await viewer.PostAsJsonAsync($"{Base}/reasons", new CreateReasonRequest("Refund", "Other", "أخرى"));
        Assert.Equal(HttpStatusCode.Forbidden, viewerCreates.StatusCode);
        Assert.Equal(ProblemCodes.Forbidden, await AuthClient.CodeOf(viewerCreates));

        var missing = await editor.PostAsync($"{Base}/reasons/{Guid.CreateVersion7()}/deactivate", null);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("CAT-387", await AuthClient.CodeOf(missing));
    }

    [DatabaseFact]
    public async Task Preparations_enforce_option_lists_codes_and_linked_services()
    {
        var tenant = Guid.CreateVersion7();
        var editor = await ClientAsync(tenant, Editor);

        var oneOption = await editor.PostAsJsonAsync($"{Base}/preparations", new CreatePreparationRequest("FAST", "Fasting hours?", "عدد ساعات الصيام؟", "OptionList", ["8 h"], "Any", null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, oneOption.StatusCode);
        Assert.Equal("CAT-210", await AuthClient.CodeOf(oneOption));

        var unknownService = await editor.PostAsJsonAsync($"{Base}/preparations", new CreatePreparationRequest("FAST", "Fasting hours?", "عدد ساعات الصيام؟", "OptionList", ["8 h", "12 h"], "Any", [Guid.CreateVersion7()]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknownService.StatusCode);
        Assert.Equal("CAT-203", await AuthClient.CodeOf(unknownService));

        var created = await editor.PostAsJsonAsync($"{Base}/preparations", new CreatePreparationRequest(" fast ", "Fasting hours?", "عدد ساعات الصيام؟", "OptionList", ["8 h", "12 h"], "Any", null));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<CreatedResource>())!.Id;

        var again = await editor.PostAsJsonAsync($"{Base}/preparations", new CreatePreparationRequest("FAST", "Again", "مرة أخرى", "Text", null, "Any", null));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("CAT-300", await AuthClient.CodeOf(again));

        Assert.Equal(HttpStatusCode.NoContent, (await editor.PutAsJsonAsync($"{Base}/preparations/{id}", new UpdatePreparationRequest("Pregnant?", "هل أنت حامل؟", "YesNo", ["ignored", "too"], "Female", null))).StatusCode);
        var detail = await editor.GetFromJsonAsync<PreparationListItem>($"{Base}/preparations/{id}");
        Assert.Equal(("FAST", "YesNo", "Female", 0, 0), (detail!.Code, detail.AnswerType, detail.Gender, detail.Options.Count, detail.Services.Count));

        var byType = await editor.GetFromJsonAsync<Paged<PreparationListItem>>($"{Base}/preparations?answerType=YesNo&search=preg");
        Assert.Single(byType!.Items);
    }

    [DatabaseFact]
    public async Task Doctors_refuse_a_bad_share_and_unknown_branches_and_filter_by_branch()
    {
        var tenant = Guid.CreateVersion7();
        var editor = await ClientAsync(tenant, Editor);

        var share = await editor.PostAsJsonAsync($"{Base}/doctors", new CreateDoctorRequest("D1", "Dr. Amal", "د. أمل", null, null, null, 100.5m, null));
        Assert.Equal(HttpStatusCode.BadRequest, share.StatusCode);

        var precision = await editor.PostAsJsonAsync($"{Base}/doctors", new CreateDoctorRequest("D1", "Dr. Amal", "د. أمل", null, null, null, 12.345m, null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, precision.StatusCode);
        Assert.Equal("CAT-220", await AuthClient.CodeOf(precision));

        var branch = await editor.PostAsJsonAsync($"{Base}/doctors", new CreateDoctorRequest("D1", "Dr. Amal", "د. أمل", null, null, [Guid.CreateVersion7()], 10m, null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, branch.StatusCode);
        Assert.Equal("CAT-205", await AuthClient.CodeOf(branch));

        var phone = await editor.PostAsJsonAsync($"{Base}/doctors", new CreateDoctorRequest("D1", "Dr. Amal", "د. أمل", null, null, null, 10m, "12-34"));
        Assert.Equal("CAT-202", await AuthClient.CodeOf(phone));

        var created = await editor.PostAsJsonAsync($"{Base}/doctors", new CreateDoctorRequest("d1", "Dr. Amal", "د. أمل", "Cardiology", "Consultant", null, 12.5m, "01001234567"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var everywhere = await editor.GetFromJsonAsync<Paged<DoctorListItem>>($"{Base}/doctors?branchId={Guid.CreateVersion7()}");
        var doctor = Assert.Single(everywhere!.Items);
        Assert.Equal(("D1", 12.5m), (doctor.Code, doctor.SharePercent));
    }

    [DatabaseFact]
    public async Task Microbiology_masters_chain_drug_types_commercial_names_and_antibiotics()
    {
        var tenant = Guid.CreateVersion7();
        var editor = await ClientAsync(tenant, Editor);

        var drugType = await IdOf(await editor.PostAsJsonAsync($"{Base}/microbiology/drug-types", new DrugTypeRequest("Penicillin", "بنسلين")));
        var commercialName = await IdOf(await editor.PostAsJsonAsync($"{Base}/microbiology/commercial-names", new CommercialNameRequest(drugType, "Augmentin", "أوجمنتين")));
        var antibiotic = await editor.PostAsJsonAsync($"{Base}/microbiology/antibiotics", new CreateAntibioticRequest("amc", "Amoxicillin-clavulanate", "أموكسيسيلين", "amc", commercialName));
        Assert.Equal(HttpStatusCode.Created, antibiotic.StatusCode);

        var list = await editor.GetFromJsonAsync<Paged<AntibioticListItem>>($"{Base}/microbiology/antibiotics?search=amox");
        var item = Assert.Single(list!.Items);
        Assert.Equal(("AMC", "AMC", "Augmentin"), (item.Code, item.Abbreviation, item.CommercialNameEn));

        var macrolide = await IdOf(await editor.PostAsJsonAsync($"{Base}/microbiology/drug-types", new DrugTypeRequest("Macrolide", "ماكروليد")));
        Assert.Equal(HttpStatusCode.NoContent, (await editor.PostAsync($"{Base}/microbiology/drug-types/{macrolide}/deactivate", null)).StatusCode);
        var inactiveLink = await editor.PostAsJsonAsync($"{Base}/microbiology/commercial-names", new CommercialNameRequest(macrolide, "Zithromax", "زيثروماكس"));
        Assert.Equal(HttpStatusCode.Conflict, inactiveLink.StatusCode);
        Assert.Equal("CAT-310", await AuthClient.CodeOf(inactiveLink));

        var unknownDrugType = await editor.PostAsJsonAsync($"{Base}/microbiology/commercial-names", new CommercialNameRequest(Guid.CreateVersion7(), "Ghost", "شبح"));
        Assert.Equal("CAT-260", await AuthClient.CodeOf(unknownDrugType));

        Assert.Equal(HttpStatusCode.Created, (await editor.PostAsJsonAsync($"{Base}/microbiology/organisms", new CreateOrganismRequest("ECOLI", "Escherichia coli", "إشريكية قولونية"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await editor.PostAsJsonAsync($"{Base}/microbiology/sensitivity-values", new CreateSensitivityValueRequest("S", "Susceptible", "حساس", 1))).StatusCode);
        var order = await editor.PostAsJsonAsync($"{Base}/microbiology/sensitivity-values", new CreateSensitivityValueRequest("R", "Resistant", "مقاوم", 5000));
        Assert.Equal(HttpStatusCode.BadRequest, order.StatusCode);
    }

    [DatabaseFact]
    public async Task Test_rules_shortcut_sets_and_diagnoses_check_their_services_and_the_rule_check_saves_nothing()
    {
        var tenant = Guid.CreateVersion7();
        var editor = await ClientAsync(tenant, Editor);
        var (a, b) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        var check = await editor.PostAsJsonAsync($"{Base}/test-rules/validate", new ValidateTestRuleRequest(null, [a], [a, b]));
        Assert.Equal(HttpStatusCode.OK, check.StatusCode);
        var result = await check.Content.ReadFromJsonAsync<TestRuleValidationResult>();
        Assert.False(result!.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "CAT-242" && issue.ServiceId == a);
        Assert.Contains(result.Issues, issue => issue.Code == "CAT-203");
        Assert.Empty((await editor.GetFromJsonAsync<Paged<TestRuleListItem>>($"{Base}/test-rules"))!.Items);

        var rule = await editor.PostAsJsonAsync($"{Base}/test-rules", new TestRuleRequest("A adds B", "أ تضيف ب", null, [a], [b]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rule.StatusCode);
        Assert.Equal("CAT-203", await AuthClient.CodeOf(rule));

        var emptySet = await editor.PostAsJsonAsync($"{Base}/shortcut-sets", new CreateShortcutSetRequest("ICU", "ICU", "العناية", []));
        Assert.Equal(HttpStatusCode.BadRequest, emptySet.StatusCode);

        var diagnosis = await editor.PostAsJsonAsync($"{Base}/diagnoses", new CreateDiagnosisRequest("e11.9", "Type 2 diabetes", "السكري النوع الثاني", null));
        Assert.Equal(HttpStatusCode.Created, diagnosis.StatusCode);
        var listed = await editor.GetFromJsonAsync<Paged<DiagnosisListItem>>($"{Base}/diagnoses?search=E11");
        Assert.Equal("E11.9", Assert.Single(listed!.Items).IcdCode);
    }

    [DatabaseFact]
    public async Task Sending_labs_comments_and_report_templates_check_their_references()
    {
        var tenant = Guid.CreateVersion7();
        var editor = await ClientAsync(tenant, Editor);

        var lab = await editor.PostAsJsonAsync($"{Base}/sending-labs", new SendingLabRequest("Delta Lab", "معمل دلتا", Guid.CreateVersion7(), Guid.CreateVersion7(), "Ahmed", "أحمد", null, null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, lab.StatusCode);
        Assert.Equal("CAT-206", await AuthClient.CodeOf(lab));

        var comment = await editor.PostAsJsonAsync($"{Base}/comment-templates", new CreateCommentTemplateRequest("HEMO", "Hemolysed sample.", "عينة متحللة.", Guid.CreateVersion7()));
        Assert.Equal("CAT-204", await AuthClient.CodeOf(comment));
        Assert.Equal(HttpStatusCode.Created, (await editor.PostAsJsonAsync($"{Base}/comment-templates", new CreateCommentTemplateRequest("hemo", "Hemolysed sample.", "عينة متحللة.", null))).StatusCode);

        var halfHeader = await editor.PostAsJsonAsync($"{Base}/report-templates", new ReportTemplateRequest("Results A4", "النتائج", "Results", null, null, true, "Both", "Header", null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, halfHeader.StatusCode);
        var template = await editor.PostAsJsonAsync($"{Base}/report-templates", new ReportTemplateRequest("Results A4", "النتائج", "Results", null, null, true, "Both", "Header", "رأس", null, null));
        Assert.Equal(HttpStatusCode.Created, template.StatusCode);
        var listed = await editor.GetFromJsonAsync<Paged<ReportTemplateListItem>>($"{Base}/report-templates?type=Results");
        Assert.Equal(("Header", "رأس", "Both"), (Assert.Single(listed!.Items).HeaderTextEn, listed.Items[0].HeaderTextAr, listed.Items[0].Language));
    }

    [DatabaseFact]
    public async Task Export_needs_catalogue_export_and_returns_a_safe_csv()
    {
        var tenant = Guid.CreateVersion7();
        var editor = await ClientAsync(tenant, Editor);
        var exporter = await ClientAsync(tenant, Exporter);
        Assert.Equal(HttpStatusCode.Created, (await editor.PostAsJsonAsync($"{Base}/reasons", new CreateReasonRequest("Discount", "=Staff, family", "خصم الموظفين"))).StatusCode);

        var forbidden = await editor.GetAsync($"{Base}/reasons/export");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var file = await exporter.GetAsync($"{Base}/reasons/export?kind=Discount");
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("text/csv", file.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("reasons-", file.Content.Headers.ContentDisposition!.FileName!.Trim('"'), StringComparison.Ordinal);
        var text = Encoding.UTF8.GetString(await file.Content.ReadAsByteArrayAsync()).TrimStart('﻿');
        Assert.Contains("\"'=Staff, family\"", text, StringComparison.Ordinal);
        Assert.Contains("خصم الموظفين", text, StringComparison.Ordinal);
    }

    private async Task<HttpClient> ClientAsync(Guid tenant, IReadOnlyList<string> permissions)
    {
        var (username, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: permissions);
        var client = factory.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(username, ApiFactory.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }

    private static async Task<Guid> IdOf(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
    }
}
