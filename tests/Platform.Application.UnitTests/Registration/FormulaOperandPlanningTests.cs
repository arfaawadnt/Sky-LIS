using Platform.Application.Catalogue;
using Platform.Application.Registration;
using Platform.Application.Registration.Commands;
using Platform.Application.Registration.Queries;
using Platform.Contracts.Registration;
using Platform.Domain.Catalogue;
using Platform.Domain.Registration;

namespace Platform.Application.UnitTests.Registration;

/// <summary>
/// LIS-CR-015 M3b at registration over in-memory ports: the order planner adds the service chosen for each operand of a formula
/// as a free line under the formula's line, unless the visit or the order already carries the test.
/// </summary>
public sealed class FormulaOperandPlanningTests
{
    private readonly RegistrationWorld _world = new();
    private readonly FakeFormulaOperands _formulas = new();

    public FormulaOperandPlanningTests()
    {
        var crea = Catalogue.Creatinine.TestIds[0];
        var urea = Catalogue.Urea.TestIds[0];
        Gfr = AddFormulaService("GFR", out var gfrTest);
        Crcl = AddFormulaService("CRCL", out var crclTest);
        Kidney = AddService("KIDNEY", ServiceType.Package, null, null, [new CatalogueServiceMemberInfo(ServiceMemberKind.Service, Gfr.Id, 1, true, true)]);
        _formulas.Operands.Add(new CatalogueFormulaOperandInfo(Gfr.Id, gfrTest, crea, "CREA", Catalogue.Creatinine.Id, false));
        _formulas.Operands.Add(new CatalogueFormulaOperandInfo(Crcl.Id, crclTest, crea, "CREA", Catalogue.Creatinine.Id, true));
        _formulas.Operands.Add(new CatalogueFormulaOperandInfo(Crcl.Id, crclTest, urea, "UREA", Catalogue.Urea.Id, true));
    }

    private FakeCatalogue Catalogue => _world.Catalogue;

    private CatalogueServiceInfo Gfr { get; }

    private CatalogueServiceInfo Crcl { get; }

    private CatalogueServiceInfo Kidney { get; }

    [Fact]
    public async Task Registering_a_formula_service_adds_its_operand_free_under_the_formula_line()
    {
        await Handler().Handle(Command(Gfr.Id), CancellationToken.None);

        var visit = Assert.Single(_world.Visits.Items);
        var gfr = visit.Lines.Single(line => line.ServiceId == Gfr.Id);
        var crea = visit.Lines.Single(line => line.ServiceId == Catalogue.Creatinine.Id);
        Assert.Equal((VisitLineKind.FormulaOperand, VisitLineOrigin.Formula, gfr.Id, false), (crea.Kind, crea.Origin, crea.ParentLineId!.Value, crea.VisibleOnReceipt));
        Assert.Equal((0m, 0m, 0m), (crea.ListPrice, crea.PatientShare, crea.PayerShare));
        Assert.Equal((100m, 100m), (visit.TotalListPrice, visit.PatientShareTotal));
        Assert.Equal(gfr.TubeId, crea.TubeId);
        Assert.Single(visit.Tubes);
        Assert.Equal(Catalogue.Creatinine.TestIds, crea.TestIds);
    }

    [Fact]
    public async Task An_operand_test_already_in_the_order_or_on_the_visit_is_not_added_again()
    {
        var withCrea = await Plan(Gfr.Id, Catalogue.Creatinine.Id);
        Assert.Equal(["GFR", "CREA"], withCrea.Lines.Select(line => line.Item.ServiceCode).ToArray());
        Assert.All(withCrea.Lines, line => Assert.Equal(VisitLineKind.Service, line.Item.Kind));

        var throughProfile = await Plan(Catalogue.Renal.Id, Gfr.Id);
        Assert.DoesNotContain(throughProfile.Lines, line => line.Item.Kind == VisitLineKind.FormulaOperand);

        await Handler().Handle(Command(Catalogue.Creatinine.Id), CancellationToken.None);
        var visit = Assert.Single(_world.Visits.Items);
        await AddLinesHandler().Handle(new AddVisitLinesCommand(visit.Id, [new OrderLineRequest(Gfr.Id, false)], null, false), CancellationToken.None);
        Assert.DoesNotContain(visit.Lines, line => line.Kind == VisitLineKind.FormulaOperand);
        Assert.Equal(2, visit.Lines.Count);
    }

