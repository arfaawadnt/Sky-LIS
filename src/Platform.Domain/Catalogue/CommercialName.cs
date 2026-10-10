using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

/// <summary>A trade name of an antibiotic under one drug type (SA-14 "commercial names with drug type").</summary>
public sealed class CommercialName : CatalogueBRecord
{
    private CommercialName()
    {
        Name = null!;
    }

    private CommercialName(Guid id)
        : base(id)
    {
        Name = null!;
    }

    public Guid DrugTypeId { get; private set; }

    /// <summary>English name unique per tenant (INV-CTB-003).</summary>
    public LocalizedText Name { get; private set; }

    /// <summary>T1 Create (<c>catalogue.create</c>): the drug type must be active (CAT-310).</summary>
    public static CommercialName Create(DrugType drugType, LocalizedText name, DateTimeOffset nowUtc)
    {
        var commercialName = new CommercialName(Guid.CreateVersion7());
        commercialName.Update(drugType, name);
        commercialName.Raise(new CommercialNameCreated(commercialName.Id, commercialName.DrugTypeId, commercialName.Name.En, nowUtc));
        return commercialName;
    }

    /// <summary>T2 Edit (<c>catalogue.edit</c>): a newly chosen drug type must be active; keeping a drug type deactivated later is allowed.</summary>
    public void Update(DrugType drugType, LocalizedText name)
    {
        ArgumentNullException.ThrowIfNull(drugType);
        ArgumentNullException.ThrowIfNull(name);
        if (drugType.Id != DrugTypeId)
        {
            RequireActiveLink(drugType.IsActive, "drug type");
        }

        DrugTypeId = drugType.Id;
        Name = name;
    }

    protected override IDomainEvent Deactivated(DateTimeOffset nowUtc) => new CommercialNameDeactivated(Id, nowUtc);

    protected override IDomainEvent Reactivated(DateTimeOffset nowUtc) => new CommercialNameReactivated(Id, nowUtc);
}
