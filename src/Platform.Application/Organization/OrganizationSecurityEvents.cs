namespace Platform.Application.Organization;

/// <summary>Administration events of the security catalogue (02 §7) written by this module after each commit; the detail never carries credential material.</summary>
public static class OrganizationSecurityEvents
{
    /// <summary>A role's permissions, special rights or result rights changed; detail <c>role:&lt;id&gt;:&lt;aspect&gt;</c>.</summary>
    public const string PrivilegeMatrixChanged = "PRIVILEGE_MATRIX_CHANGED";

    /// <summary>A member's role, branches, overrides, lock or password changed; detail <c>user:&lt;id&gt;:&lt;what&gt;</c>.</summary>
    public const string MembershipChanged = "MEMBERSHIP_CHANGED";

    /// <summary>A membership was deactivated; detail <c>user:&lt;id&gt;</c>.</summary>
    public const string UserDeactivated = "USER_DEACTIVATED";
}
