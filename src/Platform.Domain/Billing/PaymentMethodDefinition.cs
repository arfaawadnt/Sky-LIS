using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Billing;

/// <summary>
/// A payment method the laboratory offers at its cash desks (Sky LIS set-up list CP-30b; change request 2026-10-19,
/// LIS-CR-003 P-C10): a code, bilingual names, the <see cref="PaymentMethod"/> kind that carries the money semantics
/// (cash lives in the treasury drawer, the closing and the collections report group by kind), whether a transaction
/// reference is required, the order of the pick lists and the active flag. Reference-like aggregate: created from the
/// defaults or by the laboratory, edited, deactivated instead of deleted (history keeps the posted code). The laboratory
/// always keeps at least one active method of kind Cash (INV-BIL-010, BIL-052), so a cash desk can always take cash.
/// </summary>
public sealed class PaymentMethodDefinition : AggregateRoot, ITenantScoped
{
    public const int MaxCodeLength = 20;
    public const int MaxNameLength = 100;
    public const int MaxSequence = 9999;

    private PaymentMethodDefinition()
    {
        Code = string.Empty;
        Name = null!;
    }

    private PaymentMethodDefinition(string code, LocalizedText name, PaymentMethod kind, bool requiresReference, int sequence)
        : base(Guid.CreateVersion7())
    {
        Code = code;
        Name = name;
        Kind = kind;
        RequiresReference = requiresReference;
        Sequence = sequence;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }

    /// <summary>Upper-case key, unique per tenant, immutable (the posted payments and refunds carry it).</summary>
    public string Code { get; private set; }

    public LocalizedText Name { get; private set; }

    /// <summary>The money semantics: Cash needs the treasury drawer; Card / Pos / Transfer are not in the drawer.</summary>
    public PaymentMethod Kind { get; private set; }

