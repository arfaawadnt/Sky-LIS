using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;
using Platform.Domain.Treasury;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Treasury.Commands;

// Treasury access requests (T1–T5). Validators check shape; the aggregate decides (pitfall P-10).

[RequirePermissionPolicy(PermissionCatalog.Treasury, PermissionAction.Create)]
public sealed record RequestTreasuryAccessCommand(Guid TreasuryId, string DeviceId, string? DeviceLabel) : ICommand<CreatedResource>;

public sealed class RequestTreasuryAccessValidator : AbstractValidator<RequestTreasuryAccessCommand>
{
    public RequestTreasuryAccessValidator()
    {
        RuleFor(command => command.TreasuryId).NotEmpty();
        RuleFor(command => command.DeviceId).NotEmpty().MaximumLength(TreasuryAccessRequest.MaxDeviceIdLength);
        RuleFor(command => command.DeviceLabel).MaximumLength(TreasuryAccessRequest.MaxDeviceLabelLength);
    }
}

/// <summary>
/// T1. The treasury must belong to a branch the requester may work in (intake Q3, validation v0.5.0 O-4: TRS-015); the
/// assignment comes from the session principal resolved for this request (02 §4.4), never from the request body.
/// </summary>
public sealed class RequestTreasuryAccessHandler(
    ITreasuryRepository treasuries,
    ITreasuryAccessRequestRepository requests,
    IReferenceNumbers numbers,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    ISessionPrincipalQuery principals,
    IClock clock) : ICommandHandler<RequestTreasuryAccessCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(RequestTreasuryAccessCommand command, CancellationToken cancellationToken)
    {
        var actor = Actor.Of(user);
        var treasury = await treasuries.GetForUpdateAsync(command.TreasuryId, cancellationToken)
            ?? throw new DomainException(TreasuryCodes.TreasuryNotFound, "Treasury not found.", DomainFault.NotFound);

        TreasuryAccessRequest.EnsureRequesterWorksIn(treasury, await AssignedBranchesAsync(actor, cancellationToken));

        // INV-TRS-002 decided here with its code; the partial unique index is the backstop.
        if (await requests.LiveRequestExistsAsync(actor, treasury.Id, command.DeviceId.Trim(), cancellationToken))
        {
            throw new DomainException(TreasuryCodes.LiveRequestExists, "A pending or approved request already exists for this treasury on this device.", DomainFault.Conflict);
        }

        var now = clock.UtcNow;
        var number = await numbers.NextAsync(TreasuryAccessRequest.NumberKind, TreasuryAccessRequest.NumberPrefix, now, cancellationToken);
        var request = TreasuryAccessRequest.Request(treasury, number, actor, command.DeviceId, command.DeviceLabel, now);
        requests.Add(request);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(request.Id);
    }

    /// <summary>The membership's branches as stored (empty = every branch of the laboratory).</summary>
    private async Task<IReadOnlyList<Guid>> AssignedBranchesAsync(Guid actor, CancellationToken cancellationToken)
    {
        if (user.SessionId is not { } sessionId)
        {
            throw new DomainException(ProblemCodes.AuthenticatedActorRequired, "A session-bound actor is required.", DomainFault.Unauthenticated);
        }

        var principal = await principals.ResolveAsync(sessionId, actor, cancellationToken)
            ?? throw new DomainException(ProblemCodes.TokenSessionMismatch, "The session could not be resolved.", DomainFault.Unauthenticated);
        return principal.BranchIds;
    }
}

[RequirePermissionPolicy(PermissionCatalog.Treasury, PermissionAction.Approve)]
public sealed record ApproveTreasuryAccessCommand(Guid RequestId, string? Comment) : ICommand;

public sealed class ApproveTreasuryAccessValidator : AbstractValidator<ApproveTreasuryAccessCommand>
{
    public ApproveTreasuryAccessValidator()
    {
        RuleFor(command => command.RequestId).NotEmpty();
        RuleFor(command => command.Comment).MaximumLength(Approval.MaxCommentLength);
    }
}

