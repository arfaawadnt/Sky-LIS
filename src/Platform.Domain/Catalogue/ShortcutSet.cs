using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

/// <summary>
/// A named typing shortcut that drops several services into an order (SRS CP-51; LDM §3.8 "Tests Shortcuts": each
/// service keeps its own price and group, the lines stay editable after ordering). At least one service
/// (INV-CTB-005); the order of the services is the order they are added in.
/// </summary>
public sealed class ShortcutSet : CatalogueBRecord
{
    private ShortcutSet()
    {
        Code = string.Empty;
        Name = null!;
        ServiceIds = [];
    }

    private ShortcutSet(Guid id, string code)
        : base(id)
    {
        Code = code;
        Name = null!;
        ServiceIds = [];
    }

    /// <summary>What reception types (for example <c>ICU</c>); unique per tenant, immutable.</summary>
    public string Code { get; private set; }

    public LocalizedText Name { get; private set; }

    public Guid[] ServiceIds { get; private set; }

    /// <summary>T1 Create (<c>catalogue.create</c>).</summary>
    public static ShortcutSet Create(string code, LocalizedText name, IEnumerable<Guid>? serviceIds, DateTimeOffset nowUtc)
    {
        var set = new ShortcutSet(Guid.CreateVersion7(), CatalogueBGuard.Code(code));
        set.Update(name, serviceIds);
        set.Raise(new ShortcutSetCreated(set.Id, set.Code, set.Name.En, nowUtc));
        return set;
    }

    /// <summary>T2 Edit (<c>catalogue.edit</c>).</summary>
    public void Update(LocalizedText name, IEnumerable<Guid>? serviceIds)
    {
        ArgumentNullException.ThrowIfNull(name);
        var services = CatalogueBGuard.Ids(serviceIds);
        Require(services.Length >= 1, CatalogueBCodes.ShortcutSetServicesRequired, "A shortcut set needs at least one service.", DomainFault.Validation);

        Name = name;
        ServiceIds = services;
    }

    protected override IDomainEvent Deactivated(DateTimeOffset nowUtc) => new ShortcutSetDeactivated(Id, nowUtc);

    protected override IDomainEvent Reactivated(DateTimeOffset nowUtc) => new ShortcutSetReactivated(Id, nowUtc);
}
