using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Tenancy;

public sealed record TenantMembershipCreated(Guid TenantId, Guid MembershipId, Guid PlatformUserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TenantMembershipDeactivated(Guid TenantId, Guid MembershipId, Guid PlatformUserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TenantMembershipRoleChanged(Guid TenantId, Guid MembershipId, Guid PlatformUserId, Guid? RoleId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TenantMembershipReactivated(Guid TenantId, Guid MembershipId, Guid PlatformUserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>BR-17: this user's privileges now differ from the role's (added/removed keys, special rights); only this user is affected.</summary>
public sealed record TenantMembershipPrivilegesOverridden(Guid TenantId, Guid MembershipId, Guid PlatformUserId, int AddedCount, int RemovedCount, bool SpecialRightsOverridden, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>BR-17 "Restore": the user's overrides were dropped; the user inherits the role again.</summary>
public sealed record TenantMembershipRestoredToRole(Guid TenantId, Guid MembershipId, Guid PlatformUserId, Guid? RoleId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
