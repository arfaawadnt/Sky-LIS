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
    private static readonly RequestPolicyAttribute? Policy =
        (RequestPolicyAttribute?)Attribute.GetCustomAttribute(typeof(TRequest), typeof(RequestPolicyAttribute), inherit: false);

    private static readonly bool IsGated = RequestGating.IsGated(typeof(TRequest));

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (!IsGated)
        {
            return next();
        }

        switch (Policy)
        {
            case null:
                throw new DomainException(
                    ProblemCodes.NoPolicy,
                    $"Request '{typeof(TRequest).Name}' declares no authorization policy.",
                    DomainFault.Refused);
            case AllowUnauthenticatedAttribute:
                return next();
        }

        if (!currentUser.IsAuthenticated)
        {
            throw new DomainException(
                ProblemCodes.AuthenticatedActorRequired,
                "An authenticated actor is required.",
                DomainFault.Unauthenticated);
        }

        var permitted = Policy switch
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

        return next();
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
