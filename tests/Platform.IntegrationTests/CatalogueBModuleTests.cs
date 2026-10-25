using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Catalogue;
using Platform.Application.PlatformAdmin;
using Platform.Contracts.Common;
using Platform.Domain.Catalogue;
using Platform.Infrastructure.Persistence;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.IntegrationTests;

/// <summary>
/// The engine side of intake LIS-INT-CTB-001: tenant invisibility of every Catalogue B table, the unique indexes
/// (codes in the model, case-insensitive names in CatalogueB.migration.sql) → UNIQUE-409, the register projections
/// and search, the read ports of Registration/Results/Reporting, and the BR-25 clone.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CatalogueBModuleTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    private static LocalizedText Text(string en) => new(en, "عربي " + en);

    [DatabaseFact]
    public async Task Every_Catalogue_B_list_of_another_tenant_is_invisible()
    {
        var (tenantA, tenantB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var ids = await SaveAsync(tenantA, context =>
        {
            var drugType = DrugType.Create(Text("Penicillin"), Now);
            var commercialName = CommercialName.Create(drugType, Text("Augmentin"), Now);
            var doctor = Doctor.Create("DOC1", Text("Dr. A"), null, null, [Guid.CreateVersion7()], 5m, null, Now);
            CatalogueBRecord[] records =
            [
                Preparation.Create("FAST", Text("Fasting?"), PreparationAnswerType.YesNo, null, PreparationGender.Any, null, Now),
                CommentTemplate.Create("HEMO", Text("Hemolysed"), null, Now),
                doctor,
                Diagnosis.Create("E11.9", Text("Diabetes"), [Guid.CreateVersion7()], Now),
                SendingLab.Create(Text("Delta"), Guid.CreateVersion7(), Guid.CreateVersion7(), Text("Courier"), null, doctor, Now),
                ShortcutSet.Create("ICU", Text("ICU"), [Guid.CreateVersion7()], Now),
                TestRule.Create(Text("A>>B"), null, [Guid.CreateVersion7()], [Guid.CreateVersion7()], Now),
                Reason.Create(ReasonKind.Cancellation, Text("Patient request"), Now),
                ReportTemplate.Create(Text("Results A4"), ReportTemplateType.Results, true, ReportTemplateLanguage.Both, null, null, Now),
                drugType,
                commercialName,
                Antibiotic.Create("AMC", Text("Amoxicillin"), "AMC", commercialName, Now),
                Organism.Create("ECOLI", Text("E. coli"), Now),
                SensitivityValue.Create("S", Text("Susceptible"), 1, Now),
            ];
            foreach (var record in records)
            {
                context.Add((object)record);
            }

            return records.Select(record => record.Id).ToList();
        });

        using var scopeB = fixture.AppScope(tenantB);
        var contextB = scopeB.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await contextB.Set<Preparation>().IgnoreQueryFilters().CountAsync(record => ids.Contains(record.Id)));
        Assert.Equal(0, await contextB.Set<CommentTemplate>().IgnoreQueryFilters().CountAsync(record => ids.Contains(record.Id)));
        Assert.Equal(0, await contextB.Set<Doctor>().IgnoreQueryFilters().CountAsync(record => ids.Contains(record.Id)));
        Assert.Equal(0, await contextB.Set<Diagnosis>().IgnoreQueryFilters().CountAsync(record => ids.Contains(record.Id)));
        Assert.Equal(0, await contextB.Set<SendingLab>().IgnoreQueryFilters().CountAsync(record => ids.Contains(record.Id)));
        Assert.Equal(0, await contextB.Set<ShortcutSet>().IgnoreQueryFilters().CountAsync(record => ids.Contains(record.Id)));
        Assert.Equal(0, await contextB.Set<TestRule>().IgnoreQueryFilters().CountAsync(record => ids.Contains(record.Id)));
        Assert.Equal(0, await contextB.Set<Reason>().IgnoreQueryFilters().CountAsync(record => ids.Contains(record.Id)));
        Assert.Equal(0, await contextB.Set<ReportTemplate>().IgnoreQueryFilters().CountAsync(record => ids.Contains(record.Id)));
        Assert.Equal(0, await contextB.Set<DrugType>().IgnoreQueryFilters().CountAsync(record => ids.Contains(record.Id)));
        Assert.Equal(0, await contextB.Set<CommercialName>().IgnoreQueryFilters().CountAsync(record => ids.Contains(record.Id)));
        Assert.Equal(0, await contextB.Set<Antibiotic>().IgnoreQueryFilters().CountAsync(record => ids.Contains(record.Id)));
        Assert.Equal(0, await contextB.Set<Organism>().IgnoreQueryFilters().CountAsync(record => ids.Contains(record.Id)));
        Assert.Equal(0, await contextB.Set<SensitivityValue>().IgnoreQueryFilters().CountAsync(record => ids.Contains(record.Id)));
        Assert.Null(await scopeB.ServiceProvider.GetRequiredService<IPreparationRepository>().GetForUpdateAsync(ids[0], CancellationToken.None));

        using var scopeA = fixture.AppScope(tenantA);
        var contextA = scopeA.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await contextA.Set<Antibiotic>().CountAsync(record => ids.Contains(record.Id)));
    }

    [DatabaseFact]
    public async Task Duplicate_codes_and_case_insensitive_duplicate_names_are_refused_by_the_engine()
    {
        var tenant = Guid.CreateVersion7();
        await SaveAsync(tenant, context =>
        {
            context.Add(Organism.Create("KLEB", Text("Klebsiella"), Now));
            context.Add(Reason.Create(ReasonKind.Rejection, Text("Hemolysis"), Now));
            return 0;
        });

        var codeRefusal = await Assert.ThrowsAsync<DomainException>(() => SaveAsync(tenant, context =>
        {
            context.Add(Organism.Create("kleb", Text("Klebsiella 2"), Now));
            return 0;
        }));
        Assert.Equal(ProblemCodes.UniqueViolation, codeRefusal.Code);

        var nameRefusal = await Assert.ThrowsAsync<DomainException>(() => SaveAsync(tenant, context =>
        {
            context.Add(Reason.Create(ReasonKind.Rejection, Text("HEMOLYSIS"), Now));
            return 0;
        }));
        Assert.Equal(ProblemCodes.UniqueViolation, nameRefusal.Code);

        // the same name under another kind is a different reason
        await SaveAsync(tenant, context =>
        {
            context.Add(Reason.Create(ReasonKind.Cancellation, Text("Hemolysis"), Now));
            return 0;
        });
    }

    [DatabaseFact]
    public async Task Registers_search_case_insensitively_filter_and_page()
    {
        var tenant = Guid.CreateVersion7();
        var branch = Guid.CreateVersion7();
        await SaveAsync(tenant, context =>
        {
            context.Add(Doctor.Create("D100", Text("Dr. Hala 100% sure"), "Cardiology", null, [branch], 10m, "01001234567", Now));
            context.Add(Doctor.Create("D200", Text("Dr. Omar"), "Pathology", null, [Guid.CreateVersion7()], 0m, null, Now));
            context.Add(Doctor.Create("D300", Text("Dr. Mona"), "cardiology", null, null, 0m, null, Now));
            return 0;
        });

        using var scope = fixture.AppScope(tenant);
        var doctors = scope.ServiceProvider.GetRequiredService<IDoctorRepository>();

        var cardiology = await doctors.ListAsync(new CatalogueBFilter("CARDIO", null), null, PageRequest.Normalized(1, 50), CancellationToken.None);
        Assert.Equal(["D100", "D300"], cardiology.Items.Select(doctor => doctor.Code).ToArray());

        var literalPercent = await doctors.ListAsync(new CatalogueBFilter("100%", null), null, PageRequest.Normalized(1, 50), CancellationToken.None);
        Assert.Equal("D100", Assert.Single(literalPercent.Items).Code);

        var inBranch = await doctors.ListAsync(new CatalogueBFilter(null, true), branch, PageRequest.Normalized(1, 50), CancellationToken.None);
        Assert.Equal(["D100", "D300"], inBranch.Items.Select(doctor => doctor.Code).ToArray());

        var paged = await doctors.ListAsync(new CatalogueBFilter(null, null), null, PageRequest.Normalized(2, 2), CancellationToken.None);
        Assert.Equal((3L, "D300"), (paged.TotalCount, Assert.Single(paged.Items).Code));
    }

    [DatabaseFact]
    public async Task The_order_rules_port_applies_active_records_only()
    {
        var tenant = Guid.CreateVersion7();
        var (a, b, c, d, e) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var reasonId = Guid.Empty;
        await SaveAsync(tenant, context =>
        {
            context.Add(TestRule.Create(Text("A+B>>C"), null, [a, b], [c], Now));
            context.Add(TestRule.Create(Text("C>>D"), null, [c], [d], Now));
            var inactive = TestRule.Create(Text("A>>E"), null, [a], [e], Now);
            inactive.Deactivate(Now);
            context.Add(inactive);

            context.Add(Preparation.Create("ALL", Text("Medication?"), PreparationAnswerType.Text, null, PreparationGender.Any, null, Now));
            context.Add(Preparation.Create("GLU", Text("Fasting?"), PreparationAnswerType.YesNo, null, PreparationGender.Any, [e], Now));
            context.Add(Preparation.Create("PREG", Text("Pregnant?"), PreparationAnswerType.YesNo, null, PreparationGender.Female, null, Now));

            context.Add(ShortcutSet.Create("ICU", Text("ICU"), [b, a], Now));
            var reason = Reason.Create(ReasonKind.Refund, Text("Overpaid"), Now);
            reasonId = reason.Id;
            context.Add(reason);
            return 0;
        });

        using var scope = fixture.AppScope(tenant);
        var lookup = scope.ServiceProvider.GetRequiredService<IOrderRulesLookup>();

        var added = await lookup.ServicesAddedByRulesAsync([a, b], CancellationToken.None);
        Assert.Equal([c, d], added.Select(addition => addition.ServiceId).ToArray());
        Assert.Equal("A+B>>C", added[0].RuleNameEn);

        var questions = await lookup.PreparationsForAsync([a], PreparationGender.Male, CancellationToken.None);
        Assert.Equal(["ALL"], questions.Select(question => question.Code).ToArray());

        var set = await lookup.ShortcutSetByCodeAsync(" icu ", CancellationToken.None);
        Assert.Equal([b, a], set!.ServiceIds);

        Assert.Equal("Overpaid", Assert.Single(await lookup.ReasonsAsync(ReasonKind.Refund, CancellationToken.None)).NameEn);
        Assert.NotNull(await lookup.ReasonAsync(reasonId, ReasonKind.Refund, CancellationToken.None));
        Assert.Null(await lookup.ReasonAsync(reasonId, ReasonKind.Discount, CancellationToken.None));
    }

    [DatabaseFact]
    public async Task The_doctor_and_report_template_ports_answer_for_the_tenant()
    {
        var tenant = Guid.CreateVersion7();
        var branch = Guid.CreateVersion7();
        var (doctorId, templateId) = (Guid.Empty, Guid.Empty);
        await SaveAsync(tenant, context =>
        {
            var doctor = Doctor.Create("D1", Text("Dr. A"), null, null, [branch], 15m, null, Now);
            var template = ReportTemplate.Create(Text("Culture A4"), ReportTemplateType.Culture, false, ReportTemplateLanguage.En, null, Text("Footer"), Now, ReportDesign.Modern);
            (doctorId, templateId) = (doctor.Id, template.Id);
            context.Add(doctor);
            context.Add(template);
            return 0;
        });

        using var scope = fixture.AppScope(tenant);
        var doctors = scope.ServiceProvider.GetRequiredService<IDoctorLookup>();
        Assert.Equal(15m, (await doctors.GetAsync(doctorId, CancellationToken.None))!.SharePercent);
        Assert.True(await doctors.IsSelectableAsync(doctorId, branch, CancellationToken.None));
        Assert.False(await doctors.IsSelectableAsync(doctorId, Guid.CreateVersion7(), CancellationToken.None));

        var templates = scope.ServiceProvider.GetRequiredService<IReportTemplateLookup>();
        Assert.Equal("Footer", (await templates.GetAsync(templateId, CancellationToken.None))!.FooterTextEn);
        Assert.Equal(ReportDesign.Modern, (await templates.GetAsync(templateId, CancellationToken.None))!.Design);   // LIS-CR-010: app.report_templates.design
        Assert.Single(await templates.ActiveOfTypeAsync(ReportTemplateType.Culture, CancellationToken.None));
        Assert.Empty(await templates.ActiveOfTypeAsync(ReportTemplateType.Receipt, CancellationToken.None));
    }

    [DatabaseFact]
    public async Task The_clone_copies_active_templates_with_fresh_ids_and_skips_laboratory_lists()
    {
        var (platform, laboratory) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var sourceIds = await SaveAsync(platform, context =>
        {
            var drugType = DrugType.Create(Text("Penicillin"), Now);
            var commercialName = CommercialName.Create(drugType, Text("Augmentin"), Now);
            var antibiotic = Antibiotic.Create("AMC", Text("Amoxicillin"), "AMC", commercialName, Now);
            var retired = Reason.Create(ReasonKind.Discount, Text("Retired"), Now);
            retired.Deactivate(Now);
            context.AddRange(drugType, commercialName, antibiotic, retired);
            context.Add(Reason.Create(ReasonKind.Discount, Text("Staff"), Now));
            var receipt = ReportTemplate.Create(Text("Receipt"), ReportTemplateType.Receipt, true, ReportTemplateLanguage.Both, null, null, Now);
            receipt.AttachImage(ReportTemplateImage.Logo, Guid.CreateVersion7(), Now);   // a file id whose content is not in the store
            context.Add(receipt);
            context.Add(Preparation.Create("MED", Text("Medication?"), PreparationAnswerType.Text, null, PreparationGender.Any, null, Now));
            context.Add(Doctor.Create("D1", Text("Dr. Platform"), null, null, null, 0m, null, Now));
            return new[] { drugType.Id, commercialName.Id, antibiotic.Id };
        });

        CatalogueBCloneSummary summary;
        using (var scope = fixture.AppScope(laboratory))   // images are copied into the current (target) tenant's store
        {
            using (scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().Elevate())
            {
                summary = await scope.ServiceProvider.GetRequiredService<ICatalogueBCloner>().CloneAsync(platform, laboratory, CancellationToken.None);
            }
        }

        Assert.Equal((1, 1, 1, 1, 1, 1), (summary.RowsCopied["drugTypes"], summary.RowsCopied["commercialNames"], summary.RowsCopied["antibiotics"], summary.RowsCopied["reasons"], summary.RowsCopied["reportTemplates"], summary.RowsCopied["preparations"]));

        using var target = fixture.AppScope(laboratory);
        var context = target.ServiceProvider.GetRequiredService<AppDbContext>();
        var antibiotic = await context.Set<Antibiotic>().SingleAsync();
        var commercialName = await context.Set<CommercialName>().SingleAsync();
        var drugType = await context.Set<DrugType>().SingleAsync();
        Assert.DoesNotContain(antibiotic.Id, sourceIds);
        Assert.Equal((commercialName.Id, drugType.Id), (antibiotic.CommercialNameId!.Value, commercialName.DrugTypeId));
        Assert.Equal("Staff", (await context.Set<Reason>().SingleAsync()).Name.En);
        Assert.Null((await context.Set<ReportTemplate>().SingleAsync()).LogoFileId);   // the missing content is dropped, never shared
        Assert.Equal(0, await context.Set<Doctor>().CountAsync());

        // a repeated clone adds nothing that already exists
        using var again = fixture.AppScope(null);
        using (again.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().Elevate())
        {
            var second = await again.ServiceProvider.GetRequiredService<ICatalogueBCloner>().CloneAsync(platform, laboratory, CancellationToken.None);
            Assert.All(second.RowsCopied.Values, count => Assert.Equal(0, count));
        }
    }

    [DatabaseFact]
    public async Task Provisioning_links_each_copied_clinical_group_to_the_laboratorys_own_copy_of_its_template()
    {
        // validation Z-02: Catalogue A copies the groups before the templates exist in the laboratory; the link must survive
        var (platform, laboratory) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var sourceTemplate = await SaveAsync(platform, context =>
        {
            var template = ReportTemplate.Create(Text("Chemistry A4"), ReportTemplateType.Results, false, ReportTemplateLanguage.Both, null, null, Now);
            context.Add(template);
            context.Add(ReportTemplate.Create(Text("Unused A4"), ReportTemplateType.Results, false, ReportTemplateLanguage.En, null, null, Now));
            context.Add(ClinicalGroup.Create("CHEM", "C", Text("Chemistry"), null, template.Id, 1, Now));
            context.Add(ClinicalGroup.Create("HEMA", "H", Text("Haematology"), null, null, 2, Now));
            return template.Id;
        });

        using (var scope = fixture.AppScope(TenantIds.Platform))
        {
            var masterData = scope.ServiceProvider.GetRequiredService<IMasterDataCloner>();
            await scope.ServiceProvider.GetRequiredService<ITenantProvisioningScope>().RunAsync(laboratory, async ct =>
            {
                await masterData.StageAsync(platform, laboratory, ct);   // the order of the provisioning: A, the handler's flush, B
                await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
                await masterData.ApplyAsync(platform, laboratory, ct);
            }, CancellationToken.None);
        }

        using var reader = fixture.AppScope(laboratory);
        var context = reader.ServiceProvider.GetRequiredService<AppDbContext>();
        var copy = (await context.Set<ReportTemplate>().ToListAsync()).Single(template => template.Name.En == "Chemistry A4");
        Assert.NotEqual(sourceTemplate, copy.Id);
        var links = await context.Set<ClinicalGroup>().ToDictionaryAsync(group => group.Code, group => group.ReportTemplateId);
        Assert.Equal(copy.Id, links["CHEM"]);   // the laboratory's own copy, never the platform's template
        Assert.Null(links["HEMA"]);
        var port = reader.ServiceProvider.GetRequiredService<IReportTemplateLookup>();
        Assert.Equal("Chemistry A4", (await port.GetAsync(links["CHEM"]!.Value, CancellationToken.None))!.NameEn);
    }

    [DatabaseFact]
    public async Task The_clone_refuses_to_run_outside_an_elevated_scope()
    {
        using var scope = fixture.AppScope(Guid.CreateVersion7());
        var cloner = scope.ServiceProvider.GetRequiredService<ICatalogueBCloner>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => cloner.CloneAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), CancellationToken.None));
    }

    private async Task<T> SaveAsync<T>(Guid tenantId, Func<AppDbContext, T> stage)
    {
        using var scope = fixture.AppScope(tenantId);
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var result = stage(context);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return result;
    }
}
