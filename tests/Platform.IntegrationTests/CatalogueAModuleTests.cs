using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Catalogue;
using Platform.Contracts.Common;
using Platform.Domain.Catalogue;
using Platform.Infrastructure.Persistence;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.IntegrationTests;

/// <summary>
/// The engine side of intake LIS-INT-CAT-001: tenant invisibility of every catalogue-A table, the unique indexes
/// (UNIQUE-409), children saved with their root in one commit, the registers and the read port, and the cloner (BR-25).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CatalogueAModuleTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task Every_catalogue_A_table_of_another_tenant_is_invisible()
    {
        var (tenantA, tenantB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var seeded = await SeedCatalogueAsync(tenantA);

        using var scopeB = fixture.AppScope(tenantB);
        var context = scopeB.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await context.Set<ClinicalGroup>().IgnoreQueryFilters().CountAsync(row => row.Id == seeded.GroupId));
        Assert.Equal(0, await context.Set<SampleType>().IgnoreQueryFilters().CountAsync(row => row.Id == seeded.SampleTypeId));
        Assert.Equal(0, await context.Set<SampleCondition>().IgnoreQueryFilters().CountAsync(row => row.SampleTypeId == seeded.SampleTypeId));
        Assert.Equal(0, await context.Set<MeasurementUnit>().IgnoreQueryFilters().CountAsync(row => row.Id == seeded.UnitId));
        Assert.Equal(0, await context.Set<PatientCondition>().IgnoreQueryFilters().CountAsync(row => row.Id == seeded.PatientConditionId));
        Assert.Equal(0, await context.Set<LabTest>().IgnoreQueryFilters().CountAsync(row => row.Id == seeded.TestId));
        Assert.Equal(0, await context.Set<RangeBand>().IgnoreQueryFilters().CountAsync(row => row.LabTestId == seeded.TestId));
        Assert.Equal(0, await context.Set<ListValue>().IgnoreQueryFilters().CountAsync(row => row.LabTestId == seeded.ListTestId));
        Assert.Equal(0, await context.Set<Service>().IgnoreQueryFilters().CountAsync(row => row.Id == seeded.ServiceId));
        Assert.Equal(0, await context.Set<ServiceMember>().IgnoreQueryFilters().CountAsync(row => row.ServiceId == seeded.ServiceId));
        Assert.Null(await scopeB.ServiceProvider.GetRequiredService<ICatalogueLookup>().GetServiceAsync(seeded.ServiceId, CancellationToken.None));
    }

    [DatabaseFact]
    public async Task Children_are_saved_with_their_root_and_a_replace_removes_the_old_rows()
    {
        var tenant = Guid.CreateVersion7();
        var seeded = await SeedCatalogueAsync(tenant);

        using (var scope = fixture.AppScope(tenant))
        {
            var test = (await scope.ServiceProvider.GetRequiredService<ILabTestRepository>().GetForUpdateAsync(seeded.TestId, CancellationToken.None))!;
            Assert.Equal(2, test.RangeBands.Count);
            test.ReplaceRangeBands([new RangeBandSpec(GenderRestriction.Any, 0, 150, RangeAgeUnit.Years, null, 60m, 100m, null, null, null, null, null)], Now);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using var reader = fixture.AppScope(tenant);
        var context = reader.ServiceProvider.GetRequiredService<AppDbContext>();
        var band = Assert.Single(await context.Set<RangeBand>().Where(row => row.LabTestId == seeded.TestId).ToListAsync());
        Assert.Equal((tenant, 60m, 100m), (band.TenantId, band.Low!.Value, band.High!.Value));
        Assert.Equal(2, (await context.Set<LabTest>().AsNoTracking().SingleAsync(row => row.Id == seeded.TestId)).Revision);   // seeded bands (1) + this replace (2)        Assert.Equal(2, await context.Set<SampleCondition>().CountAsync(row => row.SampleTypeId == seeded.SampleTypeId && row.TenantId == tenant));
    }

    [DatabaseFact]
    public async Task Unique_indexes_refuse_duplicates_with_UNIQUE_409()
    {
        var tenant = Guid.CreateVersion7();
        await SaveAsync(tenant, scope => scope.ServiceProvider.GetRequiredService<IClinicalGroupRepository>().Add(ClinicalGroup.Create("DUP", "D", new LocalizedText("One", "واحد"), null, null, 0, Now)));
        var group = await Assert.ThrowsAsync<DomainException>(() => SaveAsync(tenant, scope => scope.ServiceProvider.GetRequiredService<IClinicalGroupRepository>().Add(ClinicalGroup.Create("DUP", "D", new LocalizedText("Two", "اثنان"), null, null, 0, Now))));
        Assert.Equal(ProblemCodes.UniqueViolation, group.Code);

        await SaveAsync(tenant, scope => scope.ServiceProvider.GetRequiredService<IMeasurementUnitRepository>().Add(MeasurementUnit.Create("mg/dL", Now)));
        var unit = await Assert.ThrowsAsync<DomainException>(() => SaveAsync(tenant, scope => scope.ServiceProvider.GetRequiredService<IMeasurementUnitRepository>().Add(MeasurementUnit.Create("MG/DL", Now))));
        Assert.Equal(ProblemCodes.UniqueViolation, unit.Code);

        // the same code in another tenant is fine
        await SaveAsync(Guid.CreateVersion7(), scope => scope.ServiceProvider.GetRequiredService<IClinicalGroupRepository>().Add(ClinicalGroup.Create("DUP", "D", new LocalizedText("One", "واحد"), null, null, 0, Now)));
    }

    [DatabaseFact]
    public async Task Two_services_swap_their_sequences_in_one_save()
    {
        var tenant = Guid.CreateVersion7();
        var seeded = await SeedCatalogueAsync(tenant);
        Guid second;
        using (var scope = fixture.AppScope(tenant))
        {
            var services = scope.ServiceProvider.GetRequiredService<IServiceRepository>();
            var service = Service.Create("GLU2", ServiceType.Test, Definition(seeded), await services.NextSequenceAsync(CancellationToken.None), [new ResolvedServiceMember(ServiceMemberKind.Test, seeded.TestId, 1, false, true, LabTestType.Numeric, null)], Now);
            services.Add(service);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
            second = service.Id;
        }

        using (var scope = fixture.AppScope(tenant))
        {
            var services = scope.ServiceProvider.GetRequiredService<IServiceRepository>();
            var loaded = await services.GetManyForUpdateAsync([seeded.ServiceId, second], CancellationToken.None);
            var (first, other) = (loaded.Single(row => row.Id == seeded.ServiceId), loaded.Single(row => row.Id == second));
            var (a, b) = (first.Sequence, other.Sequence);
            first.Resequence(b, Now);
            other.Resequence(a, Now);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using var reader = fixture.AppScope(tenant);
        var sequences = await reader.ServiceProvider.GetRequiredService<IServiceRepository>().SequencesAsync(CancellationToken.None);
        Assert.Equal((2, 1), (sequences[seeded.ServiceId], sequences[second]));
    }

    [DatabaseFact]
    public async Task Registers_join_names_search_aliases_and_the_read_port_resolves_ranges()
    {
        var tenant = Guid.CreateVersion7();
        var seeded = await SeedCatalogueAsync(tenant);

        using var scope = fixture.AppScope(tenant);
        var services = scope.ServiceProvider.GetRequiredService<IServiceRepository>();
        var page = await services.ListAsync(new ServiceListFilter("sugar", null, null, true), PageRequest.Normalized(1, 10), CancellationToken.None);
        var item = Assert.Single(page.Items);
        Assert.Equal(("GLU", "Chemistry", "Serum", 1, "Test"), (item.Code, item.ClinicalGroupNameEn, item.SampleTypeNameEn, item.MemberCount, item.Type));

        var lookup = await services.LookupAsync("12", null, true, PageRequest.Normalized(1, 20), CancellationToken.None);
        Assert.Equal(seeded.ServiceId, Assert.Single(lookup.Items).Id);

        var detail = (await services.GetDetailAsync(seeded.ServiceId, CancellationToken.None))!;
        Assert.Equal(("GLU", "Glucose", "Numeric"), (detail.Members.Single().Code, detail.Members.Single().NameEn, detail.Members.Single().MemberType));

        var port = scope.ServiceProvider.GetRequiredService<ICatalogueLookup>();
        var service = (await port.GetServiceAsync(seeded.ServiceId, CancellationToken.None))!;
        Assert.Equal([seeded.TestId], service.TestIds);
        var test = (await port.GetTestAsync(seeded.TestId, CancellationToken.None))!;
        Assert.Equal(("mg/dL", 2), (test.UnitName, test.RangeBands.Count));
        var female = await port.ResolveRangeAsync(seeded.TestId, GenderRestriction.Female, PatientAge.FromDays(365 * 30), null, CancellationToken.None);
        Assert.Equal(65m, female!.Low);
        Assert.Equal(["GLU"], (await port.GetTestsByCodeAsync(["glu"], CancellationToken.None)).Select(row => row.Code).ToArray());
        var sampleType = Assert.Single(await port.GetSampleTypesAsync([seeded.SampleTypeId], CancellationToken.None));
        Assert.Equal(2, sampleType.Conditions.Count);
        Assert.Equal("C", (await port.GetClinicalGroupAsync(seeded.GroupId, CancellationToken.None))!.UnitCode);
    }

    [DatabaseFact]
    public async Task The_cloner_copies_the_active_rows_to_a_new_tenant_with_fresh_ids_and_remapped_references()
    {
        var (source, target) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var seeded = await SeedCatalogueAsync(source);
        await SaveAsync(source, scope => scope.ServiceProvider.GetRequiredService<IMeasurementUnitRepository>().Add(Inactive(MeasurementUnit.Create("IU/L", Now))));

        CatalogueACloneResult result;
        using (var scope = fixture.AppScope(target))
        {
            using (scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().Elevate())
            {
                result = await scope.ServiceProvider.GetRequiredService<ICatalogueACloner>().CloneAsync(source, target, CancellationToken.None);
                await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
            }
        }

        Assert.Single(result.MeasurementUnits);   // the inactive unit stays behind
        Assert.Equal(2, result.Tests.Count);
        Assert.NotEqual(seeded.ServiceId, result.Services[seeded.ServiceId]);

        using var reader = fixture.AppScope(target);
        var port = reader.ServiceProvider.GetRequiredService<ICatalogueLookup>();
        var service = (await port.GetServiceAsync(result.Services[seeded.ServiceId], CancellationToken.None))!;
        Assert.Equal((result.ClinicalGroups[seeded.GroupId], result.SampleTypes[seeded.SampleTypeId]), (service.ClinicalGroupId!.Value, service.SampleTypeId!.Value));
        Assert.Equal([result.Tests[seeded.TestId]], service.TestIds);
        var test = (await port.GetTestAsync(result.Tests[seeded.TestId], CancellationToken.None))!;
        Assert.Equal((result.MeasurementUnits[seeded.UnitId], 2), (test.UnitId!.Value, test.RangeBands.Count));
        Assert.Contains(test.RangeBands, band => band.PatientConditionId == result.PatientConditions[seeded.PatientConditionId]);
        var list = (await port.GetTestAsync(result.Tests[seeded.ListTestId], CancellationToken.None))!;
        Assert.Equal(3, list.ListValues.Count);
        Assert.Equal(2, await reader.ServiceProvider.GetRequiredService<AppDbContext>().Set<SampleCondition>().CountAsync(row => row.TenantId == target));
    }

    [DatabaseFact]
    public async Task The_cloner_copies_a_legacy_Normal_and_Critical_list_value_as_Critical_only_CAT_061()
    {
        var (source, target) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var seeded = await SeedCatalogueAsync(source);
        using (var legacy = fixture.AppScope(source))
        {
            // a row stored before CAT-061 (validation v0.5.0 O-2): the domain no longer writes it, so the test writes it in SQL
            var context = legacy.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await context.Database.ExecuteSqlInterpolatedAsync($"update app.test_list_values set is_normal = true where tenant_id = {source} and lab_test_id = {seeded.ListTestId} and value = 'Positive'"));
        }

        CatalogueACloneResult result;
        using (var scope = fixture.AppScope(target))
        {
            using (scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>().Elevate())
            {
                result = await scope.ServiceProvider.GetRequiredService<ICatalogueACloner>().CloneAsync(source, target, CancellationToken.None);
                await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
            }
        }

        Assert.True(result.Tests.ContainsKey(seeded.ListTestId));   // the test is copied, not skipped
        using var reader = fixture.AppScope(target);
        var list = (await reader.ServiceProvider.GetRequiredService<ICatalogueLookup>().GetTestAsync(result.Tests[seeded.ListTestId], CancellationToken.None))!;
        var positive = Assert.Single(list.ListValues, value => value.Value == "Positive");
        Assert.Equal((false, true), (positive.IsNormal, positive.IsCritical));
    }

    private sealed record Seeded(Guid GroupId, Guid SampleTypeId, Guid ConditionId, Guid UnitId, Guid PatientConditionId, Guid TestId, Guid ListTestId, Guid ServiceId);

    /// <summary>A small catalogue: Chemistry, Serum (fasting, random), mg/dL, Pregnant, GLU (2 bands) + URINE (list), service GLU.</summary>
    private async Task<Seeded> SeedCatalogueAsync(Guid tenant)
    {
        using var scope = fixture.AppScope(tenant);
        var provider = scope.ServiceProvider;
        var group = ClinicalGroup.Create("CHEM", "C", new LocalizedText("Chemistry", "الكيمياء"), null, null, 1, Now);
        var sampleType = SampleType.Create("SERUM", "SER", new LocalizedText("Serum", "مصل"), [new SampleConditionSpec(null, "FAST", new LocalizedText("Fasting", "صائم"), true), new SampleConditionSpec(null, "RND", new LocalizedText("Random", "عشوائي"), true)], Now);
        var unit = MeasurementUnit.Create("mg/dL", Now);
        var pregnant = PatientCondition.Create(new LocalizedText("Pregnant", "حامل"), Now);
        provider.GetRequiredService<IClinicalGroupRepository>().Add(group);
        provider.GetRequiredService<ISampleTypeRepository>().Add(sampleType);
        provider.GetRequiredService<IMeasurementUnitRepository>().Add(unit);
        provider.GetRequiredService<IPatientConditionRepository>().Add(pregnant);

        var test = LabTest.Create("GLU", new LabTestDefinition(new LocalizedText("Glucose", "جلوكوز"), null, "2345-7", null, group.Id, LabTestType.Numeric, sampleType.Id, sampleType.Conditions[0].Id, unit.Id, GenderRestriction.Any, null, 0, false, true, 1, null), Now);
        test.ReplaceRangeBands(
        [
            new RangeBandSpec(GenderRestriction.Any, 0, 150, RangeAgeUnit.Years, null, 65m, 100m, 40m, 400m, null, null, null),
            new RangeBandSpec(GenderRestriction.Female, 15, 50, RangeAgeUnit.Years, pregnant.Id, 60m, 95m, null, null, null, null, null),
        ], Now);
        var urine = LabTest.Create("URINE", new LabTestDefinition(new LocalizedText("Urine protein", "بروتين البول"), null, null, null, group.Id, LabTestType.RestrictedList, null, null, null, GenderRestriction.Any, null, 0, false, false, 0, null), Now);
        urine.ReplaceListValues([new ListValueSpec(".", true, false, true, true, null), new ListValueSpec("Negative", true, false, false, false, null), new ListValueSpec("Positive", false, true, false, false, "#C8102E")], Now);
        provider.GetRequiredService<ILabTestRepository>().Add(test);
        provider.GetRequiredService<ILabTestRepository>().Add(urine);

        var service = Service.Create("GLU", ServiceType.Test, new ServiceDefinition("GLU", 12, new LocalizedText("Glucose", "جلوكوز"), null, null, null, group.Id, sampleType.Id, sampleType.Conditions[0].Id, null, ["Sugar"], null, GenderRestriction.Any, 24, 1, false, true, false, null), 1, [new ResolvedServiceMember(ServiceMemberKind.Test, test.Id, 1, false, true, LabTestType.Numeric, null)], Now);
        provider.GetRequiredService<IServiceRepository>().Add(service);

        await provider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return new Seeded(group.Id, sampleType.Id, sampleType.Conditions[0].Id, unit.Id, pregnant.Id, test.Id, urine.Id, service.Id);
    }

    private static ServiceDefinition Definition(Seeded seeded) =>
        new("GLU-2", null, new LocalizedText("Glucose 2", "جلوكوز ٢"), null, null, null, seeded.GroupId, seeded.SampleTypeId, null, null, [], null, GenderRestriction.Any, 24, 1, false, true, false, null);

    private static MeasurementUnit Inactive(MeasurementUnit unit)
    {
        unit.Deactivate(Now);
        return unit;
    }

    private async Task SaveAsync(Guid tenant, Action<IServiceScope> stage)
    {
        using var scope = fixture.AppScope(tenant);
        stage(scope);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }
}
