using Platform.Application.Abstractions.CredentialTransport;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Application.Identity.Commands;
using Platform.Contracts.Common;
using Platform.Contracts.Identity;
using Platform.Domain.Identity;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Identity.Queries;

/// <summary>Issues the challenge a client seals its credentials against (02 §3.3). Anonymous by design; rate-limited per client address.</summary>
[AllowUnauthenticated]
public sealed record IssueCredentialChallengeQuery : IQuery<CredentialChallenge>;

public sealed class IssueCredentialChallengeQueryHandler(ICredentialChallengeIssuer issuer) : IQueryHandler<IssueCredentialChallengeQuery, CredentialChallenge>
{
    public Task<CredentialChallenge> Handle(IssueCredentialChallengeQuery query, CancellationToken cancellationToken) => issuer.IssueAsync(cancellationToken);
}

/// <summary>The current session, resolved per request (02 §4.4). Self-service: any authenticated actor may read their own session.</summary>
[RequireAuthenticatedActor]
public sealed record GetMySessionQuery : IQuery<SessionResponse>;

public sealed class GetMySessionQueryHandler(ICurrentUser currentUser, ISessionPrincipalQuery principals) : IQueryHandler<GetMySessionQuery, SessionResponse>
{
    public async Task<SessionResponse> Handle(GetMySessionQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || currentUser.SessionId is not { } sessionId)
        {
            throw new DomainException(ProblemCodes.AuthenticatedActorRequired, "A session-bound actor is required.", DomainFault.Unauthenticated);
        }

        var principal = await principals.ResolveAsync(sessionId, userId, cancellationToken)
            ?? throw new DomainException(ProblemCodes.TokenSessionMismatch, "The session could not be resolved.", DomainFault.Unauthenticated);

        return Profiles.ToSession(principal);
    }
}
