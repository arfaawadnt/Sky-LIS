using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

/// <summary>A class of antimicrobial drugs (SRS CP-56 / SA-14: Penicillin, Cephalosporin, Macrolide …); commercial names belong to one.</summary>
public sealed class DrugType : CatalogueBRecord
{
    private DrugType()
    {
        Name = null!;
    }

    private DrugType(Guid id)
        : base(id)
    {
        Name = null!;
    }

    /// <summary>English name unique per tenant (INV-CTB-003).</summary>
    public LocalizedText Name { get; private set; }

    /// <summary>T1 Create (<c>catalogue.create</c>).</summary>
    public static DrugType Create(LocalizedText name, DateTimeOffset nowUtc)
    {
        var drugType = new DrugType(Guid.CreateVersion7());
        drugType.Rename(name);
        drugType.Raise(new DrugTypeCreated(drugType.Id, drugType.Name.En, nowUtc));
        return drugType;
    }

    /// <summary>T2 Edit (<c>catalogue.edit</c>).</summary>
    public void Rename(LocalizedText name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    protected override IDomainEvent Deactivated(DateTimeOffset nowUtc) => new DrugTypeDeactivated(Id, nowUtc);

    protected override IDomainEvent Reactivated(DateTimeOffset nowUtc) => new DrugTypeReactivated(Id, nowUtc);
}
