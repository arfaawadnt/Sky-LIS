using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Registration;

/// <summary>The header of a batch (T1): a group on one contract, or the samples of a sending laboratory.</summary>
public sealed record BatchHeader(
    BatchKind Kind,
    Guid PayerId,
    Guid ContractTierId,
    Guid? SendingLabId,
    Guid? ReferringDoctorId,
    string? GroupName,
    string? CourierEn,
    string? CourierAr,
    string? Notes);

/// <summary>
/// A batch registration (SRS CP-08 group, CP-09 lab-to-lab; LDM §4.1, §4.9; WF-04): one number under which one visit per
/// member is registered in the same unit of work. The members are the visits carrying <c>BatchId</c>; Billing settles a
/// group batch, Reporting claims a lab-to-lab batch from the sending lab.
/// </summary>
public sealed class Batch : AggregateRoot, ITenantScoped
{
    public const int MaxNumberLength = 20;
    public const int MaxGroupNameLength = 200;
    public const int MaxCourierLength = 200;
    public const int MaxNotesLength = 1000;
    public const int MaxMembers = 200;
    public const string NumberKind = "batch";
    public const string NumberPrefix = "BTH";

    private Batch()
    {
        Number = string.Empty;
    }

    private Batch(string number, Guid branchId, BatchHeader header, Guid actorUserId, DateTimeOffset nowUtc)
        : base(Guid.CreateVersion7())
    {
        Number = number;
        BranchId = branchId;
        Kind = header.Kind;
        PayerId = header.PayerId;
        ContractTierId = header.ContractTierId;
        SendingLabId = header.SendingLabId;
        ReferringDoctorId = header.ReferringDoctorId;
        GroupName = Blank(header.GroupName);
        CourierEn = Blank(header.CourierEn);
        CourierAr = Blank(header.CourierAr);
        Notes = Blank(header.Notes);
        RegisteredAtUtc = nowUtc;
        RegisteredByUserId = actorUserId;
    }

    public Guid TenantId { get; private set; }

    public string Number { get; private set; }

    public BatchKind Kind { get; private set; }

    public Guid BranchId { get; private set; }

    public Guid PayerId { get; private set; }

    public Guid ContractTierId { get; private set; }

    public Guid? SendingLabId { get; private set; }

    public Guid? ReferringDoctorId { get; private set; }

    /// <summary>The company or group (CP-08).</summary>
    public string? GroupName { get; private set; }

    public string? CourierEn { get; private set; }

    public string? CourierAr { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset RegisteredAtUtc { get; private set; }

    public Guid RegisteredByUserId { get; private set; }

    public int MemberCount { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>T1 Register (<c>registration.create</c>): the header; the handler registers one visit per member with this batch's id.</summary>
    public static Batch Register(string number, Guid branchId, BatchHeader header, int memberCount, Guid actorUserId, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(header);
        var cleanedNumber = (number ?? string.Empty).Trim();
        Require(cleanedNumber.Length is >= 1 and <= MaxNumberLength, RegistrationCodes.HeaderInvalid, "A batch needs a number.", DomainFault.Validation);
        Require(branchId != Guid.Empty, RegistrationCodes.HeaderInvalid, "A batch needs the session branch.", DomainFault.Validation);
        Require(header.PayerId != Guid.Empty && header.ContractTierId != Guid.Empty, RegistrationCodes.BatchContractRequired, "A batch needs a payer and a contract tier.", DomainFault.Validation);
        if (header.Kind == BatchKind.LabToLab)
        {
            Require(header.SendingLabId is { } lab && lab != Guid.Empty, RegistrationCodes.SendingLabInvalid, "A lab-to-lab batch needs the sending laboratory.", DomainFault.Validation);
        }
        else
        {
            Require(!string.IsNullOrWhiteSpace(header.GroupName), RegistrationCodes.BatchGroupNameRequired, "A group batch needs the company or group name.", DomainFault.Validation);
            Require(header.SendingLabId is null, RegistrationCodes.SendingLabInvalid, "A group batch has no sending laboratory.", DomainFault.Validation);
        }

        Require((header.GroupName?.Trim().Length ?? 0) <= MaxGroupNameLength, RegistrationCodes.BatchGroupNameRequired, $"The group name has at most {MaxGroupNameLength} characters.", DomainFault.Validation);
        Require((header.Notes?.Trim().Length ?? 0) <= MaxNotesLength, RegistrationCodes.HeaderInvalid, $"Notes have at most {MaxNotesLength} characters.", DomainFault.Validation);
        RequireMembers(memberCount, 0);

        var batch = new Batch(cleanedNumber, branchId, header, actorUserId, nowUtc) { MemberCount = memberCount };
        batch.Raise(new BatchRegistered(batch.Id, batch.Number, batch.Kind.ToString(), memberCount, actorUserId, nowUtc));
        return batch;
    }

    /// <summary>T2 Add members (<c>registration.create</c>).</summary>
    public void AddMembers(int count, Guid actorUserId, DateTimeOffset nowUtc)
    {
        RequireMembers(count, MemberCount);
        MemberCount += count;
        Raise(new BatchMembersAdded(Id, Number, count, actorUserId, nowUtc));
    }

    private static void RequireMembers(int count, int existing) =>
        Require(count is >= 1 and <= MaxMembers && existing + count <= MaxMembers * 10, RegistrationCodes.BatchMembersInvalid, $"Add 1 to {MaxMembers} members at a time.", DomainFault.Validation);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
