using System.Text.Json;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.PlatformAdmin;

// The integration library of the Super Admin portal (SA-15…SA-18): platform reference data that laboratories pick
// from when they configure analysers, PCR kits and medical rules (CP-58…CP-64). No life cycle beyond active/inactive.

/// <summary>A vendor/model of analyser (SA-15); vendor + model is unique (INV-PLT-050).</summary>
public sealed class AnalyserModel : AggregateRoot
{
    public const int MaxTextLength = 100;
    public const int MaxModes = 10;
    public const int MaxModeLength = 40;

    private AnalyserModel()
    {
        Vendor = string.Empty;
        Model = string.Empty;
        Discipline = string.Empty;
        Modes = [];
    }

    private AnalyserModel(Guid id, AnalyserModelData data)
        : base(id)
    {
        Vendor = data.Vendor;
        Model = data.Model;
        Discipline = data.Discipline;
        Modes = data.Modes.ToArray();
        Apply(data);
        IsActive = true;
    }

    public string Vendor { get; private set; }

    public string Model { get; private set; }

    /// <summary>Chemistry, haematology, immunology, PCR …</summary>
    public string Discipline { get; private set; }

    public AnalyserProtocol Protocol { get; private set; }

    public PhysicalLink PhysicalLink { get; private set; }

    /// <summary>Communication modes (uni-directional, bi-directional, query …).</summary>
    public string[] Modes { get; private set; }

    public string? DriverVersion { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    public static AnalyserModel Create(AnalyserModelData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new AnalyserModel(Guid.CreateVersion7(), data.Validated());
    }

    public void Update(AnalyserModelData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var valid = data.Validated();
        Vendor = valid.Vendor;
        Model = valid.Model;
        Discipline = valid.Discipline;
        Modes = valid.Modes.ToArray();
        Apply(valid);
    }

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;

    private void Apply(AnalyserModelData data)
    {
        Protocol = data.Protocol;
        PhysicalLink = data.PhysicalLink;
        DriverVersion = data.DriverVersion;
    }
}

public sealed record AnalyserModelData(string Vendor, string Model, string Discipline, AnalyserProtocol Protocol, PhysicalLink PhysicalLink, IReadOnlyList<string> Modes, string? DriverVersion)
{
    internal AnalyserModelData Validated()
    {
        var modes = (Modes ?? []).Select(mode => (mode ?? string.Empty).Trim()).Where(mode => mode.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (modes.Count > AnalyserModel.MaxModes || modes.Any(mode => mode.Length > AnalyserModel.MaxModeLength) || !Enum.IsDefined(Protocol) || !Enum.IsDefined(PhysicalLink))
        {
            throw new DomainException(PlatformAdminCodes.ModesInvalid, $"At most {AnalyserModel.MaxModes} modes of {AnalyserModel.MaxModeLength} characters, a known protocol and a known physical link.", DomainFault.Validation);
        }

        return new AnalyserModelData(
            DomainText.Required(Vendor, AnalyserModel.MaxTextLength, PlatformAdminCodes.LibraryTextInvalid, "The vendor"),
            DomainText.Required(Model, AnalyserModel.MaxTextLength, PlatformAdminCodes.LibraryTextInvalid, "The model"),
            DomainText.Required(Discipline, AnalyserModel.MaxTextLength, PlatformAdminCodes.LibraryTextInvalid, "The discipline"),
            Protocol,
            PhysicalLink,
            modes,
            DomainText.Optional(DriverVersion, 40, PlatformAdminCodes.LibraryTextInvalid, "The driver version"));
    }
}

/// <summary>
/// A PCR kit interpretation template (SA-16, "as CP-62"): run-file layout, per-gene Ct thresholds and the verdict
/// rules, each a JSON document that must parse (LIB-003). The name is unique (INV-PLT-051); the analyser model, when
/// given, must be active (LIB-012).
/// </summary>
public sealed class PcrKitTemplate : AggregateRoot
{
    public const int MaxNameLength = 150;
    public const int MaxJsonLength = 20_000;

    private PcrKitTemplate()
    {
        Name = string.Empty;
        LayoutJson = "{}";
        ThresholdsJson = "[]";
        VerdictRulesJson = "[]";
    }

    private PcrKitTemplate(Guid id, string name, Guid? analyserModelId, string layout, string thresholds, string verdicts)
        : base(id)
    {
        Name = name;
        AnalyserModelId = analyserModelId;
        LayoutJson = layout;
        ThresholdsJson = thresholds;
        VerdictRulesJson = verdicts;
        IsActive = true;
    }

    public string Name { get; private set; }

    public Guid? AnalyserModelId { get; private set; }

    public string LayoutJson { get; private set; }

    public string ThresholdsJson { get; private set; }