    /// <summary>A transaction number (card slip, POS or transfer reference) is mandatory on every movement with this method (BIL-003).</summary>
    public bool RequiresReference { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>The order of the pick lists (lowest first; ties by code).</summary>
    public int Sequence { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>
    /// The four methods every laboratory starts with (seeded for existing tenants by the migration, for new tenants by
    /// <c>SeedPaymentMethodsPolicy</c>, and on demand by the defaults endpoint): CASH, CARD, POS and TRANSFER.
    /// </summary>
    public static IReadOnlyList<DefaultPaymentMethod> DefaultMethods { get; } =
    [
        new("CASH", "Cash", "نقدي", PaymentMethod.Cash, RequiresReference: false, Sequence: 1),
        new("CARD", "Card", "بطاقة", PaymentMethod.Card, RequiresReference: true, Sequence: 2),
        new("POS", "POS", "نقطة بيع", PaymentMethod.Pos, RequiresReference: true, Sequence: 3),
        new("TRANSFER", "Bank transfer", "تحويل بنكي", PaymentMethod.Transfer, RequiresReference: true, Sequence: 4),
    ];

    /// <summary>The default definitions as new aggregates (one <see cref="PaymentMethodDefined"/> each).</summary>
    public static IReadOnlyList<PaymentMethodDefinition> Defaults(DateTimeOffset nowUtc) =>
        DefaultMethods.Select(method => Define(method.Code, new LocalizedText(method.NameEn, method.NameAr), method.Kind, method.RequiresReference, method.Sequence, nowUtc)).ToList();

    /// <summary>T10 Define (<c>billing.manage</c>). Code uniqueness is checked by the handler (BIL-121) and enforced by the database.</summary>
    public static PaymentMethodDefinition Define(string code, LocalizedText name, PaymentMethod kind, bool requiresReference, int sequence, DateTimeOffset nowUtc)
    {
        var definition = new PaymentMethodDefinition(CleanCode(code), RequireName(name), RequireKind(kind), requiresReference, RequireSequence(sequence));
        definition.Raise(new PaymentMethodDefined(definition.Id, definition.Code, definition.Kind.ToString(), nowUtc));
        return definition;
    }

    /// <summary>
    /// T11 Update (<c>billing.manage</c>): names, kind, reference rule and order; the code never changes. Re-kinding the
    /// last active Cash method away from Cash is refused (BIL-052) unless another active Cash method exists
    /// (<paramref name="otherActiveCashExists"/>, resolved by the handler). Raises <see cref="PaymentMethodUpdated"/> only
    /// when something changed, so the audit trail records real edits.
    /// </summary>
    public void Update(LocalizedText name, PaymentMethod kind, bool requiresReference, int sequence, bool otherActiveCashExists, DateTimeOffset nowUtc)
    {
        var cleanedName = RequireName(name);
        var cleanedKind = RequireKind(kind);
        var cleanedSequence = RequireSequence(sequence);
        if (IsActive && Kind == PaymentMethod.Cash && cleanedKind != PaymentMethod.Cash)
        {
            Require(otherActiveCashExists, BillingCodes.LastActiveCashMethod, "The laboratory keeps at least one active cash method.", DomainFault.Conflict);
        }

        if (Name == cleanedName && Kind == cleanedKind && RequiresReference == requiresReference && Sequence == cleanedSequence)
        {
            return;
        }

        Name = cleanedName;
        Kind = cleanedKind;
        RequiresReference = requiresReference;
        Sequence = cleanedSequence;
        Raise(new PaymentMethodUpdated(Id, Code, nowUtc));
    }

    /// <summary>T12 Deactivate (<c>billing.manage</c>). Idempotent. The last active Cash method stays (BIL-052); posted movements keep the code.</summary>
    public void Deactivate(bool otherActiveCashExists, DateTimeOffset nowUtc)
    {
        if (!IsActive)
        {
            return;
        }

        if (Kind == PaymentMethod.Cash)
        {
            Require(otherActiveCashExists, BillingCodes.LastActiveCashMethod, "The laboratory keeps at least one active cash method.", DomainFault.Conflict);
        }

        IsActive = false;
        Raise(new PaymentMethodDeactivated(Id, Code, nowUtc));
    }

    /// <summary>T13 Reactivate (<c>billing.manage</c>). Idempotent.</summary>
    public void Reactivate(DateTimeOffset nowUtc)
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        Raise(new PaymentMethodReactivated(Id, Code, nowUtc));
    }

    /// <summary>How a movement uses this definition: its kind, its code and its reference rule.</summary>
    public PaymentMethodUse AsUse() => new(Kind, Code, RequiresReference);

    /// <summary>Upper-case, 1 to 20 characters: letters, digits, '-' or '_' (BIL-051).</summary>
    public static string CleanCode(string code)
    {
        var value = (code ?? string.Empty).Trim().ToUpperInvariant();
        Require(
            value.Length is >= 1 and <= MaxCodeLength && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'),
            BillingCodes.MethodCodeInvalid,
            $"The code must be 1 to {MaxCodeLength} characters: letters, digits, '-' or '_'.",
            DomainFault.Validation);
        return value;
    }

    private static LocalizedText RequireName(LocalizedText name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Require(name.En.Length <= MaxNameLength && name.Ar.Length <= MaxNameLength, BillingCodes.TextTooLong, $"A name may have at most {MaxNameLength} characters.", DomainFault.Validation);
        return name;
    }

    private static PaymentMethod RequireKind(PaymentMethod kind)
    {
        Require(Enum.IsDefined(kind), BillingCodes.MethodKindInvalid, "The kind is Cash, Card, Pos or Transfer.", DomainFault.Validation);
        return kind;
    }

    private static int RequireSequence(int sequence)
    {
        Require(sequence is >= 0 and <= MaxSequence, BillingCodes.MethodSequenceInvalid, $"The sequence is between 0 and {MaxSequence}.", DomainFault.Validation);
        return sequence;
    }
}

/// <summary>One of the methods every laboratory starts with (<see cref="PaymentMethodDefinition.DefaultMethods"/>).</summary>
public sealed record DefaultPaymentMethod(string Code, string NameEn, string NameAr, PaymentMethod Kind, bool RequiresReference, int Sequence);
