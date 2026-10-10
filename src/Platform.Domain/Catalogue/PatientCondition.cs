using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

/// <summary>
/// A patient condition that switches reference ranges (SRS CP-45, BR-08; LDM §2.5: pregnant, diabetic, smoker…).
/// Chosen at registration; range bands may target one. The English name is unique per tenant ignoring case.
/// </summary>
public sealed class PatientCondition : AggregateRoot, ITenantScoped
{
    private PatientCondition()
    {
        Name = null!;
        NameKey = string.Empty;
    }

    private PatientCondition(Guid id, LocalizedText name)
        : base(id)
    {
        Name = name;
        NameKey = KeyOf(name);
        IsActive = true;
    }

    public Guid TenantId { get; private set; }

    public LocalizedText Name { get; private set; }

    /// <summary>The upper-cased English name: the unique index key (INV-CAT-002).</summary>
    public string NameKey { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>T8 Create (<c>catalogue.create</c>).</summary>
    public static PatientCondition Create(LocalizedText name, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(name);
        var condition = new PatientCondition(Guid.CreateVersion7(), name);
        condition.Raise(new PatientConditionCreated(condition.Id, name.En, nowUtc));
        return condition;
    }

    /// <summary>T8 Edit (<c>catalogue.edit</c>).</summary>
    public void Rename(LocalizedText name, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name == Name)
        {
            return;
        }

        Name = name;
        NameKey = KeyOf(name);
        Raise(new PatientConditionChanged(Id, name.En, nowUtc));
    }

    public void Deactivate(DateTimeOffset nowUtc)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        Raise(new PatientConditionDeactivated(Id, Name.En, nowUtc));
    }

    public void Reactivate(DateTimeOffset nowUtc)
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        Raise(new PatientConditionReactivated(Id, Name.En, nowUtc));
    }

    public static string KeyOf(LocalizedText name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return KeyOf(name.En);
    }

    public static string KeyOf(string nameEn) => (nameEn ?? string.Empty).Trim().ToUpperInvariant();
}

/// <summary>Refusal codes of <see cref="PatientCondition"/> (intake LIS-INT-CAT-001 §7, T8).</summary>
public static class PatientConditionCodes
{
    public const string NameExists = "CAT-103";
    public const string NotFound = "CAT-193";
}
