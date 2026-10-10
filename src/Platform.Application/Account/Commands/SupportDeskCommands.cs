using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Domain.Account;
using Platform.Domain.Authorization;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.Account.Commands;

// The platform side of the support desk (S3, S4, S5, S7 by the desk). Every handler first checks the platform tenant
// (SUP-403): a laboratory role holding support.manage never reaches another laboratory's tickets.

[RequirePermissionPolicy(PermissionCatalog.Support, PermissionAction.Manage)]
public sealed record AnswerSupportTicketCommand(Guid TicketId, string Text) : ICommand<CreatedResource>;

public sealed class AnswerSupportTicketValidator : AbstractValidator<AnswerSupportTicketCommand>
{
    public AnswerSupportTicketValidator()
    {
        RuleFor(command => command.TicketId).NotEmpty();
        RuleFor(command => command.Text).NotEmpty().MaximumLength(SupportTicket.MaxTextLength);
    }
}

public sealed class AnswerSupportTicketHandler(ISupportTicketRepository tickets, IUnitOfWork unitOfWork, ICurrentUser user, ICurrentTenant tenant, IClock clock)
    : ICommandHandler<AnswerSupportTicketCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(AnswerSupportTicketCommand command, CancellationToken cancellationToken)
    {
        SupportDeskScope.RequireDesk(tenant);
        var actor = AccountActor.Require(user);
        var ticket = await tickets.GetForUpdateAsync(command.TicketId, cancellationToken) ?? throw TicketErrors.NotFound();

        var reply = ticket.ReplyFromPlatform(actor, user.DisplayName, command.Text, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(reply.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Support, PermissionAction.Manage)]
public sealed record StartSupportTicketCommand(Guid TicketId) : ICommand;

public sealed class StartSupportTicketValidator : AbstractValidator<StartSupportTicketCommand>
{
    public StartSupportTicketValidator() => RuleFor(command => command.TicketId).NotEmpty();
}

public sealed class StartSupportTicketHandler(ISupportTicketRepository tickets, IUnitOfWork unitOfWork, ICurrentUser user, ICurrentTenant tenant, IClock clock)
    : ICommandHandler<StartSupportTicketCommand>
{
    public async Task Handle(StartSupportTicketCommand command, CancellationToken cancellationToken)
    {
        SupportDeskScope.RequireDesk(tenant);
        var actor = AccountActor.Require(user);
        var ticket = await tickets.GetForUpdateAsync(command.TicketId, cancellationToken) ?? throw TicketErrors.NotFound();

        ticket.Start(actor, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Support, PermissionAction.Manage)]
public sealed record ResolveSupportTicketCommand(Guid TicketId, string? Note) : ICommand;

public sealed class ResolveSupportTicketValidator : AbstractValidator<ResolveSupportTicketCommand>
{
    public ResolveSupportTicketValidator()
    {
        RuleFor(command => command.TicketId).NotEmpty();
        RuleFor(command => command.Note).MaximumLength(SupportTicket.MaxTextLength);
    }
}

public sealed class ResolveSupportTicketHandler(ISupportTicketRepository tickets, IUnitOfWork unitOfWork, ICurrentUser user, ICurrentTenant tenant, IClock clock)
    : ICommandHandler<ResolveSupportTicketCommand>
{
    public async Task Handle(ResolveSupportTicketCommand command, CancellationToken cancellationToken)
    {
        SupportDeskScope.RequireDesk(tenant);
        var actor = AccountActor.Require(user);
        var ticket = await tickets.GetForUpdateAsync(command.TicketId, cancellationToken) ?? throw TicketErrors.NotFound();

        ticket.Resolve(actor, user.DisplayName, command.Note, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Support, PermissionAction.Manage)]
public sealed record CloseSupportTicketAtDeskCommand(Guid TicketId) : ICommand;

public sealed class CloseSupportTicketAtDeskValidator : AbstractValidator<CloseSupportTicketAtDeskCommand>
{
    public CloseSupportTicketAtDeskValidator() => RuleFor(command => command.TicketId).NotEmpty();
}

public sealed class CloseSupportTicketAtDeskHandler(ISupportTicketRepository tickets, IUnitOfWork unitOfWork, ICurrentUser user, ICurrentTenant tenant, IClock clock)
    : ICommandHandler<CloseSupportTicketAtDeskCommand>
{
    public async Task Handle(CloseSupportTicketAtDeskCommand command, CancellationToken cancellationToken)
    {
        SupportDeskScope.RequireDesk(tenant);
        var actor = AccountActor.Require(user);
        var ticket = await tickets.GetForUpdateAsync(command.TicketId, cancellationToken) ?? throw TicketErrors.NotFound();

        ticket.Close(actor, byPlatform: true, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
