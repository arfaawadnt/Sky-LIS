using Platform.Domain.Registration;
using Platform.SharedKernel.Primitives;
using static Platform.Domain.UnitTests.Registration.RegistrationTestData;

namespace Platform.Domain.UnitTests.Registration;

/// <summary>
/// LIS-CR-015 M3b / M3c on the Visit aggregate: a line ordered for a formula operand hangs under its formula line at price 0,
/// is not top-level (no discount, no repricing, no own action) and follows its formula when it is cancelled or released.
/// </summary>
public sealed class VisitFormulaOperandTests
{
    [Fact]
    public void An_operand_line_hangs_under_its_formula_line_at_price_0_with_its_receipt_flag_and_its_own_tube()
    {
        var formula = Test("GFR", ChemistrySerum(), patient: 150m);
        var visit = Register(formula, Operand("CREA", ChemistrySerum(), formula.ServiceId, visible: false), Operand("CBC", HaematologyBlood(), formula.ServiceId));

        var gfr = visit.Lines.Single(line => line.ServiceCode == "GFR");
        var crea = visit.Lines.Single(line => line.ServiceCode == "CREA");
        var cbc = visit.Lines.Single(line => line.ServiceCode == "CBC");
        Assert.Equal((VisitLineKind.FormulaOperand, VisitLineOrigin.Formula, gfr.Id, false), (crea.Kind, crea.Origin, crea.ParentLineId!.Value, crea.VisibleOnReceipt));
        Assert.True(cbc.VisibleOnReceipt);
        Assert.True(gfr.VisibleOnReceipt);
        Assert.All([crea, cbc], line => Assert.Equal((0m, 0m, 0m, 0m), (line.ListPrice, line.PatientShare, line.PayerShare, line.Discount)));
        Assert.False(crea.IsTopLevel);
        Assert.Equal((150m, 150m), (visit.TotalListPrice, visit.PatientShareTotal));
        Assert.Equal(crea.TubeId, gfr.TubeId);   // same BR-02 key: one serum tube
        Assert.NotNull(cbc.TubeId);
        Assert.Equal(2, visit.Tubes.Count);
        var registered = Assert.IsType<VisitRegistered>(visit.DomainEvents.Single(raised => raised is VisitRegistered));
        Assert.Contains(registered.Lines, line => line.LineId == crea.Id && line.TestIds.Count == 1);
    }

    [Fact]
    public void An_operand_follows_a_formula_inside_a_package_and_never_counts_against_the_line_limit()
    {
        var member = Test("GFR", ChemistrySerum());
        var visit = Register(Package("KIDNEY", 300m, member), Operand("CREA", ChemistrySerum(), member.ServiceId));

        var memberLine = visit.Lines.Single(line => line.Kind == VisitLineKind.PackageMember);
        var operand = visit.Lines.Single(line => line.Kind == VisitLineKind.FormulaOperand);
        Assert.Equal(memberLine.Id, operand.ParentLineId);
        Assert.Equal(300m, visit.PatientShareTotal);

        var items = Enumerable.Range(0, Visit.MaxLines).Select(index => Test($"T{index}", null, patient: 1m)).ToList();
        var full = Visit.Register(Header(), [.. items, Operand("CREA", null, items[0].ServiceId)], [], null, Numbers(1), Actor, Now);
        Assert.Equal(Visit.MaxLines + 1, full.Lines.Count);
    }

    [Fact]
    public void An_operand_needs_its_formula_line_on_the_visit()
    {
        var refused = Assert.Throws<DomainException>(() => Register(Test("GLU", ChemistrySerum()), Operand("CREA", ChemistrySerum(), Guid.CreateVersion7())));
        Assert.Equal(RegistrationCodes.HeaderInvalid, refused.Code);
    }

    [Fact]
    public void An_operand_is_not_cancelled_adjusted_or_released_alone_REG_026_and_follows_its_formula()
    {
        var formula = Test("GFR", ChemistrySerum());
        var visit = Register(formula, Operand("CREA", ChemistrySerum(), formula.ServiceId), Test("CBC", HaematologyBlood()));
        var gfr = visit.Lines.Single(line => line.ServiceCode == "GFR");
        var crea = visit.Lines.Single(line => line.ServiceCode == "CREA");

        Assert.Equal(RegistrationCodes.FormulaOperandAction, Assert.Throws<DomainException>(() => visit.CancelLines([crea.Id], null, "x", Actor, Now)).Code);
        Assert.Equal(RegistrationCodes.FormulaOperandAction, Assert.Throws<DomainException>(() => visit.AdjustLineShares(crea.Id, 0m, 0m, null, "x", Actor, Now)).Code);
        Assert.Equal([gfr.Id, crea.Id], visit.LinesAffectedBy([gfr.Id]).Select(line => line.Id).ToArray());

        visit.ClearDomainEvents();
        var cancelled = visit.CancelLines([gfr.Id], null, "Not needed", Actor, Now);

        Assert.Equal([gfr.Id, crea.Id], cancelled.Select(line => line.Id).ToArray());
        Assert.Equal(VisitLineStatus.Cancelled, crea.Status);
        Assert.Equal(2, visit.DomainEvents.OfType<VisitLineCancelled>().Count());
        Assert.Equal(VisitLineStatus.Ordered, visit.Lines.Single(line => line.ServiceCode == "CBC").Status);
    }

