using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

/// <summary>An organism reported by culture entry (SRS CP-56 / SA-14: code, name; LDM §3.6).</summary>
public sealed class Organism : CatalogueBRecord
{
    private Organism()
    {
        Code = string.Empty;
        Name = null!;
    }

    private Organism(Guid id, string code)
        : base(id)
    {
        Code = code;
        Name = null!;
    }

    /// <summary>Unique per tenant, immutable.</summary>
    public string Code { get; private set; }

    public LocalizedText Name { get; private set; }

    /// <summary>T1 Create (<c>catalogue.create</c>).</summary>
    public static Organism Create(string code, LocalizedText name, DateTimeOffset nowUtc)
    {
        var organism = new Organism(Guid.CreateVersion7(), CatalogueBGuard.Code(code));
        organism.Rename(name);
        organism.Raise(new OrganismCreated(organism.Id, organism.Code, organism.Name.En, nowUtc));
        return organism;
    }

    /// <summary>T2 Edit (<c>catalogue.edit</c>).</summary>
    public void Rename(LocalizedText name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    protected override IDomainEvent Deactivated(DateTimeOffset nowUtc) => new OrganismDeactivated(Id, nowUtc);

    protected override IDomainEvent Reactivated(DateTimeOffset nowUtc) => new OrganismReactivated(Id, nowUtc);
}