public sealed class ApproveTreasuryAccessHandler(ITreasuryAccessRequestRepository requests, IUnitOfWork unitOfWork, ICurrentUser user, IClock clock) : ICommandHandler<ApproveTreasuryAccessCommand>
{
    public async Task Handle(ApproveTreasuryAccessCommand command, CancellationToken cancellationToken)
    {
        var actor = Actor.Of(user);
        var request = await requests.GetForUpdateAsync(command.RequestId, cancellationToken)
            ?? throw new DomainException(TreasuryCodes.RequestNotFound, "Treasury access request not found.", DomainFault.NotFound);

        request.Approve(actor, clock.UtcNow, command.Comment, actorMayApproveOwn: user.IsPrimaryAdministrator);   // state guard, SOD-TRS-001 (primary administrator excepted), recorded Approval, event
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Treasury, PermissionAction.Approve)]
public sealed record RejectTreasuryAccessCommand(Guid RequestId, string Reason) : ICommand;

public sealed class RejectTreasuryAccessValidator : AbstractValidator<RejectTreasuryAccessCommand>
{
    public RejectTreasuryAccessValidator()
    {
        RuleFor(command => command.RequestId).NotEmpty();
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(TreasuryAccessRequest.MaxReasonLength);
    }
}

public sealed class RejectTreasuryAccessHandler(ITreasuryAccessRequestRepository requests, IUnitOfWork unitOfWork, ICurrentUser user, IClock clock) : ICommandHandler<RejectTreasuryAccessCommand>
{
    public async Task Handle(RejectTreasuryAccessCommand command, CancellationToken cancellationToken)
    {
        var actor = Actor.Of(user);
        var request = await requests.GetForUpdateAsync(command.RequestId, cancellationToken)
            ?? throw new DomainException(TreasuryCodes.RequestNotFound, "Treasury access request not found.", DomainFault.NotFound);

        request.Reject(actor, clock.UtcNow, command.Reason);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Treasury, PermissionAction.Approve)]
public sealed record RevokeTreasuryAccessCommand(Guid RequestId, string Reason) : ICommand;

public sealed class RevokeTreasuryAccessValidator : AbstractValidator<RevokeTreasuryAccessCommand>
{
    public RevokeTreasuryAccessValidator()
    {
        RuleFor(command => command.RequestId).NotEmpty();
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(TreasuryAccessRequest.MaxReasonLength);
    }
}

public sealed class RevokeTreasuryAccessHandler(ITreasuryAccessRequestRepository requests, IUnitOfWork unitOfWork, ICurrentUser user, IClock clock) : ICommandHandler<RevokeTreasuryAccessCommand>
{
    public async Task Handle(RevokeTreasuryAccessCommand command, CancellationToken cancellationToken)
    {
        var actor = Actor.Of(user);
        var request = await requests.GetForUpdateAsync(command.RequestId, cancellationToken)
            ?? throw new DomainException(TreasuryCodes.RequestNotFound, "Treasury access request not found.", DomainFault.NotFound);

        request.Revoke(actor, clock.UtcNow, command.Reason);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Treasury, PermissionAction.Cancel)]
public sealed record CancelTreasuryAccessRequestCommand(Guid RequestId, string? Reason) : ICommand;

public sealed class CancelTreasuryAccessRequestValidator : AbstractValidator<CancelTreasuryAccessRequestCommand>
{
    public CancelTreasuryAccessRequestValidator()
    {
        RuleFor(command => command.RequestId).NotEmpty();
        RuleFor(command => command.Reason).MaximumLength(TreasuryAccessRequest.MaxReasonLength);
    }
}

public sealed class CancelTreasuryAccessRequestHandler(ITreasuryAccessRequestRepository requests, IUnitOfWork unitOfWork, ICurrentUser user, IClock clock) : ICommandHandler<CancelTreasuryAccessRequestCommand>
{
    public async Task Handle(CancelTreasuryAccessRequestCommand command, CancellationToken cancellationToken)
    {
        var actor = Actor.Of(user);
        var request = await requests.GetForUpdateAsync(command.RequestId, cancellationToken)
            ?? throw new DomainException(TreasuryCodes.RequestNotFound, "Treasury access request not found.", DomainFault.NotFound);

        request.Cancel(actor, clock.UtcNow, command.Reason);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
