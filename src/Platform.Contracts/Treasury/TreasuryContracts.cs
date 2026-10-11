namespace Platform.Contracts.Treasury;

// Request and response shapes of the Treasury module. Validation here is shape only; rules live in the aggregates.

public sealed record CreateTreasuryRequest(string Code, string NameEn, string NameAr, Guid BranchId);

public sealed record RenameTreasuryRequest(string NameEn, string NameAr);

/// <summary>A treasury with its branch code and names from the Organization module, so every reader sees <c>CODE · name</c> without <c>organization.view</c> (UAT A-04).</summary>
public sealed record TreasuryListItem(Guid Id, string Code, string NameEn, string NameAr, Guid BranchId, bool IsActive, string? BranchCode, string? BranchNameEn, string? BranchNameAr);

public sealed record RequestTreasuryAccessRequest(Guid TreasuryId, string DeviceId, string? DeviceLabel);

public sealed record ApproveTreasuryAccessRequest(string? Comment);

public sealed record RejectTreasuryAccessRequest(string Reason);

public sealed record RevokeTreasuryAccessRequest(string Reason);

public sealed record CancelTreasuryAccessRequest(string? Reason);

/// <summary>Status names as stored and filtered: Pending, Approved, Rejected, Revoked, Cancelled.</summary>
public sealed record TreasuryAccessRequestListItem(
    Guid Id,
    string Number,
    Guid TreasuryId,
    string TreasuryCode,
    string TreasuryNameEn,
    string TreasuryNameAr,
    Guid BranchId,
    Guid RequestedByUserId,
    string RequestedByName,
    string DeviceId,
    string? DeviceLabel,
    string Status,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? DecidedAtUtc,
    string? DecidedByName);

public sealed record ApprovalView(Guid ActorUserId, string ActorName, DateTimeOffset AtUtc, string? Comment);

public sealed record TreasuryAccessRequestDetail(
    Guid Id,
    string Number,
    Guid TreasuryId,
    string TreasuryCode,
    string TreasuryNameEn,
    string TreasuryNameAr,
    Guid BranchId,
    Guid RequestedByUserId,
    string RequestedByName,
    string DeviceId,
    string? DeviceLabel,
    string Status,
    DateTimeOffset RequestedAtUtc,
    ApprovalView? Approval,
    Guid? DecidedByUserId,
    string? DecidedByName,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionReason);
