using Platform.Application.Catalogue;
using Platform.Application.Catalogue.Commands;
using Platform.Contracts.Catalogue;
using Platform.Domain.Catalogue;

namespace Platform.Application.UnitTests.Catalogue;

/// <summary>LIS-CR-015: what the formula check answers per reference (the services that may be ordered and the default) and the command's shape rules.</summary>
public sealed class FormulaOperandChoiceTests
{
    private static readonly Guid Chol = Guid.CreateVersion7();
    private static readonly Guid CholService = Guid.CreateVersion7();
    private static readonly Guid LipidProfile = Guid.CreateVersion7();
    private static readonly Guid OldCholService = Guid.CreateVersion7();

    private static FormulaOperandTestRead Cholesterol() => new(Chol, "CHOL", "Cholesterol", "الكوليسترول", true,
    [
        new FormulaOperandServiceRead(OldCholService, "CHOL-OLD", "Cholesterol (old)", "قديم", ServiceType.Test, false, true, 1),
        new FormulaOperandServiceRead(LipidProfile, "LIPID", "Lipid profile", "دهون", ServiceType.Profile, true, true, 4),
        new FormulaOperandServiceRead(CholService, "CHOL", "Cholesterol", "الكوليسترول", ServiceType.Test, true, true, 1),
    ]);

    [Fact]
    public void The_check_lists_per_reference_the_active_orderable_carriers_and_the_single_test_service_as_default()
    {
        var choices = FormulaOperandChoiceViews.For(["CHOL", "HDL"], [Cholesterol()]);

        Assert.Collection(
            choices,
            chol =>
            {
                Assert.Equal(("CHOL", (Guid?)Chol, "Cholesterol", (Guid?)CholService), (chol.TestCode, chol.TestId, chol.TestNameEn, chol.DefaultServiceId));
                Assert.Equal(["LIPID", "CHOL"], chol.Services.Select(service => service.Code).ToArray());
                Assert.Equal(["Profile", "Test"], chol.Services.Select(service => service.Type).ToArray());
            },
            hdl =>
            {
                Assert.Equal(("HDL", (Guid?)null, (Guid?)null), (hdl.TestCode, hdl.TestId, hdl.DefaultServiceId));
                Assert.Empty(hdl.Services);
            });
    }

    [Fact]
    public void The_choices_of_a_read_feed_the_aggregate()
    {
        var choices = Cholesterol().Choices;

        Assert.Equal(CholService, choices.DefaultServiceId);
        Assert.True(choices.Allows(LipidProfile));
        Assert.False(choices.Allows(OldCholService));
    }

    [Fact]
    public void Operands_carry_a_test_code_and_null_operands_keep_the_stored_choices()
    {
        var validator = new SetLabTestFormulaValidator();
        var id = Guid.CreateVersion7();

        Assert.True(validator.Validate(new SetLabTestFormulaCommand(id, "[CHOL] + 1")).IsValid);
        Assert.True(validator.Validate(new SetLabTestFormulaCommand(id, "[CHOL] + 1", [new FormulaOperandItem("CHOL", null, true)])).IsValid);

        var blank = validator.Validate(new SetLabTestFormulaCommand(id, "[CHOL] + 1", [new FormulaOperandItem(" ", null, false)]));
        Assert.Equal("Operands[0].TestCode", Assert.Single(blank.Errors).PropertyName);

        var tooMany = Enumerable.Range(0, SetLabTestFormulaValidator.MaxOperands + 1).Select(index => new FormulaOperandItem($"T{index}", null, false)).ToList();
        Assert.False(validator.Validate(new SetLabTestFormulaCommand(id, "[CHOL] + 1", tooMany)).IsValid);
    }
}
