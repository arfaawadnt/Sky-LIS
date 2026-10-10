using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

/// <summary>
/// A result unit (SRS CP-44; LDM "Results Units"): a language-neutral symbol such as <c>mg/dL</c>, unique per tenant
/// ignoring case (INV-CAT-002, through <see cref="NameKey"/>). Never deleted (a unit referenced by tests stays).
/// </summary>
public sealed class MeasurementUnit : AggregateRoot, ITenantScoped
{
    public const int MaxNameLength = 50;

    private MeasurementUnit()
    {
        Name = string.Empty;
        NameKey = string.Empty;
    }

    private MeasurementUnit(Guid id, string name)
        : base(id)
    {
        Name = name;
        NameKey = KeyOf(name);
        IsActive = true;
    }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; }

    /// <summary>The upper-cased name: the unique index key that makes names unique ignoring case.</summary>
    public string NameKey { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>T7 Create (<c>catalogue.create</c>).</summary>
    public static MeasurementUnit Create(string name, DateTimeOffset nowUtc)
    {
        var unit = new MeasurementUnit(Guid.CreateVersion7(), CleanName(name));
        unit.Raise(new MeasurementUnitCreated(unit.Id, unit.Name, nowUtc));
        return unit;
    }

    /// <summary>T7 Edit (<c>catalogue.edit</c>).</summary>
    public void Rename(string name, DateTimeOffset nowUtc)
    {
        var cleaned = CleanName(name);
        if (string.Equals(cleaned, Name, StringComparison.Ordinal))
        {
            return;
        }

        Name = cleaned;
        NameKey = KeyOf(cleaned);
        Raise(new MeasurementUnitChanged(Id, Name, nowUtc));
    }

    public void Deactivate(DateTimeOffset nowUtc)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        Raise(new MeasurementUnitDeactivated(Id, Name, nowUtc));
    }

    public void Reactivate(DateTimeOffset nowUtc)
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        Raise(new MeasurementUnitReactivated(Id, Name, nowUtc));
    }

    public static string CleanName(string? name)
    {
        var cleaned = (name ?? string.Empty).Trim();
        if (cleaned.Length is < 1 or > MaxNameLength)
        {
            CatalogueAGuard.Refuse(MeasurementUnitCodes.NameInvalid, $"The unit name is required and holds at most {MaxNameLength} characters.");
        }

        return cleaned;
    }

    public static string KeyOf(string name) => (name ?? string.Empty).Trim().ToUpperInvariant();
}

/// <summary>Refusal codes of <see cref="MeasurementUnit"/> (intake LIS-INT-CAT-001 §7, T7).</summary>
public static class MeasurementUnitCodes
{
    public const string NameInvalid = "CAT-015";
    public const string NameExists = "CAT-102";
    public const string NotFound = "CAT-192";
}
