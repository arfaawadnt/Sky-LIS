using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

/// <summary>Where a reason is offered (SRS CP-53): sample rejection, cancellation, discount, refund, money transfer.</summary>
public enum ReasonKind
{
    Rejection = 0,
    Cancellation = 1,
    Discount = 2,
    Refund = 3,
    Transfer = 4,
}

/// <summary>
/// A reason of a reason list (SRS CP-53; LDM §5.9 rejection reasons, §7.2 cancellation/refund/discount reasons).
/// Other modules record the reason with the transaction (BR-15) and offer only active reasons of the matching kind.
/// The kind never changes: history keeps its meaning.
/// </summary>
public sealed class Reason : CatalogueBRecord
{
    private Reason()
    {
        Name = null!;
    }

    private Reason(Guid id, ReasonKind kind)
        : base(id)
    {
        Kind = kind;
        Name = null!;
    }

    public ReasonKind Kind { get; private set; }

    /// <summary>English name unique per tenant and kind (INV-CTB-003).</summary>
    public LocalizedText Name { get; private set; }

    /// <summary>T1 Create (<c>catalogue.create</c>).</summary>
    public static Reason Create(ReasonKind kind, LocalizedText name, DateTimeOffset nowUtc)
    {
        Require(Enum.IsDefined(kind), CatalogueBCodes.TextInvalid, "The reason kind is unknown.", DomainFault.Validation);
        var reason = new Reason(Guid.CreateVersion7(), kind);
        reason.Rename(name);
        reason.Raise(new ReasonCreated(reason.Id, reason.Kind, reason.Name.En, nowUtc));
        return reason;
    }

    /// <summary>T2 Edit (<c>catalogue.edit</c>): the name only.</summary>
    public void Rename(LocalizedText name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    protected override IDomainEvent Deactivated(DateTimeOffset nowUtc) => new ReasonDeactivated(Id, Kind, nowUtc);

    protected override IDomainEvent Reactivated(DateTimeOffset nowUtc) => new ReasonReactivated(Id, Kind, nowUtc);
}
