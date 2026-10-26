using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;
using Platform.Domain.Catalogue;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Catalogue.Commands;

// Sample types and conditions (SRS CP-43; intake LIS-INT-CAT-001 T3–T6).

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Create)]
/// <summary><paramref name="ForCulture"/> (LIS-CR-007, C-11): a culture specimen; absent = false.</summary>
public sealed record CreateSampleTypeCommand(string Code, string ShortName, string NameEn, string NameAr, IReadOnlyList<SampleConditionItem>? Conditions, bool? ForCulture = null) : ICommand<CreatedResource>;

public sealed class CreateSampleTypeValidator : AbstractValidator<CreateSampleTypeCommand>
{
    public CreateSampleTypeValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(SampleType.MaxCodeLength);
        RuleFor(command => command.ShortName).NotEmpty().MaximumLength(SampleType.MaxShortNameLength);
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.Conditions!.Count).LessThanOrEqualTo(SampleType.MaxConditions).When(command => command.Conditions is not null);
        RuleForEach(command => command.Conditions).SetValidator(new SampleConditionItemValidator());
    }
}

public sealed class SampleConditionItemValidator : AbstractValidator<SampleConditionItem>
{
    public SampleConditionItemValidator()
    {
        RuleFor(item => item.Code).NotEmpty().MaximumLength(SampleCondition.MaxCodeLength).When(item => item.Id is null);
        RuleFor(item => item.NameEn).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(item => item.NameAr).NotEmpty().MaximumLength(LocalizedText.MaxLength);
    }
}