    [Fact]
    public async Task Two_formulas_reading_the_same_test_order_it_once_under_the_first()
    {
        var plan = await Plan(Gfr.Id, Crcl.Id);

        Assert.Equal(["GFR", "CREA", "CRCL", "UREA"], plan.Lines.Select(line => line.Item.ServiceCode).ToArray());
        Assert.Equal(("GFR", "CRCL"), (plan.Lines[1].FormulaServiceCode, plan.Lines[3].FormulaServiceCode));
        Assert.Equal((Gfr.Id, Crcl.Id), (plan.Lines[1].Item.FormulaServiceId!.Value, plan.Lines[3].Item.FormulaServiceId!.Value));
        Assert.Equal(200m, plan.Lines.Sum(line => line.Item.PatientShare));
    }

    [Fact]
    public async Task A_formula_inside_a_package_orders_its_operand_under_the_member_line()
    {
        var plan = await Plan(Kidney.Id);

        Assert.Equal(["KIDNEY", "CREA"], plan.Lines.Select(line => line.Item.ServiceCode).ToArray());
        Assert.Equal((Gfr.Id, "GFR"), (plan.Lines[1].Item.FormulaServiceId!.Value, plan.Lines[1].FormulaServiceCode));

        await Handler().Handle(Command(Kidney.Id), CancellationToken.None);
        var visit = Assert.Single(_world.Visits.Items);
        var member = visit.Lines.Single(line => line.Kind == VisitLineKind.PackageMember);
        Assert.Equal(member.Id, visit.Lines.Single(line => line.Kind == VisitLineKind.FormulaOperand).ParentLineId);
    }

    [Fact]
    public async Task An_operand_service_that_cannot_be_ordered_is_skipped_with_REG_207()
    {
        _formulas.Operands.Clear();
        _formulas.Operands.Add(new CatalogueFormulaOperandInfo(Gfr.Id, Guid.CreateVersion7(), Guid.CreateVersion7(), "OLD", Catalogue.Inactive.Id, true));
        _formulas.Operands.Add(new CatalogueFormulaOperandInfo(Gfr.Id, Guid.CreateVersion7(), Guid.CreateVersion7(), "PSA", Catalogue.Psa.Id, true));
        _formulas.Operands.Add(new CatalogueFormulaOperandInfo(Gfr.Id, Guid.CreateVersion7(), Guid.CreateVersion7(), "NONE", null, true));

        var plan = await Plan(Gfr.Id);

        Assert.Equal(["GFR"], plan.Lines.Select(line => line.Item.ServiceCode).ToArray());
        Assert.Equal(2, plan.Warnings.Count(warning => warning.Code == RegistrationCodes.WarningSkippedCompanion));
        Assert.Empty(plan.Issues);
    }

    [Fact]
    public async Task The_dry_run_and_the_price_quote_show_the_operand_at_price_0_with_its_formula()
    {
        var check = await new CheckOrderHandler(_world.Visits, _world.Patients, _world.Payers, Catalogue, _world.Rules, _world.Prices, _world.Settings, _world.Clock, _formulas)
            .Handle(new CheckOrderQuery(null, _world.Female.Id, _world.PayerId, _world.TierId, null, [new OrderLineRequest(Gfr.Id, false)], 120), CancellationToken.None);

        var operand = Assert.Single(check.Lines, line => line.Kind == nameof(VisitLineKind.FormulaOperand));
        Assert.Equal(("CREA", "GFR", false, 0m, nameof(VisitLineOrigin.Formula)), (operand.Code, operand.FormulaServiceCode, operand.VisibleOnReceipt, operand.PatientShare, operand.Origin));
        Assert.Equal(100m, check.Totals.PatientShareTotal);
        Assert.Empty(check.Issues);

        var quote = await new OrderPlanner(Catalogue, _world.Rules, _world.Prices, _world.Visits, _formulas).PlanAsync(
            new OrderContext(null, _world.PayerId, _world.TierId, null, [new OrderLineRequest(Gfr.Id, false)], new HashSet<Guid>(), null, RegistrationWorld.Now, 0, ForQuote: true),
            CancellationToken.None);
        Assert.Equal(["GFR", "CREA"], quote.Lines.Select(line => line.Item.ServiceCode).ToArray());
        Assert.Equal(0m, quote.Lines[1].Item.ListPrice);
    }

