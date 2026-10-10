using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Organization;
using Platform.Contracts.Results;
using Platform.Domain.Authorization;
using Platform.Domain.Results;
using Platform.Domain.Settings;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Results.Commands;

// T3 Review, T4 Verify (the signature gate), T5 Undo, T7 Acknowledge delta (intake LIS-INT-RES-001 §7). Single-result
// commands surface the refusal code; bulk commands (CP-21 review all / verify all) report each refusal and commit the rest.

[RequirePermissionPolicy(PermissionCatalog.Results, PermissionAction.Approve)]
public sealed record ReviewResultCommand(Guid ResultId) : ICommand;

public sealed class ReviewResultValidator : AbstractValidator<ReviewResultCommand>
{
    public ReviewResultValidator() => RuleFor(command => command.ResultId).NotEmpty();
}

public sealed class ReviewResultHandler(IResultRepository results, IUserPrivileges privileges, IUserDirectory directory, IUnitOfWork unitOfWork, ICurrentUser user, IClock clock) : ICommandHandler<ReviewResultCommand>
{
    public async Task Handle(ReviewResultCommand command, CancellationToken cancellationToken)
    {
        var actor = ResultsActor.Of(user);
        BranchScope.Require(user);
        var name = await ResultsActor.NameOfAsync(user, directory, cancellationToken);
        var result = (await results.ListForUpdateAsync([command.ResultId], cancellationToken)).SingleOrDefault() ?? throw ResultsNotFound.Result();
        result.Review(ResultRights.Actor(actor, name, privileges, result), clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Results, PermissionAction.Approve)]
public sealed record VerifyResultCommand(Guid ResultId) : ICommand;

public sealed class VerifyResultValidator : AbstractValidator<VerifyResultCommand>
{
    public VerifyResultValidator() => RuleFor(command => command.ResultId).NotEmpty();
}

public sealed class VerifyResultHandler(IResultRepository results, ITenantSettings settings, IUserPrivileges privileges, IUserDirectory directory, IUnitOfWork unitOfWork, ICurrentUser user, IClock clock) : ICommandHandler<VerifyResultCommand>
{
    public async Task Handle(VerifyResultCommand command, CancellationToken cancellationToken)
    {
        var actor = ResultsActor.Of(user);
        BranchScope.Require(user);
        var name = await ResultsActor.NameOfAsync(user, directory, cancellationToken);
        var reviewFirst = await settings.GetAsync(SettingKeys.ReviewBeforeVerify, cancellationToken);
        var result = (await results.ListForUpdateAsync([command.ResultId], cancellationToken)).SingleOrDefault() ?? throw ResultsNotFound.Result();
        result.Verify(ResultRights.Actor(actor, name, privileges, result), reviewFirst, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Results, PermissionAction.Approve)]
public sealed record ReviewResultsCommand(IReadOnlyList<Guid> ResultIds) : ICommand<BulkResultOutcome>;

public sealed class ReviewResultsValidator : AbstractValidator<ReviewResultsCommand>
{
    public ReviewResultsValidator() => RuleFor(command => command.ResultIds).Must(BulkResults.IsValid).WithMessage(BulkResults.Message);
}

public sealed class ReviewResultsHandler(IResultRepository results, IUserPrivileges privileges, IUserDirectory directory, IUnitOfWork unitOfWork, ICurrentUser user, IClock clock) : ICommandHandler<ReviewResultsCommand, BulkResultOutcome>
{
    public async Task<BulkResultOutcome> Handle(ReviewResultsCommand command, CancellationToken cancellationToken)
    {
        var actor = ResultsActor.Of(user);
        BranchScope.Require(user);
        var name = await ResultsActor.NameOfAsync(user, directory, cancellationToken);
        var now = clock.UtcNow;
        var outcome = await BulkResults.ApplyAsync(results, command.ResultIds, result => result.Review(ResultRights.Actor(actor, name, privileges, result), now), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return outcome;
    }
}

[RequirePermissionPolicy(PermissionCatalog.Results, PermissionAction.Approve)]
public sealed record VerifyResultsCommand(IReadOnlyList<Guid> ResultIds) : ICommand<BulkResultOutcome>;

public sealed class VerifyResultsValidator : AbstractValidator<VerifyResultsCommand>
{
    public VerifyResultsValidator() => RuleFor(command => command.ResultIds).Must(BulkResults.IsValid).WithMessage(BulkResults.Message);
}

public sealed class VerifyResultsHandler(IResultRepository results, ITenantSettings settings, IUserPrivileges privileges, IUserDirectory directory, IUnitOfWork unitOfWork, ICurrentUser user, IClock clock) : ICommandHandler<VerifyResultsCommand, BulkResultOutcome>
{
    public async Task<BulkResultOutcome> Handle(VerifyResultsCommand command, CancellationToken cancellationToken)
    {
        var actor = ResultsActor.Of(user);
        BranchScope.Require(user);
        var name = await ResultsActor.NameOfAsync(user, directory, cancellationToken);
        var reviewFirst = await settings.GetAsync(SettingKeys.ReviewBeforeVerify, cancellationToken);
        var now = clock.UtcNow;
        var outcome = await BulkResults.ApplyAsync(results, command.ResultIds, result => result.Verify(ResultRights.Actor(actor, name, privileges, result), reviewFirst, now), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return outcome;
    }
}

[RequirePermissionPolicy(PermissionCatalog.Results, PermissionAction.Cancel)]
public sealed record UndoResultCommand(Guid ResultId, string Reason) : ICommand;

public sealed class UndoResultValidator : AbstractValidator<UndoResultCommand>
{
    public UndoResultValidator()
    {
        RuleFor(command => command.ResultId).NotEmpty();
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(Result.MaxReasonLength);
    }
}

public sealed class UndoResultHandler(IResultRepository results, IUserPrivileges privileges, IUserDirectory directory, IUnitOfWork unitOfWork, ICurrentUser user, IClock clock) : ICommandHandler<UndoResultCommand>
{
    public async Task Handle(UndoResultCommand command, CancellationToken cancellationToken)
    {
        var actor = ResultsActor.Of(user);
        BranchScope.Require(user);
        var name = await ResultsActor.NameOfAsync(user, directory, cancellationToken);
        var result = (await results.ListForUpdateAsync([command.ResultId], cancellationToken)).SingleOrDefault() ?? throw ResultsNotFound.Result();
        result.Undo(ResultRights.Actor(actor, name, privileges, result), command.Reason, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Results, PermissionAction.Approve)]
public sealed record AcknowledgeDeltaCommand(Guid ResultId, string? Comment) : ICommand;

public sealed class AcknowledgeDeltaValidator : AbstractValidator<AcknowledgeDeltaCommand>
{
    public AcknowledgeDeltaValidator()
    {
        RuleFor(command => command.ResultId).NotEmpty();
        RuleFor(command => command.Comment).MaximumLength(Result.MaxReasonLength);
    }
}

public sealed class AcknowledgeDeltaHandler(IResultRepository results, IUserPrivileges privileges, IUserDirectory directory, IUnitOfWork unitOfWork, ICurrentUser user, IClock clock) : ICommandHandler<AcknowledgeDeltaCommand>
{
    public async Task Handle(AcknowledgeDeltaCommand command, CancellationToken cancellationToken)
    {
        var actor = ResultsActor.Of(user);
        BranchScope.Require(user);
        var name = await ResultsActor.NameOfAsync(user, directory, cancellationToken);
        var result = (await results.ListForUpdateAsync([command.ResultId], cancellationToken)).SingleOrDefault() ?? throw ResultsNotFound.Result();
        result.AcknowledgeDelta(ResultRights.Actor(actor, name, privileges, result), command.Comment, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>The bulk pattern of CP-21: each result decides on its own; refusals are reported with their codes, the rest commits together.</summary>
internal static class BulkResults
{
    public const int MaxIds = 500;
    public const string Message = "Send 1 to 500 result ids.";

    public static bool IsValid(IReadOnlyList<Guid>? ids) => ids is { Count: >= 1 and <= MaxIds } && ids.All(id => id != Guid.Empty);

    public static async Task<BulkResultOutcome> ApplyAsync(IResultRepository results, IReadOnlyList<Guid> ids, Action<Result> transition, CancellationToken cancellationToken)
    {
        var distinct = ids.Distinct().ToList();
        var loaded = (await results.ListForUpdateAsync(distinct, cancellationToken)).ToDictionary(result => result.Id);
        var changed = new List<Guid>();
        var refused = new List<ResultRefusal>();
        foreach (var id in distinct)
        {
            if (!loaded.TryGetValue(id, out var result))
            {
                refused.Add(new ResultRefusal(id, ResultsCodes.ResultNotFound, "Result not found."));
                continue;
            }

            try
            {
                transition(result);
                changed.Add(id);
            }
            catch (DomainException exception)
            {
                refused.Add(new ResultRefusal(id, exception.Code, exception.Message));
            }
        }

        return new BulkResultOutcome(changed, refused);
    }
}