    public string VerdictRulesJson { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    public static PcrKitTemplate Create(string name, AnalyserModel? analyserModel, string layoutJson, string thresholdsJson, string verdictRulesJson)
    {
        CheckModel(analyserModel);
        return new PcrKitTemplate(
            Guid.CreateVersion7(),
            DomainText.Required(name, MaxNameLength, PlatformAdminCodes.LibraryTextInvalid, "The kit name"),
            analyserModel?.Id,
            Json(layoutJson, "The run-file layout"),
            Json(thresholdsJson, "The gene thresholds"),
            Json(verdictRulesJson, "The verdict rules"));
    }

    public void Update(string name, AnalyserModel? analyserModel, string layoutJson, string thresholdsJson, string verdictRulesJson)
    {
        if (analyserModel?.Id != AnalyserModelId)
        {
            CheckModel(analyserModel);
        }

        Name = DomainText.Required(name, MaxNameLength, PlatformAdminCodes.LibraryTextInvalid, "The kit name");
        AnalyserModelId = analyserModel?.Id;
        LayoutJson = Json(layoutJson, "The run-file layout");
        ThresholdsJson = Json(thresholdsJson, "The gene thresholds");
        VerdictRulesJson = Json(verdictRulesJson, "The verdict rules");
    }

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;

    /// <summary>INV-PLT-052: the text is one JSON object or array (LIB-003); it is stored re-serialised, compact.</summary>
    public static string Json(string? text, string field)
    {
        var trimmed = (text ?? string.Empty).Trim();
        Require(trimmed.Length is > 0 and <= MaxJsonLength, PlatformAdminCodes.JsonInvalid, $"{field} is required JSON of at most {MaxJsonLength} characters.", DomainFault.Validation);
        try
        {
            using var document = JsonDocument.Parse(trimmed, new JsonDocumentOptions { MaxDepth = 32 });
            Require(document.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array, PlatformAdminCodes.JsonInvalid, $"{field} must be a JSON object or array.", DomainFault.Validation);
            return JsonSerializer.Serialize(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new DomainException(PlatformAdminCodes.JsonInvalid, $"{field} is not valid JSON: {exception.Message}", DomainFault.Validation, exception);
        }
    }

    private static void CheckModel(AnalyserModel? analyserModel)
    {
        if (analyserModel is not null)
        {
            Require(analyserModel.IsActive, PlatformAdminCodes.AnalyserModelInactive, "The analyser model is inactive.", DomainFault.Conflict);
        }
    }
}

/// <summary>
/// A version of a standard code system (SA-17): LOINC, CPT, ICD-10 or SNOMED. Name + version is unique (INV-PLT-053).
/// The codes are <see cref="CodeSystemItem"/> rows imported from CSV (upsert by code); they are not loaded with the root.
/// </summary>
public sealed class CodeSystem : AggregateRoot
{
    public const int MaxVersionLength = 40;
    public const int MaxUseLength = 300;

    private CodeSystem()
    {
        Version = string.Empty;
    }

    private CodeSystem(Guid id, CodeSystemName name, string version, string? use)
        : base(id)
    {
        Name = name;
        Version = version;
        Use = use;
        IsActive = true;
    }

    public CodeSystemName Name { get; private set; }

    public string Version { get; private set; }

    /// <summary>What the laboratories use it for (test identifiers, billing codes, diagnoses …).</summary>
    public string? Use { get; private set; }

    public int ItemCount { get; private set; }

    public DateTimeOffset? LastImportedAtUtc { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    public static CodeSystem Create(CodeSystemName name, string version, string? use)
    {
        Require(Enum.IsDefined(name), PlatformAdminCodes.LibraryTextInvalid, "The code system is LOINC, CPT, ICD-10 or SNOMED.", DomainFault.Validation);
        return new CodeSystem(
            Guid.CreateVersion7(),
            name,
            DomainText.Required(version, MaxVersionLength, PlatformAdminCodes.LibraryTextInvalid, "The version"),
            DomainText.Optional(use, MaxUseLength, PlatformAdminCodes.LibraryTextInvalid, "The use"));
    }

    public void Update(string version, string? use)
    {
        Version = DomainText.Required(version, MaxVersionLength, PlatformAdminCodes.LibraryTextInvalid, "The version");
        Use = DomainText.Optional(use, MaxUseLength, PlatformAdminCodes.LibraryTextInvalid, "The use");
    }

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;

    /// <summary>
    /// I1 Import (upsert by code): <paramref name="existing"/> are the current items keyed by code (tracked), the rows
    /// come from the file (a later duplicate wins). Returns the new items to stage; existing ones are renamed in place.
    /// </summary>
    public CodeSystemImport Import(IReadOnlyDictionary<string, CodeSystemItem> existing, IEnumerable<(string Code, string Display)> rows, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(rows);
        var added = new Dictionary<string, CodeSystemItem>(StringComparer.Ordinal);
        var updated = 0;
        var unchanged = 0;
        foreach (var (rawCode, rawDisplay) in rows)
        {
            var code = CodeSystemItem.CleanCode(rawCode);
            var display = CodeSystemItem.CleanDisplay(rawDisplay, code);
            if (existing.TryGetValue(code, out var item))
            {
                if (item.Rename(display))
                {
                    updated++;
                }
                else
                {
                    unchanged++;
                }
            }
            else if (added.TryGetValue(code, out var pending))
            {
                pending.Rename(display);
            }
            else
            {
                added[code] = CodeSystemItem.Create(Id, code, display);
            }
        }

        ItemCount = existing.Count + added.Count;
        LastImportedAtUtc = nowUtc;
        Raise(new CodeSystemImported(Id, Name.ToString(), Version, added.Count, updated, nowUtc));
        return new CodeSystemImport(added.Values.ToList(), added.Count, updated, unchanged);
    }
}

public sealed record CodeSystemImport(IReadOnlyList<CodeSystemItem> NewItems, int Inserted, int Updated, int Unchanged);

/// <summary>
/// One code of a code system (<c>saas.code_system_items</c>, key <c>(code_system_id, code)</c>). Bulk reference
/// rows: not an entity of its own, not field-audited (the import event records what changed).
/// </summary>
public sealed class CodeSystemItem
{
    public const int MaxCodeLength = 64;
    public const int MaxDisplayLength = 1000;

    private CodeSystemItem()
    {
        Code = string.Empty;
        Display = string.Empty;
    }

    private CodeSystemItem(Guid codeSystemId, string code, string display)
    {
        CodeSystemId = codeSystemId;
        Code = code;
        Display = display;
    }

    public Guid CodeSystemId { get; private set; }

    public string Code { get; private set; }

    public string Display { get; private set; }

    internal static CodeSystemItem Create(Guid codeSystemId, string code, string display) => new(codeSystemId, code, display);

    internal bool Rename(string display)
    {
        if (string.Equals(Display, display, StringComparison.Ordinal))
        {
            return false;
        }

        Display = display;
        return true;
    }

    internal static string CleanCode(string? code)
    {
        var value = (code ?? string.Empty).Trim();
        if (value.Length is 0 or > MaxCodeLength || value.Any(char.IsWhiteSpace))
        {
            throw new DomainException(PlatformAdminCodes.CodeSystemItemInvalid, $"A code is 1 to {MaxCodeLength} characters without spaces ('{Truncate(value)}').", DomainFault.Validation);
        }

        return value;
    }

    internal static string CleanDisplay(string? display, string code)
    {
        var value = (display ?? string.Empty).Trim();
        if (value.Length is 0 or > MaxDisplayLength)
        {
            throw new DomainException(PlatformAdminCodes.CodeSystemItemInvalid, $"The display of code '{code}' is required and at most {MaxDisplayLength} characters.", DomainFault.Validation);
        }

        return value;
    }

    private static string Truncate(string value) => value.Length <= 20 ? value : value[..20] + "…";
}

/// <summary>A reusable medical-rule pattern (SA-18): name (unique, INV-PLT-054), clinical group, formula and description.</summary>
public sealed class RuleTemplate : AggregateRoot
{
    public const int MaxNameLength = 150;
    public const int MaxGroupLength = 100;
    public const int MaxFormulaLength = 2000;
    public const int MaxDescriptionLength = 1000;

    private RuleTemplate()
    {
        Name = string.Empty;
        Formula = string.Empty;
    }

    private RuleTemplate(Guid id, string name, string? clinicalGroupName, string formula, string? description)
        : base(id)
    {
        Name = name;
        ClinicalGroupName = clinicalGroupName;
        Formula = formula;
        Description = description;
        IsActive = true;
    }

    public string Name { get; private set; }

    public string? ClinicalGroupName { get; private set; }

    public string Formula { get; private set; }

    public string? Description { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    public static RuleTemplate Create(string name, string? clinicalGroupName, string formula, string? description) =>
        new(
            Guid.CreateVersion7(),
            DomainText.Required(name, MaxNameLength, PlatformAdminCodes.LibraryTextInvalid, "The name"),
            DomainText.Optional(clinicalGroupName, MaxGroupLength, PlatformAdminCodes.LibraryTextInvalid, "The clinical group"),
            DomainText.Required(formula, MaxFormulaLength, PlatformAdminCodes.LibraryTextInvalid, "The formula"),
            DomainText.Optional(description, MaxDescriptionLength, PlatformAdminCodes.LibraryTextInvalid, "The description"));

    public void Update(string name, string? clinicalGroupName, string formula, string? description)
    {
        Name = DomainText.Required(name, MaxNameLength, PlatformAdminCodes.LibraryTextInvalid, "The name");
        ClinicalGroupName = DomainText.Optional(clinicalGroupName, MaxGroupLength, PlatformAdminCodes.LibraryTextInvalid, "The clinical group");
        Formula = DomainText.Required(formula, MaxFormulaLength, PlatformAdminCodes.LibraryTextInvalid, "The formula");
        Description = DomainText.Optional(description, MaxDescriptionLength, PlatformAdminCodes.LibraryTextInvalid, "The description");
    }

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;
}
