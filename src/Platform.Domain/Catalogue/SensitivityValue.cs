using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

/// <summary>An interpretation of a sensitivity result (SA-14 "sensitivity values (S/I/R)"; LDM §3.6 <c>Sensitivity</c>), offered in display order.</summary>
public sealed class SensitivityValue : CatalogueBRecord
{
    public const int MaxCodeLength = 10;
    public const int MaxDisplayOrder = 999;

    private SensitivityValue()
    {
        Code = string.Empty;
        Name = null!;
    }

    private SensitivityValue(Guid id, string code)
        : base(id)
    {
        Code = code;
        Name = null!;
    }

    /// <summary><c>S</c>, <c>I</c>, <c>R</c>, <c>SDD</c> …; unique per tenant, immutable.</summary>
    public string Code { get; private set; }

    public LocalizedText Name { get; private set; }

    /// <summary>0–999; ties order by code.</summary>
    public int DisplayOrder { get; private set; }

    /// <summary>T1 Create (<c>catalogue.create</c>).</summary>
    public static SensitivityValue Create(string code, LocalizedText name, int displayOrder, DateTimeOffset nowUtc)
    {
        var value = new SensitivityValue(Guid.CreateVersion7(), CatalogueBGuard.Code(code, MaxCodeLength));
        value.Update(name, displayOrder);
        value.Raise(new SensitivityValueCreated(value.Id, value.Code, value.Name.En, nowUtc));
        return value;
    }

    /// <summary>T2 Edit (<c>catalogue.edit</c>).</summary>
    public void Update(LocalizedText name, int displayOrder)
    {
        ArgumentNullException.ThrowIfNull(name);
        Require(displayOrder is >= 0 and <= MaxDisplayOrder, CatalogueBCodes.DisplayOrderInvalid, $"The display order must be between 0 and {MaxDisplayOrder}.", DomainFault.Validation);
        Name = name;
        DisplayOrder = displayOrder;
    }

    protected override IDomainEvent Deactivated(DateTimeOffset nowUtc) => new SensitivityValueDeactivated(Id, nowUtc);

    protected override IDomainEvent Reactivated(DateTimeOffset nowUtc) => new SensitivityValueReactivated(Id, nowUtc);
}
