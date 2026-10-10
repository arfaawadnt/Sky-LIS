using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;
using Platform.Domain.Catalogue;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Catalogue.Commands;

// Clinical groups (SRS CP-39; intake LIS-INT-CAT-001 T1–T3). Each handler: load, call the aggregate, commit once.

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Create)]
public sealed record CreateClinicalGroupCommand(string Code, string UnitCode, string NameEn, string NameAr, string? ReportName, Guid? ReportTemplateId, int DisplayOrder) : ICommand<CreatedResource>;

public sealed class CreateClinicalGroupValidator : AbstractValidator<CreateClinicalGroupCommand>
{
    public CreateClinicalGroupValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(ClinicalGroup.MaxCodeLength);
        RuleFor(command => command.UnitCode).NotEmpty().MaximumLength(ClinicalGroup.MaxUnitCodeLength);
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.ReportName).MaximumLength(ClinicalGroup.MaxReportNameLength);
        RuleFor(command => command.DisplayOrder).InclusiveBetween(0, ClinicalGroup.MaxDisplayOrder);
    }
}

public sealed class CreateClinicalGroupHandler(IClinicalGroupRepository groups, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<CreateClinicalGroupCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateClinicalGroupCommand command, CancellationToken cancellationToken)
    {
        var code = ClinicalGroup.CleanCode(command.Code);
        if (await groups.CodeExistsAsync(code, cancellationToken))
        {
            throw new DomainException(ClinicalGroupCodes.CodeExists, $"A clinical group with code '{code}' already exists.", DomainFault.Conflict);
        }

        var group = ClinicalGroup.Create(code, command.UnitCode, new LocalizedText(command.NameEn, command.NameAr), command.ReportName, command.ReportTemplateId, command.DisplayOrder, clock.UtcNow);
        groups.Add(group);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(group.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record UpdateClinicalGroupCommand(Guid ClinicalGroupId, string UnitCode, string NameEn, string NameAr, string? ReportName, Guid? ReportTemplateId, int DisplayOrder) : ICommand;

public sealed class UpdateClinicalGroupValidator : AbstractValidator<UpdateClinicalGroupCommand>
{
    public UpdateClinicalGroupValidator()
    {
        RuleFor(command => command.ClinicalGroupId).NotEmpty();
        RuleFor(command => command.UnitCode).NotEmpty().MaximumLength(ClinicalGroup.MaxUnitCodeLength);
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.ReportName).MaximumLength(ClinicalGroup.MaxReportNameLength);
        RuleFor(command => command.DisplayOrder).InclusiveBetween(0, ClinicalGroup.MaxDisplayOrder);
    }
}

public sealed class UpdateClinicalGroupHandler(IClinicalGroupRepository groups, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<UpdateClinicalGroupCommand>
{
    public async Task Handle(UpdateClinicalGroupCommand command, CancellationToken cancellationToken)
    {
        var group = await groups.GetForUpdateAsync(command.ClinicalGroupId, cancellationToken)
            ?? throw new DomainException(ClinicalGroupCodes.NotFound, "Clinical group not found.", DomainFault.NotFound);

        group.Change(command.UnitCode, new LocalizedText(command.NameEn, command.NameAr), command.ReportName, command.ReportTemplateId, command.DisplayOrder, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>T3: deactivate (<paramref name="Active"/> false) or reactivate; both idempotent.</summary>
[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Manage)]
public sealed record SetClinicalGroupActiveCommand(Guid ClinicalGroupId, bool Active) : ICommand;

public sealed class SetClinicalGroupActiveValidator : AbstractValidator<SetClinicalGroupActiveCommand>
{
    public SetClinicalGroupActiveValidator() => RuleFor(command => command.ClinicalGroupId).NotEmpty();
}

public sealed class SetClinicalGroupActiveHandler(IClinicalGroupRepository groups, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<SetClinicalGroupActiveCommand>
{
    public async Task Handle(SetClinicalGroupActiveCommand command, CancellationToken cancellationToken)
    {
        var group = await groups.GetForUpdateAsync(command.ClinicalGroupId, cancellationToken)
            ?? throw new DomainException(ClinicalGroupCodes.NotFound, "Clinical group not found.", DomainFault.NotFound);

        if (command.Active)
        {
            group.Reactivate(clock.UtcNow);
        }
        else
        {
            group.Deactivate(clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
