using Platform.Domain.Catalogue;
using Platform.SharedKernel.Primitives;
using static Platform.Domain.UnitTests.Catalogue.CatalogueAFixtures;

namespace Platform.Domain.UnitTests.Catalogue;

/// <summary>LIS-CR-015 (M3b / M3c): the operands stored with a formula — one per reference, defaults, the CAT-067 rule.</summary>
public sealed class FormulaOperandTests
{
    private static readonly Guid CholService = Guid.CreateVersion7();
    private static readonly Guid HdlService = Guid.CreateVersion7();
    private static readonly Guid LipidProfile = Guid.CreateVersion7();

    private static readonly FormulaOperandChoices[] Choices =
    [
        new("CHOL", [CholService, LipidProfile], CholService),
        new("HDL", [HdlService, LipidProfile], HdlService),
        new("TG", [LipidProfile], null),
    ];

    [Fact]
    public void Every_reference_gets_one_operand_in_reference_order_missing_ones_take_the_default_off_the_receipt()
    {
        var test = Test(LabTestType.Formula, "LDL");

        test.SetFormula("[CHOL] - [HDL] - [TG] / 5", ["CHOL", "HDL", "TG"], [new FormulaOperand(" hdl ", LipidProfile, true)], Choices, Now);

        Assert.Equal(
            [new FormulaOperand("CHOL", CholService, false), new FormulaOperand("HDL", LipidProfile, true), new FormulaOperand("TG", null, false)],
            test.FormulaOperands);
    }

    [Fact]
    public void Operands_of_codes_no_longer_referenced_are_dropped_and_a_repeated_code_keeps_its_first_entry()
    {
        var test = Test(LabTestType.Formula, "LDL");

        test.SetFormula(
            "[HDL] * 2",
            ["HDL"],
            [new FormulaOperand("CHOL", CholService, true), new FormulaOperand("HDL", null, true), new FormulaOperand("HDL", HdlService, false)],
            Choices,
            Now);

        Assert.Equal([new FormulaOperand("HDL", null, false)], test.FormulaOperands);   // nothing ordered: off the receipt
    }

    [Fact]
    public void A_service_that_does_not_carry_the_test_is_refused_CAT_067_and_an_empty_id_means_none()
    {
        var test = Test(LabTestType.Formula, "LDL");

        var refused = Assert.Throws<DomainException>(() => test.SetFormula("[CHOL] + 1", ["CHOL"], [new FormulaOperand("CHOL", HdlService, false)], Choices, Now));
        Assert.Equal(LabTestCodes.FormulaOperandServiceInvalid, refused.Code);
        Assert.Equal("CAT-067", refused.Code);
        Assert.Equal(LabTestCodes.FormulaOperandServiceInvalid, Assert.Throws<DomainException>(() => test.SetFormula("[GLU] + 1", ["GLU"], [new FormulaOperand("GLU", CholService, false)], Choices, Now)).Code);
        Assert.Null(test.FormulaExpression);   // nothing changed

        test.SetFormula("[CHOL] + 1", ["CHOL"], [new FormulaOperand("CHOL", Guid.Empty, true)], Choices, Now);
        Assert.Equal([new FormulaOperand("CHOL", null, false)], test.FormulaOperands);
    }

    [Fact]
    public void Clearing_the_formula_clears_the_operands_and_the_older_overload_leaves_them_unset()
    {
        var test = Test(LabTestType.Formula, "LDL");
        test.SetFormula("[CHOL] + 1", ["CHOL"], [], Choices, Now);
        Assert.Single(test.FormulaOperands);

        test.SetFormula(null, [], [], Choices, Now);
        Assert.Empty(test.FormulaOperands);

        test.SetFormula("[CHOL] + 1", ["CHOL"], Now);   // a copy or an older caller: readers derive the defaults
        Assert.Equal(["CHOL"], test.FormulaReferences);
        Assert.Empty(test.FormulaOperands);
    }

    [Fact]
    public void The_default_service_is_the_first_active_orderable_single_test_service()
    {
        var inactive = Guid.CreateVersion7();
        var hidden = Guid.CreateVersion7();
        var profile = Guid.CreateVersion7();
        var own = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        var choices = FormulaOperandChoices.From(" chol ",
        [
            new FormulaOperandCarrier(inactive, ServiceType.Test, false, true, 1),
            new FormulaOperandCarrier(hidden, ServiceType.Test, true, false, 1),
            new FormulaOperandCarrier(profile, ServiceType.Profile, true, true, 3),
            new FormulaOperandCarrier(own, ServiceType.Test, true, true, 1),
            new FormulaOperandCarrier(second, ServiceType.Test, true, true, 1),
        ]);

        Assert.Equal("CHOL", choices.TestCode);
        Assert.Equal([profile, own, second], choices.ServiceIds);
        Assert.Equal(own, choices.DefaultServiceId);
        Assert.False(choices.Allows(inactive));
        Assert.Null(FormulaOperandChoices.From("TG", [new FormulaOperandCarrier(profile, ServiceType.Profile, true, true, 3)]).DefaultServiceId);
    }
}
