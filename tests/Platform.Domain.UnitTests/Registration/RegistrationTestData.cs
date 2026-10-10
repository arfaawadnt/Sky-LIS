using Platform.Domain.Registration;

namespace Platform.Domain.UnitTests.Registration;

/// <summary>Builders of the Registration domain tests: catalogue-like tube specs, order items and a visit header.</summary>
internal static class RegistrationTestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 8, 8, 0, 0, TimeSpan.Zero);   // Thursday 10:00 in Cairo (UTC+2)
    public static readonly Guid Actor = Guid.CreateVersion7();
    public static readonly Guid Chemistry = Guid.CreateVersion7();
    public static readonly Guid Haematology = Guid.CreateVersion7();
    public static readonly Guid Serum = Guid.CreateVersion7();
    public static readonly Guid WholeBlood = Guid.CreateVersion7();
    public static readonly Guid Fasting = Guid.CreateVersion7();

    public static TubeSpec ChemistrySerum(Guid? condition = null) =>
        new(Chemistry, Serum, condition, "C", "Chemistry", "الكيمياء", "Serum", "مصل", condition is null ? null : "Fasting", condition is null ? null : "صائم");

    public static TubeSpec HaematologyBlood() => new(Haematology, WholeBlood, null, "H", "Haematology", "أمراض الدم", "Whole blood", "دم كامل", null, null);

    public static VisitOrderItem Test(string code, TubeSpec? tube, decimal patient = 100m, decimal payer = 0m, bool reserved = false, Guid? serviceId = null) =>
        new(serviceId ?? Guid.CreateVersion7(), code, code + " test", "تحليل " + code, "Test", VisitLineKind.Service, tube, [Guid.CreateVersion7()], patient + payer, patient, payer, Now.AddHours(24), reserved, VisitLineOrigin.Manual, null, []);

    public static VisitOrderItem Additional(string code, decimal patient = 50m) =>
        new(Guid.CreateVersion7(), code, code, code, "Additional", VisitLineKind.Service, null, [], patient, patient, 0m, null, false, VisitLineOrigin.Manual, null, []);

    public static VisitOrderItem Package(string code, decimal patient, params VisitOrderItem[] members) =>
        new(Guid.CreateVersion7(), code, code + " panel", "باقة " + code, "Package", VisitLineKind.Package, null, [], patient, patient, 0m, Now.AddHours(48), false, VisitLineOrigin.Manual, null,
            [.. members.Select(member => member with { Kind = VisitLineKind.PackageMember, ListPrice = 0m, PatientShare = 0m, PayerShare = 0m })]);

    public static VisitRegistration Header(string accession = "ACC-2026-00001", Guid? patientId = null) =>
        new(accession, patientId ?? Guid.CreateVersion7(), Guid.CreateVersion7(), VisitLocationType.Walkin, null, null, Guid.CreateVersion7(), Guid.CreateVersion7(), null, null, null, null, false, false, false, 120, null);

    public static SampleNumberSource Numbers(int count, int start = 1) => new(Enumerable.Range(start, count).Select(index => $"SMP-2026-{index:00000}"));

    public static Visit Register(params VisitOrderItem[] items) =>
        Visit.Register(Header(), items, [], null, Numbers(10), Actor, Now);

    public static DiscountRequest Discount(DiscountKind kind, decimal value, int cap = 20) => new(kind, value, Guid.CreateVersion7(), "Staff discount", cap);
}