    private CatalogueServiceInfo AddFormulaService(string code, out Guid testId)
    {
        var test = new CatalogueTestInfo(
            Guid.CreateVersion7(), code, code, code, null, LabTestType.Formula, FakeCatalogue.Chemistry, null, null, null, null, GenderRestriction.Any, null, 2, false, true, 1, null,
            new LabTestTiming(24, 0, null, FrequencyCheckAction.Alert, null, null, LabTestTiming.EveryDay), "[CREA]", ["CREA"], [], [], [], true);
        Catalogue.Tests[test.Id] = test;
        testId = test.Id;
        return AddService(code, ServiceType.Test, FakeCatalogue.Chemistry, FakeCatalogue.Serum, [new CatalogueServiceMemberInfo(ServiceMemberKind.Test, test.Id, 1, true, true)]);
    }

    private CatalogueServiceInfo AddService(string code, ServiceType type, Guid? group, Guid? sampleType, IReadOnlyList<CatalogueServiceMemberInfo> members)
    {
        var service = new CatalogueServiceInfo(
            Guid.CreateVersion7(), code, code, null, code + " en", code + " ar", null, type, group, sampleType, null, GenderRestriction.Any, 0, 1, null, false, true, true, null, Catalogue.Services.Count + 1, true, members);
        Catalogue.Services[service.Id] = service;
        return service;
    }

    private Task<OrderPlan> Plan(params Guid[] services) =>
        new OrderPlanner(Catalogue, _world.Rules, _world.Prices, _world.Visits, _formulas).PlanAsync(
            new OrderContext(new OrderPatient(_world.Female.Id, "Female"), _world.PayerId, _world.TierId, null, [.. services.Select(id => new OrderLineRequest(id, false))], new HashSet<Guid>(), null, RegistrationWorld.Now, 0),
            CancellationToken.None);

    private RegisterVisitCommand Command(params Guid[] services) =>
        new(
            _world.Female.Id,
            null,
            null,
            _world.PayerId,
            _world.TierId,
            null,
            null,
            null,
            false,
            false,
            false,
            [.. services.Select(id => new OrderLineRequest(id, false))],
            null,
            null,
            false,
            120);

    private RegisterVisitHandler Handler() =>
        new(_world.Visits, _world.Bookings, _world.Patients, _world.Payers, _world.Doctors, Catalogue, _world.Rules, _world.Prices, _world.Settings, _world.Privileges, _world.Numbers, _world.UnitOfWork, _world.User, _world.Clock, _formulas);

    private AddVisitLinesHandler AddLinesHandler() =>
        new(_world.Visits, _world.Patients, Catalogue, _world.Rules, _world.Prices, _world.Numbers, _world.UnitOfWork, _world.User, _world.Clock, _formulas);
}

/// <summary>In-memory <see cref="IFormulaOperandLookup"/>: the operands of the requested services, in insertion order.</summary>
internal sealed class FakeFormulaOperands : IFormulaOperandLookup
{
    public List<CatalogueFormulaOperandInfo> Operands { get; } = [];

    public Task<IReadOnlyList<CatalogueFormulaOperandInfo>> ForServicesAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CatalogueFormulaOperandInfo>>([.. Operands.Where(operand => serviceIds.Contains(operand.ServiceId))]);
}
