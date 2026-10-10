using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

/// <summary>
/// A laboratory unit (SRS CP-39; LDM §2.2, §3.5 "Group"): splits tubes (BR-02), result rights and reports. The unit
/// code is the letter printed on the tube label (LDM §2.2). Never deleted; deactivation keeps existing services and
/// tests working and removes the group from new selections (BR-24).
/// </summary>
public sealed class ClinicalGroup : AggregateRoot, ITenantScoped
{
    public const int MaxCodeLength = 16;
    public const int MaxUnitCodeLength = 4;
    public const int MaxReportNameLength = 200;
    public const int MaxDisplayOrder = 9999;

    private ClinicalGroup()
    {
        Code = string.Empty;
        UnitCode = string.Empty;
        Name = null!;
    }

    private ClinicalGroup(Guid id, string code)
        : base(id)
    {
        Code = code;
        UnitCode = string.Empty;
        Name = null!;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }

    /// <summary>Unique per tenant (INV-CAT-001), immutable.</summary>
    public string Code { get; private set; }

    public string UnitCode { get; private set; }

    public LocalizedText Name { get; private set; }

    /// <summary>Heading of the group's report section.</summary>
    public string? ReportName { get; private set; }

    /// <summary>The report template of catalogue B (by id; validated by that module).</summary>
    public Guid? ReportTemplateId { get; private set; }

    public int DisplayOrder { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>T1 Create (<c>catalogue.create</c>); uniqueness is the handler's check, backed by the unique index.</summary>
    public static ClinicalGroup Create(string code, string unitCode, LocalizedText name, string? reportName, Guid? reportTemplateId, int displayOrder, DateTimeOffset nowUtc)
    {
        var group = new ClinicalGroup(Guid.CreateVersion7(), CleanCode(code));
        group.Apply(unitCode, name, reportName, reportTemplateId, displayOrder);
        group.Raise(new ClinicalGroupCreated(group.Id, group.Code, nowUtc));
        return group;
    }

    /// <summary>T2 Edit (<c>catalogue.edit</c>): everything but the code; raises an event only when something changed.</summary>
    public void Change(string unitCode, LocalizedText name, string? reportName, Guid? reportTemplateId, int displayOrder, DateTimeOffset nowUtc)
    {
        var before = (UnitCode, Name, ReportName, ReportTemplateId, DisplayOrder);
        Apply(unitCode, name, reportName, reportTemplateId, displayOrder);
        if (before != (UnitCode, Name, ReportName, ReportTemplateId, DisplayOrder))
        {
            Raise(new ClinicalGroupChanged(Id, Code, nowUtc));
        }
    }

    /// <summary>T3 Deactivate (<c>catalogue.manage</c>). Idempotent.</summary>
    public void Deactivate(DateTimeOffset nowUtc)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        Raise(new ClinicalGroupDeactivated(Id, Code, nowUtc));
    }

    /// <summary>T3 Reactivate (<c>catalogue.manage</c>). Idempotent.</summary>
    public void Reactivate(DateTimeOffset nowUtc)
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        Raise(new ClinicalGroupReactivated(Id, Code, nowUtc));
    }

    public static string CleanCode(string? code) => CatalogueAGuard.Code(code, MaxCodeLength, "clinical group");

    public static string CleanUnitCode(string? unitCode)
    {
        var cleaned = (unitCode ?? string.Empty).Trim().ToUpperInvariant();
        if (cleaned.Length is < 1 or > MaxUnitCodeLength || !cleaned.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c)))
        {
            CatalogueAGuard.Refuse(ClinicalGroupCodes.UnitCodeInvalid, $"The unit code is 1 to {MaxUnitCodeLength} letters or digits (the tube label letter).");
        }

        return cleaned;
    }

    private void Apply(string unitCode, LocalizedText name, string? reportName, Guid? reportTemplateId, int displayOrder)
    {
        ArgumentNullException.ThrowIfNull(name);
        UnitCode = CleanUnitCode(unitCode);
        Name = name;
        ReportName = CatalogueAGuard.Optional(reportName, MaxReportNameLength, "The report name");
        ReportTemplateId = reportTemplateId == Guid.Empty ? null : reportTemplateId;
        DisplayOrder = CatalogueAGuard.Range(displayOrder, 0, MaxDisplayOrder, "The display order");
    }
}
