using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.PlatformAdmin.Commands;

// Notification templates (SA-20). The texts are served to every sender through INotificationTemplateSource.

[RequirePermissionPolicy(PermissionCatalog.Notifications, PermissionAction.Manage)]
public sealed record CreateNotificationTemplateCommand(string Code, string Channel, string TextEn, string TextAr, string? SubjectEn, string? SubjectAr)
    : ICommand<CreatedResource>, IPlatformAdministrationRequest;

public sealed class CreateNotificationTemplateValidator : AbstractValidator<CreateNotificationTemplateCommand>
{
    public CreateNotificationTemplateValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(NotificationTemplate.MaxCodeLength);
        RuleFor(command => command.Channel).NotEmpty().IsEnumName(typeof(TemplateChannel), caseSensitive: false);
        RuleFor(command => command.TextEn).NotEmpty().MaximumLength(NotificationTemplate.MaxTextLength);
        RuleFor(command => command.TextAr).NotEmpty().MaximumLength(NotificationTemplate.MaxTextLength);
        RuleFor(command => command.SubjectEn).MaximumLength(NotificationTemplate.MaxSubjectLength);
        RuleFor(command => command.SubjectAr).MaximumLength(NotificationTemplate.MaxSubjectLength);
    }
}

public sealed class CreateNotificationTemplateHandler(INotificationTemplateRepository templates, ICurrentTenant currentTenant, IUnitOfWork unitOfWork) : ICommandHandler<CreateNotificationTemplateCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateNotificationTemplateCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var template = NotificationTemplate.Create(command.Code, Enum.Parse<TemplateChannel>(command.Channel, ignoreCase: true), command.TextEn, command.TextAr, command.SubjectEn, command.SubjectAr);
        if (await templates.CodeExistsAsync(template.Code, cancellationToken))
        {
            throw new DomainException(PlatformAdminCodes.TemplateCodeExists, $"A template with code '{template.Code}' already exists.", DomainFault.Conflict);
        }

        templates.Add(template);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(template.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Notifications, PermissionAction.Manage)]
public sealed record UpdateNotificationTemplateCommand(Guid TemplateId, string Channel, string TextEn, string TextAr, string? SubjectEn, string? SubjectAr)
    : ICommand, IPlatformAdministrationRequest;

public sealed class UpdateNotificationTemplateValidator : AbstractValidator<UpdateNotificationTemplateCommand>
{
    public UpdateNotificationTemplateValidator()
    {
        RuleFor(command => command.TemplateId).NotEmpty();
        RuleFor(command => command.Channel).NotEmpty().IsEnumName(typeof(TemplateChannel), caseSensitive: false);
        RuleFor(command => command.TextEn).NotEmpty().MaximumLength(NotificationTemplate.MaxTextLength);
        RuleFor(command => command.TextAr).NotEmpty().MaximumLength(NotificationTemplate.MaxTextLength);
        RuleFor(command => command.SubjectEn).MaximumLength(NotificationTemplate.MaxSubjectLength);
        RuleFor(command => command.SubjectAr).MaximumLength(NotificationTemplate.MaxSubjectLength);
    }
}

public sealed class UpdateNotificationTemplateHandler(INotificationTemplateRepository templates, ICurrentTenant currentTenant, IUnitOfWork unitOfWork) : ICommandHandler<UpdateNotificationTemplateCommand>
{
    public async Task Handle(UpdateNotificationTemplateCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var template = await templates.GetForUpdateAsync(command.TemplateId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.TemplateNotFound, "Template not found.", DomainFault.NotFound);
        template.Update(Enum.Parse<TemplateChannel>(command.Channel, ignoreCase: true), command.TextEn, command.TextAr, command.SubjectEn, command.SubjectAr);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>An inactive template is not served; senders fall back to the built-in text of the code.</summary>
[RequirePermissionPolicy(PermissionCatalog.Notifications, PermissionAction.Manage)]
public sealed record SetNotificationTemplateActiveCommand(Guid TemplateId, bool Active) : ICommand, IPlatformAdministrationRequest;

public sealed class SetNotificationTemplateActiveValidator : AbstractValidator<SetNotificationTemplateActiveCommand>
{
    public SetNotificationTemplateActiveValidator() => RuleFor(command => command.TemplateId).NotEmpty();
}

public sealed class SetNotificationTemplateActiveHandler(INotificationTemplateRepository templates, ICurrentTenant currentTenant, IUnitOfWork unitOfWork) : ICommandHandler<SetNotificationTemplateActiveCommand>
{
    public async Task Handle(SetNotificationTemplateActiveCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var template = await templates.GetForUpdateAsync(command.TemplateId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.TemplateNotFound, "Template not found.", DomainFault.NotFound);
        if (command.Active)
        {
            template.Reactivate();
        }
        else
        {
            template.Deactivate();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
