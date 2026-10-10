using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Organization;

// Past-tense facts (intake LIS-INT-ORG-001 §11); ids and the few values a reader needs. The outbox stamps the tenant.

public sealed record RoleCreated(Guid RoleId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>
/// A privilege-affecting change of a role (BR-17): permissions, special rights, result rights, activation. The
/// handler that commits it evicts the cached principals of the role's members (02 §4.4). <paramref name="Aspect"/>
/// names what changed (<see cref="RoleChangeAspects"/>).
/// </summary>
public sealed record RoleChanged(Guid RoleId, DateTimeOffset OccurredAtUtc, string? Aspect = null) : DomainEvent(OccurredAtUtc);

/// <summary>The values of <see cref="RoleChanged.Aspect"/>.</summary>
public static class RoleChangeAspects
{
    public const string Permissions = "permissions";
    public const string SpecialRights = "special-rights";
    public const string ResultRights = "result-rights";
    public const string Deactivated = "deactivated";
    public const string Activated = "activated";
}

public sealed record BranchCreated(Guid BranchId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record BranchDeactivated(Guid BranchId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record BranchReactivated(Guid BranchId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
