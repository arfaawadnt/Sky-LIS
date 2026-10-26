using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Catalogue;
using Platform.Domain.Catalogue;
using Platform.Infrastructure.Persistence;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Localization;

namespace Platform.IntegrationTests;

/// <summary>
/// LIS-CR-015: the operands stored as <c>app.tests.formula_operands</c> (jsonb) and the adapter of <see cref="IFormulaOperandLookup"/>
/// — a profile and a test service carrying a formula, an older formula without stored operands (the default service per reference),
/// an inactive operand test skipped, another tenant seeing nothing.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class FormulaOperandLookupTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    [DatabaseFact]
    public async Task The_lookup_answers_the_stored_operands_of_the_formulas_the_services_carry()
    {
        var tenant = Guid.CreateVersion7();
        var seeded = await SeedAsync(tenant);

        using var scope = fixture.AppScope(tenant);
        var rows = await scope.ServiceProvider.GetRequiredService<IFormulaOperandLookup>()
            .ForServicesAsync([seeded.Lipid, seeded.LdlService, seeded.OldService, seeded.CholService], CancellationToken.None);

        Assert.Equal(
        [
            new CatalogueFormulaOperandInfo(seeded.Lipid, seeded.Ldl, seeded.Chol, "CHOL", seeded.Lipid, true),
            new CatalogueFormulaOperandInfo(seeded.Lipid, seeded.Ldl, seeded.Hdl, "HDL", seeded.HdlService, false),
            new CatalogueFormulaOperandInfo(seeded.LdlService, seeded.Ldl, seeded.Chol, "CHOL", seeded.Lipid, true),
            new CatalogueFormulaOperandInfo(seeded.LdlService, seeded.Ldl, seeded.Hdl, "HDL", seeded.HdlService, false),
            // an older formula (no stored operands): the default service per reference, off the receipt
            new CatalogueFormulaOperandInfo(seeded.OldService, seeded.Old, seeded.Chol, "CHOL", seeded.CholService, false),
        ],
        rows.ToArray());

        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IFormulaOperandLookup>().ForServicesAsync([], CancellationToken.None));

        using var other = fixture.AppScope(Guid.CreateVersion7());
        Assert.Empty(await other.ServiceProvider.GetRequiredService<IFormulaOperandLookup>().ForServicesAsync([seeded.Lipid, seeded.LdlService], CancellationToken.None));
    }

    [DatabaseFact]
    public async Task The_operands_round_trip_through_the_jsonb_column_and_older_rows_read_empty()
    {
        var tenant = Guid.CreateVersion7();
        var seeded = await SeedAsync(tenant);

        using var scope = fixture.AppScope(tenant);
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ldl = await context.Set<LabTest>().AsNoTracking().IgnoreAutoIncludes().SingleAsync(test => test.Id == seeded.Ldl);
        Assert.Equal(
            [new FormulaOperand("CHOL", seeded.Lipid, true), new FormulaOperand("HDL", seeded.HdlService, false), new FormulaOperand("TG", null, false)],
            ldl.FormulaOperands.ToArray());
        var old = await context.Set<LabTest>().AsNoTracking().IgnoreAutoIncludes().SingleAsync(test => test.Id == seeded.Old);
        Assert.Empty(old.FormulaOperands);

        var json = await context.Database.SqlQuery<string>($"SELECT formula_operands::text AS \"Value\" FROM app.tests WHERE id = {seeded.Ldl}").SingleAsync();
        Assert.Contains("\"testCode\": \"CHOL\"", json, StringComparison.Ordinal);
        Assert.Contains("\"visibleOnReceipt\": true", json, StringComparison.Ordinal);

        // the editor's read: the tests by code with their carriers in sequence order
        var reads = await scope.ServiceProvider.GetRequiredService<ILabTestRepository>().FormulaOperandTestsAsync(["chol", "tg"], CancellationToken.None);
        var chol = Assert.Single(reads, read => read.Code == "CHOL");
        Assert.Equal([seeded.CholService, seeded.Lipid], chol.Services.Select(service => service.Id).ToArray());
        Assert.Equal(seeded.CholService, chol.Choices.DefaultServiceId);
        Assert.False(Assert.Single(reads, read => read.Code == "TG").IsActive);
    }

    private sealed record Seeded(Guid Chol, Guid Hdl, Guid Ldl, Guid Old, Guid CholService, Guid HdlService, Guid Lipid, Guid LdlService, Guid OldService);

    /// <summary>CHOL, HDL, TG (inactive) and the formulas LDL (operands stored) and OLDF (saved without operands), with their services.</summary>
    private async Task<Seeded> SeedAsync(Guid tenant)
    {
        using var scope = fixture.AppScope(tenant);
        var provider = scope.ServiceProvider;
        var group = ClinicalGroup.Create("CHEM", "C", new LocalizedText("Chemistry", "الكيمياء"), null, null, 1, Now);
        var serum = SampleType.Create("SERUM", "SER", new LocalizedText("Serum", "مصل"), [], Now);
        provider.GetRequiredService<IClinicalGroupRepository>().Add(group);
        provider.GetRequiredService<ISampleTypeRepository>().Add(serum);

        LabTest Test(string code, LabTestType type) => LabTest.Create(code, new LabTestDefinition(new LocalizedText(code, code), null, null, null, group.Id, type, null, null, null, GenderRestriction.Any, null, 0, false, true, 1, null), Now);
        var chol = Test("CHOL", LabTestType.Numeric);
        var hdl = Test("HDL", LabTestType.Numeric);
        var tg = Test("TG", LabTestType.Numeric);
        var ldl = Test("LDL", LabTestType.Formula);
        var old = Test("OLDF", LabTestType.Formula);

        Service Single(string code, ServiceType type, int sequence, params LabTest[] tests) => Service.Create(
            code,
            type,
            new ServiceDefinition(code, null, new LocalizedText(code, code), null, null, null, group.Id, serum.Id, null, null, [], null, GenderRestriction.Any, 24, 1, false, true, false, null),
            sequence,
            tests.Select((test, index) => new ResolvedServiceMember(ServiceMemberKind.Test, test.Id, index + 1, false, true, test.Type, null)).ToList(),
            Now);
        var cholService = Single("CHOL", ServiceType.Test, 1, chol);
        var hdlService = Single("HDL", ServiceType.Test, 2, hdl);
        var lipid = Single("LIPID", ServiceType.Profile, 3, chol, hdl, ldl);
        var ldlService = Single("LDL", ServiceType.Test, 4, ldl);
        var oldService = Single("OLDF", ServiceType.Test, 5, old);

        ldl.SetFormula(
            "[CHOL] - [HDL] - [TG] / 5",
            ["CHOL", "HDL", "TG"],
            [new FormulaOperand("CHOL", lipid.Id, true)],
            [new FormulaOperandChoices("CHOL", [cholService.Id, lipid.Id], cholService.Id), new FormulaOperandChoices("HDL", [hdlService.Id, lipid.Id], hdlService.Id), new FormulaOperandChoices("TG", [], null)],
            Now);
        old.SetFormula("[CHOL] * 2", ["CHOL"], Now);   // as saved before LIS-CR-015: no operands
        tg.Deactivate(Now);

        var tests = provider.GetRequiredService<ILabTestRepository>();
        foreach (var test in new[] { chol, hdl, tg, ldl, old })
        {
            tests.Add(test);
        }

        var services = provider.GetRequiredService<IServiceRepository>();
        foreach (var service in new[] { cholService, hdlService, lipid, ldlService, oldService })
        {
            services.Add(service);
        }

        await provider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return new Seeded(chol.Id, hdl.Id, ldl.Id, old.Id, cholService.Id, hdlService.Id, lipid.Id, ldlService.Id, oldService.Id);
    }
}
