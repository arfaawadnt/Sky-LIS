using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

/// <summary>An antibiotic of the sensitivity panel (SRS CP-56 / SA-14: code, name, abbreviation, commercial name; LDM §3.6 culture set-up).</summary>
public sealed class Antibiotic : CatalogueBRecord
{
    public const int MaxAbbreviationLength = 10;

    private Antibiotic()
    {
        Code = string.Empty;
        Name = null!;
        Abbreviation = string.Empty;
    }

    private Antibiotic(Guid id, string code)
        : base(id)
    {
        Code = code;
        Name = null!;
        Abbreviation = string.Empty;
    }

    /// <summary>Unique per tenant, immutable.</summary>
    public string Code { get; private set; }

    public LocalizedText Name { get; private set; }

    /// <summary>The short form printed on the sensitivity table (for example <c>AMC</c>).</summary>
    public string Abbreviation { get; private set; }

    public Guid? CommercialNameId { get; private set; }

    /// <summary>T1 Create (<c>catalogue.create</c>): a linked commercial name must be active (CAT-310).</summary>
    public static Antibiotic Create(string code, LocalizedText name, string abbreviation, CommercialName? commercialName, DateTimeOffset nowUtc)
    {
        var antibiotic = new Antibiotic(Guid.CreateVersion7(), CatalogueBGuard.Code(code));
        antibiotic.Update(name, abbreviation, commercialName);
        antibiotic.Raise(new AntibioticCreated(antibiotic.Id, antibiotic.Code, antibiotic.Name.En, nowUtc));
        return antibiotic;
    }

    /// <summary>T2 Edit (<c>catalogue.edit</c>): everything but the code.</summary>
    public void Update(LocalizedText name, string abbreviation, CommercialName? commercialName)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (commercialName is not null && commercialName.Id != CommercialNameId)
        {
            RequireActiveLink(commercialName.IsActive, "commercial name");
        }

        Name = name;
        Abbreviation = CatalogueBGuard.Code(abbreviation, MaxAbbreviationLength);
        CommercialNameId = commercialName?.Id;
    }

    protected override IDomainEvent Deactivated(DateTimeOffset nowUtc) => new AntibioticDeactivated(Id, nowUtc);

    protected override IDomainEvent Reactivated(DateTimeOffset nowUtc) => new AntibioticReactivated(Id, nowUtc);
}
