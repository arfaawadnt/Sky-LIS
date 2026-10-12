using Platform.Application.Abstractions.Persistence;

namespace Platform.Application.Catalogue;

/// <summary>
/// The sortable columns of the catalogue master-data registers (validation v0.5.0 B-03): the list validators refuse any
/// other key and the repositories map each key to its column. A bilingual name sorts as <c>nameEn</c> or <c>nameAr</c>,
/// whichever the page shows.
/// </summary>
public static class CatalogueSortKeys
{
    public static readonly SortKeys ClinicalGroups = new("code", "nameEn", "nameAr", "displayOrder");
    public static readonly SortKeys SampleTypes = new("code", "nameEn", "nameAr");
    public static readonly SortKeys Units = new("name");
    public static readonly SortKeys PatientConditions = new("nameEn", "nameAr");
    public static readonly SortKeys Services = new("sequence", "code", "nameEn", "nameAr");
    public static readonly SortKeys CommentTemplates = new("shortName");
    public static readonly SortKeys DrugTypes = new("nameEn", "nameAr");
    public static readonly SortKeys CommercialNames = new("nameEn", "nameAr", "drugTypeEn", "drugTypeAr");
    public static readonly SortKeys Antibiotics = new("code", "nameEn", "nameAr");
    public static readonly SortKeys Organisms = new("code", "nameEn", "nameAr");
    public static readonly SortKeys SensitivityValues = new("displayOrder", "code", "nameEn", "nameAr");
    public static readonly SortKeys Reasons = new("kind", "nameEn", "nameAr");
    public static readonly SortKeys ReportTemplates = new("nameEn", "nameAr", "type");
    public static readonly SortKeys SendingLabs = new("nameEn", "nameAr");
    public static readonly SortKeys LabTests = new("code", "nameEn", "nameAr");
    public static readonly SortKeys Preparations = new("code", "questionEn", "questionAr");
    public static readonly SortKeys Doctors = new("code", "nameEn", "nameAr", "specialty");
    public static readonly SortKeys Diagnoses = new("icdCode", "nameEn", "nameAr");
    public static readonly SortKeys ShortcutSets = new("code", "nameEn", "nameAr");
    public static readonly SortKeys TestRules = new("nameEn", "nameAr");
}
