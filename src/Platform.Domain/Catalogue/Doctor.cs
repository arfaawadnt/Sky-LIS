using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

/// <summary>
/// A referring doctor (SRS CP-48; LDM §4.2 <c>Doctor</c>, §7.3 <c>Doctor Income</c>). The share percentage feeds the
/// doctor-income report; the branches restrict where reception may pick the doctor (empty = every branch).
/// </summary>
public sealed class Doctor : CatalogueBRecord
{
    private Doctor()
    {
        Code = string.Empty;
        Name = null!;
        BranchIds = [];
    }

    private Doctor(Guid id, string code)
        : base(id)
    {
        Code = code;
        Name = null!;
        BranchIds = [];
    }

    /// <summary>Counter kind of the generated codes (LIS-CR-007 C-09): one never-restarting counter per tenant.</summary>
    public const string GeneratedCodeKind = "doctor";

    /// <summary>Prefix of a generated code: <c>DR-001</c>, <c>DR-002</c> … (no year).</summary>
    public const string GeneratedCodePrefix = "DR";

    /// <summary>Digits of a generated code's number (at least; <c>DR-1000</c> follows <c>DR-999</c>).</summary>
    public const int GeneratedCodeWidth = 3;

    /// <summary>The code proposed for the <paramref name="sequence"/>-th doctor of a tenant when the user leaves the code blank.</summary>
    public static string GeneratedCode(long sequence) => ReferenceNumberFormat.Short(GeneratedCodePrefix, sequence, GeneratedCodeWidth);

    /// <summary>Unique per tenant, immutable.</summary>
    public string Code { get; private set; }

    public LocalizedText Name { get; private set; }

    public string? Specialty { get; private set; }

    public string? Occupation { get; private set; }

    /// <summary>Branches (Organization) where the doctor is offered; empty = every branch.</summary>
    public Guid[] BranchIds { get; private set; }

    /// <summary>0–100 with at most two decimals (INV-CTB-007).</summary>
    public decimal SharePercent { get; private set; }

    public string? Phone { get; private set; }

    /// <summary>T1 Create (<c>catalogue.create</c>). The handler checks code uniqueness and the branches.</summary>
    public static Doctor Create(string code, LocalizedText name, string? specialty, string? occupation, IEnumerable<Guid>? branchIds, decimal sharePercent, string? phone, DateTimeOffset nowUtc)
    {
        var doctor = new Doctor(Guid.CreateVersion7(), CatalogueBGuard.Code(code));
        doctor.Update(name, specialty, occupation, branchIds, sharePercent, phone);
        doctor.Raise(new DoctorCreated(doctor.Id, doctor.Code, doctor.Name.En, nowUtc));
        return doctor;
    }

    /// <summary>T2 Edit (<c>catalogue.edit</c>): everything but the code.</summary>
    public void Update(LocalizedText name, string? specialty, string? occupation, IEnumerable<Guid>? branchIds, decimal sharePercent, string? phone)
    {
        ArgumentNullException.ThrowIfNull(name);
        Require(sharePercent is >= 0m and <= 100m && decimal.Round(sharePercent, 2) == sharePercent, CatalogueBCodes.SharePercentInvalid, "The share must be between 0 and 100 with at most two decimals.", DomainFault.Validation);

        Name = name;
        Specialty = CatalogueBGuard.OptionalText(specialty, CatalogueBGuard.MaxFreeTextLength, "specialty");
        Occupation = CatalogueBGuard.OptionalText(occupation, CatalogueBGuard.MaxFreeTextLength, "occupation");
        BranchIds = CatalogueBGuard.Ids(branchIds);
        SharePercent = sharePercent;
        Phone = CatalogueBGuard.Phone(phone);
    }

    /// <summary>Reception may pick the doctor in this branch: active, and either unrestricted or assigned to it.</summary>
    public bool IsAvailableIn(Guid branchId) => IsActive && (BranchIds.Length == 0 || BranchIds.Contains(branchId));

    protected override IDomainEvent Deactivated(DateTimeOffset nowUtc) => new DoctorDeactivated(Id, nowUtc);

    protected override IDomainEvent Reactivated(DateTimeOffset nowUtc) => new DoctorReactivated(Id, nowUtc);
}
