using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Organization;
using Platform.Domain.Authorization;
using Platform.Domain.Results;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.Results.Commands;

// T10 report comment (prints) and T11 internal note (never prints; LDM §5.4 three comment kinds).

[RequirePermissionPolicy(PermissionCatalog.Results, PermissionAction.Create)]
public sealed record SetResultCommentCommand(Guid ResultId, string? Text) : ICommand;

public sealed class SetResultCommentValidator : AbstractValidator<SetResultCommentCommand>
{
    public SetResultCommentValidator()
    {
        RuleFor(command => command.ResultId).NotEmpty();
        RuleFor(command => command.Text).MaximumLength(Result.MaxCommentLength);
    }
}

public sealed class SetResultCommentHandler(IResultRepository results, IUserPrivileges privileges, IUserDirectory directory, IUnitOfWork unitOfWork, ICurrentUser user, IClock clock) : ICommandHandler<SetResultCommentCommand>
{
    public async Task Handle(SetResultCommentCommand command, CancellationToken cancellationToken)
    {
        var actor = ResultsActor.Of(user);
        BranchScope.Require(user);
        var name = await ResultsActor.NameOfAsync(user, directory, cancellationToken);
        var result = (await results.ListForUpdateAsync([command.ResultId], cancellationToken)).SingleOrDefault() ?? throw ResultsNotFound.Result();
        result.SetReportComment(command.Text, ResultRights.Actor(actor, name, privileges, result), clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Results, PermissionAction.Create)]
public sealed record SetResultNoteCommand(Guid ResultId, string? Text) : ICommand;

public sealed class SetResultNoteValidator : AbstractValidator<SetResultNoteCommand>
{
    public SetResultNoteValidator()
    {
        RuleFor(command => command.ResultId).NotEmpty();
        RuleFor(command => command.Text).MaximumLength(Result.MaxNoteLength);
    }
}

public sealed class SetResultNoteHandler(IResultRepository results, IUserPrivileges privileges, IUserDirectory directory, IUnitOfWork unitOfWork, ICurrentUser user, IClock clock) : ICommandHandler<SetResultNoteCommand>
{
    public async Task Handle(SetResultNoteCommand command, CancellationToken cancellationToken)
    {
        var actor = ResultsActor.Of(user);
        BranchScope.Require(user);
        var name = await ResultsActor.NameOfAsync(user, directory, cancellationToken);
        var result = (await results.ListForUpdateAsync([command.ResultId], cancellationToken)).SingleOrDefault() ?? throw ResultsNotFound.Result();
        result.SetInternalNote(command.Text, ResultRights.Actor(actor, name, privileges, result), clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