    [Fact]
    public void A_package_cancels_with_its_members_and_their_operands()
    {
        var member = Test("GFR", ChemistrySerum());
        var visit = Register(Package("KIDNEY", 300m, member), Operand("CREA", ChemistrySerum(), member.ServiceId));
        var package = visit.Lines.Single(line => line.Kind == VisitLineKind.Package);

        Assert.Equal(3, visit.LinesAffectedBy([package.Id]).Count);
        visit.CancelLines([package.Id], null, "Not needed", Actor, Now);

        Assert.All(visit.Lines, line => Assert.Equal(VisitLineStatus.Cancelled, line.Status));
    }

    [Fact]
    public void A_reserved_formula_reserves_its_operand_which_is_released_with_it()
    {
        var formula = Test("GFR", ChemistrySerum(), reserved: true);
        var visit = Register(Test("CBC", HaematologyBlood()), formula, Operand("CREA", ChemistrySerum(), formula.ServiceId));
        var gfr = visit.Lines.Single(line => line.ServiceCode == "GFR");
        var crea = visit.Lines.Single(line => line.ServiceCode == "CREA");
        Assert.Equal((VisitLineStatus.Reserved, null), (crea.Status, crea.TubeId));

        var alone = Assert.Throws<DomainException>(() => visit.ReleaseReserved([new LineRelease(crea.Id, ChemistrySerum(), null)], Numbers(1, 50), Actor, Now));
        Assert.Equal(RegistrationCodes.FormulaOperandAction, alone.Code);

        visit.ReleaseReserved([new LineRelease(gfr.Id, ChemistrySerum(), Now.AddHours(24)), new LineRelease(crea.Id, ChemistrySerum(), Now.AddHours(24))], Numbers(1, 50), Actor, Now);

        Assert.Equal((VisitLineStatus.Ordered, VisitLineStatus.Ordered), (gfr.Status, crea.Status));
        Assert.NotNull(crea.TubeId);
        Assert.Equal(gfr.TubeId, crea.TubeId);
    }

    [Fact]
    public void Operands_take_no_discount_and_keep_price_0_on_a_contract_transfer()
    {
        var formula = Test("GFR", ChemistrySerum(), patient: 100m);
        var visit = Visit.Register(Header(), [formula, Operand("CREA", ChemistrySerum(), formula.ServiceId)], [], Discount(DiscountKind.Percent, 10m), Numbers(1), Actor, Now);
        var crea = visit.Lines.Single(line => line.ServiceCode == "CREA");
        Assert.Equal((10m, 90m, 0m), (visit.DiscountTotal, visit.PatientShareTotal, crea.Discount));

        visit.TransferContract(Guid.CreateVersion7(), Guid.CreateVersion7(), new Dictionary<Guid, LinePrice> { [formula.ServiceId] = new(200m, 200m, 0m) }, null, "Insurance", Actor, Now);

        Assert.Equal((0m, 0m), (crea.ListPrice, crea.PatientShare));
        Assert.Equal(200m, visit.TotalListPrice);
    }

    [Fact]
    public void An_operand_is_a_live_service_of_the_visit_and_holds_its_tests_under_its_own_code()
    {
        var formula = Test("GFR", ChemistrySerum());
        var operand = Operand("CREA", ChemistrySerum(), formula.ServiceId);
        var visit = Register(formula, operand);

        Assert.Contains(operand.ServiceId, visit.LiveServiceIds);
        Assert.Equal("CREA", visit.LiveTestHolders()[operand.TestIds[0]]);
        Assert.Equal(RegistrationCodes.DuplicateService, Assert.Throws<DomainException>(() => visit.AddLines([operand with { Kind = VisitLineKind.Service }], [], Numbers(1, 60), Actor, Now)).Code);
    }

    private static VisitOrderItem Operand(string code, TubeSpec? tube, Guid formulaServiceId, bool visible = true) =>
        Test(code, tube, patient: 80m) with
        {
            Kind = VisitLineKind.FormulaOperand,
            Origin = VisitLineOrigin.Formula,
            FormulaServiceId = formulaServiceId,
            VisibleOnReceipt = visible,
        };
}
