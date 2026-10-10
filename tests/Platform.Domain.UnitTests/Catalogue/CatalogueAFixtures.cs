using Platform.Domain.Catalogue;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Localization;

namespace Platform.Domain.UnitTests.Catalogue;

/// <summary>Builders shared by the catalogue-A scenarios (intake LIS-INT-CAT-001 §16).</summary>
internal static class CatalogueAFixtures
{
    public static readonly DateTimeOffset Now = FixedClock.Instant;

    public static LocalizedText Name(string en = "Glucose", string ar = "جلوكوز") => new(en, ar);

    public static LabTestDefinition TestDefinition(LabTestType type = LabTestType.Numeric, Guid? groupId = null) =>
        new(Name(), "Glucose (fasting)", "2345-7", null, groupId ?? Guid.CreateVersion7(), type, null, null, null, GenderRestriction.Any, null, 1, false, true, 1, null);

    public static LabTest Test(LabTestType type = LabTestType.Numeric, string code = "GLU") => LabTest.Create(code, TestDefinition(type), Now);

    public static ServiceDefinition ServiceDefinition(Guid? groupId = null, Guid? sampleTypeId = null, decimal? profileSum = null, Guid? conditionId = null) =>
        new("GLU", 12, Name(), null, null, null, groupId ?? Guid.CreateVersion7(), sampleTypeId ?? Guid.CreateVersion7(), conditionId, null, ["Sugar"], null, GenderRestriction.Any, 24, 1, false, true, false, profileSum);

    public static ResolvedServiceMember TestMember(int order, LabTestType type = LabTestType.Numeric, bool inSum = false, Guid? id = null) =>
        new(ServiceMemberKind.Test, id ?? Guid.CreateVersion7(), order, inSum, true, type, null);

    public static ResolvedServiceMember ServiceMember(int order, ServiceType type = ServiceType.Test, Guid? id = null) =>
        new(ServiceMemberKind.Service, id ?? Guid.CreateVersion7(), order, false, true, null, type);

    public static RangeBandSpec Band(
        int from,
        int to,
        RangeAgeUnit unit = RangeAgeUnit.Years,
        GenderRestriction sex = GenderRestriction.Any,
        Guid? conditionId = null,
        decimal? low = 70m,
        decimal? high = 110m,
        decimal? panicLow = null,
        decimal? panicHigh = null,
        decimal? reportableLow = null,
        decimal? reportableHigh = null,
        string? text = null) =>
        new(sex, from, to, unit, conditionId, low, high, panicLow, panicHigh, reportableLow, reportableHigh, text);

    public static ListValueSpec Value(string value, bool isDefault = false, bool isPlaceholder = false, bool isCritical = false, bool isNormal = true, string? colour = null) =>
        new(value, isNormal, isCritical, isDefault, isPlaceholder, colour);
}
