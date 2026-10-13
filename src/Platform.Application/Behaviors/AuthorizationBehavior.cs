using MediatR;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Behaviors;

/// <summary>
/// Deny-by-default authorization on commands and queries (D-04, 01 §3.4). Runs third in the fixed pipeline
/// order (Tracing → Logging → Authorization → Validation → Idempotency). The decision is made here from the
/// policy attribute and the per-request privileges; controllers carry no role attributes.
/// </summary>
public sealed class AuthorizationBehavior<TRequest, TResponse>(ICurrentUser currentUser, IUserPrivileges privileges)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly RequestPolicyAttribute? Policy = RequestAuthorization.PolicyOf(typeof(TRequest));

    private static readonly bool IsGated = RequestGating.IsGated(typeof(TRequest));

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (IsGated)
        {
            RequestAuthorization.Demand(typeof(TRequest), Policy, currentUser, privileges);
        }

        return next();
    }
}

/// <summary>
/// The authorization decision of one request type, shared by <see cref="AuthorizationBehavior{TRequest,TResponse}"/> and
/// the Web API's refusal of an unreadable body (validation F-17: an unauthorized caller learns nothing of a request's
/// shape, so its 403 comes before any 400).
/// </summary>
public static class RequestAuthorization
{
    /// <summary>The request type's own policy attribute, or null.</summary>
    public static RequestPolicyAttribute? PolicyOf(Type requestType)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        return (RequestPolicyAttribute?)Attribute.GetCustomAttribute(requestType, typeof(RequestPolicyAttribute), inherit: false);
    }

    /// <summary>True when the request type is open to anonymous callers (<see cref="AllowUnauthenticatedAttribute"/>).</summary>
    public static bool AllowsAnonymous(Type requestType) => PolicyOf(requestType) is AllowUnauthenticatedAttribute;

    /// <summary>Throws the refusal the pipeline gives for this request type and actor: NoPolicy, 401, unknown permission or 403.</summary>
    public static void Demand(Type requestType, RequestPolicyAttribute? policy, ICurrentUser currentUser, IUserPrivileges privileges)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(privileges);

        switch (policy)
        {
            case null:
                throw new DomainException(
                    ProblemCodes.NoPolicy,
                    $"Request '{requestType.Name}' declares no authorization policy.",
                    DomainFault.Refused);
            case AllowUnauthenticatedAttribute:
                return;
        }

        if (!currentUser.IsAuthenticated)
        {
            throw new DomainException(
                ProblemCodes.AuthenticatedActorRequired,
                "An authenticated actor is required.",
                DomainFault.Unauthenticated);
        }

        var permitted = policy switch
        {
            RequireAuthenticatedActorAttribute => true,
            RequirePermissionPolicyAttribute p when !PermissionCatalog.IsKnown(p.PermissionKey) =>
                throw new DomainException(ProblemCodes.UnknownPermission, $"Unknown permission '{p.PermissionKey}'.", DomainFault.Refused),
            RequirePermissionPolicyAttribute p => privileges.Has(p.PermissionKey),
            _ => false,
        };

        if (!permitted)
        {
            throw new DomainException(
                ProblemCodes.Forbidden,
                "This action is not permitted for the current actor.",
                DomainFault.Refused);
        }
    }
}

/// <summary>Decides whether a request type is subject to the authorization gate: every command and query is.</summary>
public static class RequestGating
{
    public static bool IsGated(Type requestType)
    {
        ArgumentNullException.ThrowIfNull(requestType);

        if (typeof(ICommand).IsAssignableFrom(requestType))
        {
            return true;
        }

        return requestType.GetInterfaces().Any(static i =>
            i.IsGenericType
            && (i.GetGenericTypeDefinition() == typeof(ICommand<>) || i.GetGenericTypeDefinition() == typeof(IQuery<>)));
    }
}
