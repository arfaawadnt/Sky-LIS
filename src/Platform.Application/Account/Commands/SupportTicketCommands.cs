using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Domain.Account;
using Platform.Domain.Authorization;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Account.Commands;

// The laboratory side of the support desk (CP-67; S1, S2, S6, S7). Validators check shape; the aggregate decides.

[RequirePermissionPolicy(PermissionCatalog.Support, PermissionAction.Create)]
public sealed record OpenSupportTicketCommand(string Subject, string Description, string Priority) : ICommand<CreatedResource>;

public sealed class OpenSupportTicketValidator : AbstractValidator<OpenSupportTicketCommand>
{
    public OpenSupportTicketValidator()
    {
        RuleFor(command => command.Subject).NotEmpty().MaximumLength(SupportTicket.MaxSubjectLength);
        RuleFor(command => command.Description).NotEmpty().MaximumLength(SupportTicket.MaxTextLength);
        RuleFor(command => command.Priority).NotEmpty().IsEnumName(typeof(SupportTicketPriority), caseSensitive: false);
    }
}

public sealed class OpenSupportTicketHandler(
    ISupportTicketRepository tickets,
    ILaboratoryProfileLookup laboratories,
    IReferenceNumbers numbers,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    ICurrentTenant tenant,
    IClock clock) : ICommandHandler<OpenSupportTicketCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(OpenSupportTicketCommand command, CancellationToken cancellationToken)
    {
        var tenantId = SupportDeskScope.RequireLaboratory(tenant);
        var actor = AccountActor.Require(user);
        var laboratory = await laboratories.FindAsync(tenantId, cancellationToken);

        var now = clock.UtcNow;
        var number = await numbers.NextAsync(SupportTicket.NumberKind, SupportTicket.NumberPrefix, now, cancellationToken);
        var ticket = SupportTicket.Open(
            tenantId,
            number,
            command.Subject,
            command.Description,
            Enum.Parse<SupportTicketPriority>(command.Priority, ignoreCase: true),
            actor,
            user.DisplayName,
            laboratory?.NameEn,
            laboratory?.NameAr,
            now);
        tickets.Add(ticket);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(ticket.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Support, PermissionAction.Create)]
public sealed record ReplyToSupportTicketCommand(Guid TicketId, string Text) : ICommand<CreatedResource>;

public sealed class ReplyToSupportTicketValidator : AbstractValidator<ReplyToSupportTicketCommand>
{
    public ReplyToSupportTicketValidator()
    {
        RuleFor(command => command.TicketId).NotEmpty();
        RuleFor(command => command.Text).NotEmpty().MaximumLength(SupportTicket.MaxTextLength);
    }
}

public sealed class ReplyToSupportTicketHandler(ISupportTicketRepository tickets, IUnitOfWork unitOfWork, ICurrentUser user, ICurrentTenant tenant, IClock clock)
    : ICommandHandler<ReplyToSupportTicketCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(ReplyToSupportTicketCommand command, CancellationToken cancellationToken)
    {
        SupportDeskScope.RequireLaboratory(tenant);
        var actor = AccountActor.Require(user);
        var ticket = await tickets.GetForUpdateAsync(command.TicketId, cancellationToken) ?? throw TicketErrors.NotFound();

        var reply = ticket.ReplyFromLaboratory(actor, user.DisplayName, command.Text, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(reply.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Support, PermissionAction.Create)]
public sealed record ReopenSupportTicketCommand(Guid TicketId) : ICommand;

public sealed class ReopenSupportTicketValidator : AbstractValidator<ReopenSupportTicketCommand>
{
    public ReopenSupportTicketValidator() => RuleFor(command => command.TicketId).NotEmpty();
}

public sealed class ReopenSupportTicketHandler(ISupportTicketRepository tickets, IUnitOfWork unitOfWork, ICurrentUser user, ICurrentTenant tenant, IClock clock)
    : ICommandHandler<ReopenSupportTicketCommand>
{
    public async Task Handle(ReopenSupportTicketCommand command, CancellationToken cancellationToken)
    {
        SupportDeskScope.RequireLaboratory(tenant);
        var actor = AccountActor.Require(user);
        var ticket = await tickets.GetForUpdateAsync(command.TicketId, cancellationToken) ?? throw TicketErrors.NotFound();

        ticket.Reopen(actor, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Support, PermissionAction.Create)]
public sealed record CloseSupportTicketCommand(Guid TicketId) : ICommand;

public sealed class CloseSupportTicketValidator : AbstractValidator<CloseSupportTicketCommand>
{
    public CloseSupportTicketValidator() => RuleFor(command => command.TicketId).NotEmpty();
}

public sealed class CloseSupportTicketHandler(ISupportTicketRepository tickets, IUnitOfWork unitOfWork, ICurrentUser user, ICurrentTenant tenant, IClock clock)
    : ICommandHandler<CloseSupportTicketCommand>
{
    public async Task Handle(CloseSupportTicketCommand command, CancellationToken cancellationToken)
    {
        SupportDeskScope.RequireLaboratory(tenant);
        var actor = AccountActor.Require(user);
        var ticket = await tickets.GetForUpdateAsync(command.TicketId, cancellationToken) ?? throw TicketErrors.NotFound();

        ticket.Close(actor, byPlatform: false, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>The one not-found refusal of the module's ticket handlers; a ticket of another laboratory is not found (INV-SUP-004).</summary>
internal static class TicketErrors
{
    public static DomainException NotFound() => new(AccountCodes.TicketNotFound, "Support ticket not found.", DomainFault.NotFound);
}