public sealed class CreateSampleTypeHandler(ISampleTypeRepository sampleTypes, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<CreateSampleTypeCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateSampleTypeCommand command, CancellationToken cancellationToken)
    {
        var code = SampleType.CleanCode(command.Code);
        if (await sampleTypes.CodeExistsAsync(code, cancellationToken))
        {
            throw new DomainException(SampleTypeCodes.CodeExists, $"A sample type with code '{code}' already exists.", DomainFault.Conflict);
        }

        var conditions = (command.Conditions ?? []).Select(item => new SampleConditionSpec(null, item.Code, new LocalizedText(item.NameEn, item.NameAr), item.IsActive)).ToList();
        var sampleType = SampleType.Create(code, command.ShortName, new LocalizedText(command.NameEn, command.NameAr), conditions, clock.UtcNow, command.ForCulture ?? false);
        sampleTypes.Add(sampleType);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(sampleType.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
/// <summary><paramref name="ForCulture"/> (LIS-CR-007, C-11): absent = the flag is kept, so an older client never clears it.</summary>
public sealed record UpdateSampleTypeCommand(Guid SampleTypeId, string ShortName, string NameEn, string NameAr, bool? ForCulture = null) : ICommand;

public sealed class UpdateSampleTypeValidator : AbstractValidator<UpdateSampleTypeCommand>
{
    public UpdateSampleTypeValidator()
    {
        RuleFor(command => command.SampleTypeId).NotEmpty();
        RuleFor(command => command.ShortName).NotEmpty().MaximumLength(SampleType.MaxShortNameLength);
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocalizedText.MaxLength);
    }
}

public sealed class UpdateSampleTypeHandler(ISampleTypeRepository sampleTypes, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<UpdateSampleTypeCommand>
{
    public async Task Handle(UpdateSampleTypeCommand command, CancellationToken cancellationToken)
    {
        var sampleType = await sampleTypes.GetForUpdateAsync(command.SampleTypeId, cancellationToken)
            ?? throw new DomainException(SampleTypeCodes.NotFound, "Sample type not found.", DomainFault.NotFound);

        sampleType.Change(command.ShortName, new LocalizedText(command.NameEn, command.NameAr), clock.UtcNow, command.ForCulture);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>T6: the full list of conditions; listed ones are updated or added, omitted existing ones are deactivated.</summary>
[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record SaveSampleConditionsCommand(Guid SampleTypeId, IReadOnlyList<SampleConditionItem> Conditions) : ICommand;

public sealed class SaveSampleConditionsValidator : AbstractValidator<SaveSampleConditionsCommand>
{
    public SaveSampleConditionsValidator()
    {
        RuleFor(command => command.SampleTypeId).NotEmpty();
        RuleFor(command => command.Conditions).NotNull();
        RuleFor(command => command.Conditions.Count).LessThanOrEqualTo(SampleType.MaxConditions).When(command => command.Conditions is not null);
        RuleForEach(command => command.Conditions).SetValidator(new SampleConditionItemValidator());
    }
}

public sealed class SaveSampleConditionsHandler(ISampleTypeRepository sampleTypes, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<SaveSampleConditionsCommand>
{
    public async Task Handle(SaveSampleConditionsCommand command, CancellationToken cancellationToken)
    {
        var sampleType = await sampleTypes.GetForUpdateAsync(command.SampleTypeId, cancellationToken)
            ?? throw new DomainException(SampleTypeCodes.NotFound, "Sample type not found.", DomainFault.NotFound);

        var specs = command.Conditions.Select(item => new SampleConditionSpec(item.Id, item.Code, new LocalizedText(item.NameEn, item.NameAr), item.IsActive)).ToList();
        sampleType.ReplaceConditions(specs, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Manage)]
public sealed record SetSampleTypeActiveCommand(Guid SampleTypeId, bool Active) : ICommand;

public sealed class SetSampleTypeActiveValidator : AbstractValidator<SetSampleTypeActiveCommand>
{
    public SetSampleTypeActiveValidator() => RuleFor(command => command.SampleTypeId).NotEmpty();
}

public sealed class SetSampleTypeActiveHandler(ISampleTypeRepository sampleTypes, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<SetSampleTypeActiveCommand>
{
    public async Task Handle(SetSampleTypeActiveCommand command, CancellationToken cancellationToken)
    {
        var sampleType = await sampleTypes.GetForUpdateAsync(command.SampleTypeId, cancellationToken)
            ?? throw new DomainException(SampleTypeCodes.NotFound, "Sample type not found.", DomainFault.NotFound);

        if (command.Active)
        {
            sampleType.Reactivate(clock.UtcNow);
        }
        else
        {
            sampleType.Deactivate(clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Compatible conditions (LIS-CR-015 M4): the full compatibility map of a sample type; listed conditions merge into the
/// condition named (or keep their own sample), unlisted ones keep their own sample. Rules in <see cref="SampleType.SetCompatibleConditions"/>.
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record SaveCompatibleConditionsCommand(Guid SampleTypeId, IReadOnlyList<SampleConditionPairItem> Pairs) : ICommand;

public sealed class SaveCompatibleConditionsValidator : AbstractValidator<SaveCompatibleConditionsCommand>
{
    public SaveCompatibleConditionsValidator()
    {
        RuleFor(command => command.SampleTypeId).NotEmpty();
        RuleFor(command => command.Pairs).NotNull();
        RuleFor(command => command.Pairs.Count).LessThanOrEqualTo(SampleType.MaxConditions).When(command => command.Pairs is not null);
        RuleForEach(command => command.Pairs).ChildRules(pair =>
        {
            pair.RuleFor(item => item.ConditionId).NotEmpty();
            pair.RuleFor(item => item.MergesIntoConditionId).NotEqual(Guid.Empty).When(item => item.MergesIntoConditionId is not null);
        });
    }
}

public sealed class SaveCompatibleConditionsHandler(ISampleTypeRepository sampleTypes, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<SaveCompatibleConditionsCommand>
{
    public async Task Handle(SaveCompatibleConditionsCommand command, CancellationToken cancellationToken)
    {
        var sampleType = await sampleTypes.GetForUpdateAsync(command.SampleTypeId, cancellationToken)
            ?? throw new DomainException(SampleTypeCodes.NotFound, "Sample type not found.", DomainFault.NotFound);

        if (sampleType.SetCompatibleConditions([.. command.Pairs.Select(pair => new SampleConditionPair(pair.ConditionId, pair.MergesIntoConditionId))], clock.UtcNow))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
