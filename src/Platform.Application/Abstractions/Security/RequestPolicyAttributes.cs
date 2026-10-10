using Platform.Domain.Authorization;

namespace Platform.Application.Abstractions.Security;

/// <summary>
/// Base of the three policy attributes a command or query may carry (D-04). Exactly one is required on every
/// <c>ICommand</c>, <c>ICommand&lt;T&gt;</c> and <c>IQuery&lt;T&gt;</c>; the build fails otherwise
/// (RequestPolicyTests) and the authorization behavior refuses with <c>AUTHZ-000</c> at runtime as a last line.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public abstract class RequestPolicyAttribute : Attribute;

/// <summary>The actor must hold the named permission in the current tenant. The key must exist in <see cref="PermissionCatalog"/>.</summary>
public sealed class RequirePermissionPolicyAttribute : RequestPolicyAttribute
{
    public RequirePermissionPolicyAttribute(string module, PermissionAction action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        Module = module;
        Action = action;
        PermissionKey = PermissionCatalog.Key(module, action);
    }

    public string Module { get; }

    public PermissionAction Action { get; }

    /// <summary>The catalogue key, <c>module.action</c>.</summary>
    public string PermissionKey { get; }
}

/// <summary>Any authenticated actor may send the request: self-service surfaces only (my session, my privileges, my profile).</summary>
public sealed class RequireAuthenticatedActorAttribute : RequestPolicyAttribute;

/// <summary>The request runs before authentication: sign-in, credential challenge, refresh. Nothing else qualifies.</summary>
public sealed class AllowUnauthenticatedAttribute : RequestPolicyAttribute;
