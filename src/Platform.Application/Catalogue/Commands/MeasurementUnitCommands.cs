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

// Units of measurement (SRS CP-44, T7) and patient conditions (SRS CP-45, T8).

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Create)]
public sealed record CreateMeasurementUnitCommand(string Name) : ICommand<CreatedResource>;

public sealed class CreateMeasurementUnitValidator : AbstractValidator<CreateMeasurementUnitCommand>
{
    public CreateMeasurementUnitValidator() => RuleFor(command => command.Name).NotEmpty().MaximumLength(MeasurementUnit.MaxNameLength);
}

public sealed class CreateMeasurementUnitHandler(IMeasurementUnitRepository units, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<CreateMeasurementUnitCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateMeasurementUnitCommand command, CancellationToken cancellationToken)
    {
        var name = MeasurementUnit.CleanName(command.Name);
        if (await units.NameExistsAsync(MeasurementUnit.KeyOf(name), null, cancellationToken))
        {
            throw new DomainException(MeasurementUnitCodes.NameExists, $"A unit named '{name}' already exists.", DomainFault.Conflict);
        }

        var unit = MeasurementUnit.Create(name, clock.UtcNow);
        units.Add(unit);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(unit.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record RenameMeasurementUnitCommand(Guid MeasurementUnitId, string Name) : ICommand;

public sealed class RenameMeasurementUnitValidator : AbstractValidator<RenameMeasurementUnitCommand>
{
    public RenameMeasurementUnitValidator()
    {
        RuleFor(command => command.MeasurementUnitId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(MeasurementUnit.MaxNameLength);
    }
}

public sealed class RenameMeasurementUnitHandler(IMeasurementUnitRepository units, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<RenameMeasurementUnitCommand>
{
    public async Task Handle(RenameMeasurementUnitCommand command, CancellationToken cancellationToken)
    {
        var unit = await units.GetForUpdateAsync(command.MeasurementUnitId, cancellationToken)
            ?? throw new DomainException(MeasurementUnitCodes.NotFound, "Unit of measurement not found.", DomainFault.NotFound);

        var name = MeasurementUnit.CleanName(command.Name);
        if (await units.NameExistsAsync(MeasurementUnit.KeyOf(name), unit.Id, cancellationToken))
        {
            throw new DomainException(MeasurementUnitCodes.NameExists, $"A unit named '{name}' already exists.", DomainFault.Conflict);
        }

        unit.Rename(name, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Manage)]
public sealed record SetMeasurementUnitActiveCommand(Guid MeasurementUnitId, bool Active) : ICommand;

public sealed class SetMeasurementUnitActiveValidator : AbstractValidator<SetMeasurementUnitActiveCommand>
{
    public SetMeasurementUnitActiveValidator() => RuleFor(command => command.MeasurementUnitId).NotEmpty();
}

public sealed class SetMeasurementUnitActiveHandler(IMeasurementUnitRepository units, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<SetMeasurementUnitActiveCommand>
{
    public async Task Handle(SetMeasurementUnitActiveCommand command, CancellationToken cancellationToken)
    {
        var unit = await units.GetForUpdateAsync(command.MeasurementUnitId, cancellationToken)
            ?? throw new DomainException(MeasurementUnitCodes.NotFound, "Unit of measurement not found.", DomainFault.NotFound);

        if (command.Active)
        {
            unit.Reactivate(clock.UtcNow);
        }
        else
        {
            unit.Deactivate(clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Create)]
public sealed record CreatePatientConditionCommand(string NameEn, string NameAr) : ICommand<CreatedResource>;

public sealed class CreatePatientConditionValidator : AbstractValidator<CreatePatientConditionCommand>
{
    public CreatePatientConditionValidator()
    {
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocalizedText.MaxLength);
    }
}

public sealed class CreatePatientConditionHandler(IPatientConditionRepository conditions, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<CreatePatientConditionCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreatePatientConditionCommand command, CancellationToken cancellationToken)
    {
        var name = new LocalizedText(command.NameEn, command.NameAr);
        if (await conditions.NameExistsAsync(PatientCondition.KeyOf(name), null, cancellationToken))
        {
            throw new DomainException(PatientConditionCodes.NameExists, $"A patient condition named '{name.En}' already exists.", DomainFault.Conflict);
        }

        var condition = PatientCondition.Create(name, clock.UtcNow);
        conditions.Add(condition);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(condition.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record RenamePatientConditionCommand(Guid PatientConditionId, string NameEn, string NameAr) : ICommand;

public sealed class RenamePatientConditionValidator : AbstractValidator<RenamePatientConditionCommand>
{
    public RenamePatientConditionValidator()
    {
        RuleFor(command => command.PatientConditionId).NotEmpty();
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocalizedText.MaxLength);
    }
}

public sealed class RenamePatientConditionHandler(IPatientConditionRepository conditions, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<RenamePatientConditionCommand>
{
    public async Task Handle(RenamePatientConditionCommand command, CancellationToken cancellationToken)
    {
        var condition = await conditions.GetForUpdateAsync(command.PatientConditionId, cancellationToken)
            ?? throw new DomainException(PatientConditionCodes.NotFound, "Patient condition not found.", DomainFault.NotFound);

        var name = new LocalizedText(command.NameEn, command.NameAr);
        if (await conditions.NameExistsAsync(PatientCondition.KeyOf(name), condition.Id, cancellationToken))
        {
            throw new DomainException(PatientConditionCodes.NameExists, $"A patient condition named '{name.En}' already exists.", DomainFault.Conflict);
        }

        condition.Rename(name, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Manage)]
public sealed record SetPatientConditionActiveCommand(Guid PatientConditionId, bool Active) : ICommand;

public sealed class SetPatientConditionActiveValidator : AbstractValidator<SetPatientConditionActiveCommand>
{
    public SetPatientConditionActiveValidator() => RuleFor(command => command.PatientConditionId).NotEmpty();
}

public sealed class SetPatientConditionActiveHandler(IPatientConditionRepository conditions, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<SetPatientConditionActiveCommand>
{
    public async Task Handle(SetPatientConditionActiveCommand command, CancellationToken cancellationToken)
    {
        var condition = await conditions.GetForUpdateAsync(command.PatientConditionId, cancellationToken)
            ?? throw new DomainException(PatientConditionCodes.NotFound, "Patient condition not found.", DomainFault.NotFound);

        if (command.Active)
        {
            condition.Reactivate(clock.UtcNow);
        }
        else
        {
            condition.Deactivate(clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
