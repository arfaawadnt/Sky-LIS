using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Treasury;

/// <summary>
/// A cash drawer of a branch (SRS CP-28). Reference-like aggregate: created by the lab administrator, renamed,
/// deactivated instead of deleted (deactivation refuses nothing that already exists; payments check the active
/// flag at the moment of a transaction). The tenant is stamped by the persistence layer from the current scope.
/// </summary>
public sealed class Treasury : AggregateRoot, ITenantScoped
{
    public const int MaxCodeLength = 16;

    private Treasury()
    {
        Code = string.Empty;
        Name = null!;
    }

    private Treasury(Guid id, string code, LocalizedText name, Guid branchId)
        : base(id)
    {
        Code = code;
        Name = name;
        BranchId = branchId;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }

    /// <summary>Short upper-case code, unique per tenant (INV-TRS-001).</summary>
    public string Code { get; private set; }

    public LocalizedText Name { get; private set; }

    public Guid BranchId { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>T6 Create (<c>treasury.manage</c>). Code uniqueness is checked by the handler against the repository and enforced by the database.</summary>
    public static Treasury Create(string code, LocalizedText name, Guid branchId, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(name);
        Require(branchId != Guid.Empty, TreasuryCodes.BranchRequired, "A treasury belongs to a branch.", DomainFault.Validation);

        var treasury = new Treasury(Guid.CreateVersion7(), CleanCode(code), name, branchId);
        treasury.Raise(new TreasuryCreated(treasury.Id, treasury.Code, branchId, nowUtc));
        return treasury;
    }

    /// <summary>T7 Rename (<c>treasury.manage</c>); allowed in both states.</summary>
    public void Rename(LocalizedText name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    /// <summary>T8 Deactivate (<c>treasury.manage</c>). Idempotent. Open access requests are not touched; new ones are refused (TRS-010).</summary>
    public void Deactivate(DateTimeOffset nowUtc)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        Raise(new TreasuryDeactivated(Id, Code, nowUtc));
    }

    /// <summary>T9 Reactivate (<c>treasury.manage</c>). Idempotent.</summary>
    public void Reactivate(DateTimeOffset nowUtc)
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        Raise(new TreasuryReactivated(Id, Code, nowUtc));
    }

    public static string CleanCode(string code)
    {
        var value = (code ?? string.Empty).Trim().ToUpperInvariant();
        Require(
            value.Length is >= 1 and <= MaxCodeLength && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'),
            TreasuryCodes.CodeInvalid,
            $"The code must be 1 to {MaxCodeLength} characters: letters, digits, '-' or '_'.",
            DomainFault.Validation);
        return value;
    }
}
