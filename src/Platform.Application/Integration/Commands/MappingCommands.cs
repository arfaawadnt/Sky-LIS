using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Contracts.Integration;
using Platform.Domain.Authorization;
using Platform.Domain.Integration;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Integration.Commands;

// Analyser test mappings (CP-59; intake rows T1–T4).

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Edit)]
public sealed record MapAnalyserTestCommand(Guid AnalyserId, SaveMappingRequest Mapping) : ICommand<CreatedResource>;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Edit)]
public sealed record UpdateAnalyserTestMappingCommand(Guid AnalyserId, Guid MappingId, SaveMappingRequest Mapping) : ICommand;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Edit)]
public sealed record SetMappingAutoSendCommand(Guid AnalyserId, Guid MappingId, bool Enabled) : ICommand;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Edit)]
public sealed record ReplaceMappingConversionsCommand(Guid AnalyserId, Guid MappingId, IReadOnlyList<ConversionDto> Conversions) : ICommand;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Edit)]
public sealed record SetMappingRangeOverrideCommand(Guid AnalyserId, Guid MappingId, RangeOverrideDto? RangeOverride) : ICommand;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Manage)]
public sealed record DeactivateMappingCommand(Guid AnalyserId, Guid MappingId) : ICommand;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Manage)]
public sealed record ReactivateMappingCommand(Guid AnalyserId, Guid MappingId) : ICommand;

public sealed class ConversionDtoValidator : AbstractValidator<ConversionDto>
{
    public ConversionDtoValidator()
    {
        RuleFor(conversion => conversion.FromValue).NotEmpty().MaximumLength(AnalyserTestMapping.MaxConversionValueLength);
        RuleFor(conversion => conversion.ToValue).NotEmpty().MaximumLength(AnalyserTestMapping.MaxConversionValueLength);
    }
}

public sealed class RangeOverrideDtoValidator : AbstractValidator<RangeOverrideDto>
{
    public RangeOverrideDtoValidator() => RuleFor(range => range.Text).MaximumLength(AnalyserTestMapping.MaxRangeTextLength);
}

public sealed class SaveMappingRequestValidator : AbstractValidator<SaveMappingRequest>
{
    public SaveMappingRequestValidator()
    {
        RuleFor(request => request.TestId).NotEmpty();
        RuleFor(request => request.AnalyserCode).NotEmpty().MaximumLength(AnalyserTestMapping.MaxCodeLength);
        RuleFor(request => request.HostCode).MaximumLength(AnalyserTestMapping.MaxCodeLength);
        RuleFor(request => request.ResultSelection).InclusiveBetween(1, AnalyserTestMapping.MaxResultSelection);
        RuleFor(request => request.Slope).NotEqual(0m);
        RuleFor(request => request.QueryDetails).MaximumLength(AnalyserTestMapping.MaxTextLength);
        RuleFor(request => request.Methodology).MaximumLength(AnalyserTestMapping.MaxTextLength);
        RuleFor(request => request.Conversions).Must(conversions => conversions is null || conversions.Count <= AnalyserTestMapping.MaxConversions).WithMessage($"At most {AnalyserTestMapping.MaxConversions} conversions.");
        RuleForEach(request => request.Conversions).SetValidator(new ConversionDtoValidator());
        RuleFor(request => request.RangeOverride!).SetValidator(new RangeOverrideDtoValidator()).When(request => request.RangeOverride is not null);
    }
}

public sealed class MapAnalyserTestValidator : AbstractValidator<MapAnalyserTestCommand>
{
    public MapAnalyserTestValidator()
    {
        RuleFor(command => command.AnalyserId).NotEmpty();
        RuleFor(command => command.Mapping).NotNull().SetValidator(new SaveMappingRequestValidator());
    }
}

public sealed class UpdateAnalyserTestMappingValidator : AbstractValidator<UpdateAnalyserTestMappingCommand>
{
    public UpdateAnalyserTestMappingValidator()
    {
        RuleFor(command => command.AnalyserId).NotEmpty();
        RuleFor(command => command.MappingId).NotEmpty();
        RuleFor(command => command.Mapping).NotNull().SetValidator(new SaveMappingRequestValidator());
    }
}

public sealed class SetMappingAutoSendValidator : AbstractValidator<SetMappingAutoSendCommand>
{
    public SetMappingAutoSendValidator()
    {
        RuleFor(command => command.AnalyserId).NotEmpty();
        RuleFor(command => command.MappingId).NotEmpty();
    }
}

public sealed class ReplaceMappingConversionsValidator : AbstractValidator<ReplaceMappingConversionsCommand>
{
    public ReplaceMappingConversionsValidator()
    {
        RuleFor(command => command.AnalyserId).NotEmpty();
        RuleFor(command => command.MappingId).NotEmpty();
        RuleFor(command => command.Conversions).NotNull().Must(conversions => conversions is null || conversions.Count <= AnalyserTestMapping.MaxConversions).WithMessage($"At most {AnalyserTestMapping.MaxConversions} conversions.");
        RuleForEach(command => command.Conversions).SetValidator(new ConversionDtoValidator());
    }
}

public sealed class SetMappingRangeOverrideValidator : AbstractValidator<SetMappingRangeOverrideCommand>
{
    public SetMappingRangeOverrideValidator()
    {
        RuleFor(command => command.AnalyserId).NotEmpty();
        RuleFor(command => command.MappingId).NotEmpty();
        RuleFor(command => command.RangeOverride!).SetValidator(new RangeOverrideDtoValidator()).When(command => command.RangeOverride is not null);
    }
}

public sealed class DeactivateMappingValidator : AbstractValidator<DeactivateMappingCommand>
{
    public DeactivateMappingValidator()
    {
        RuleFor(command => command.AnalyserId).NotEmpty();
        RuleFor(command => command.MappingId).NotEmpty();
    }
}

public sealed class ReactivateMappingValidator : AbstractValidator<ReactivateMappingCommand>
{
    public ReactivateMappingValidator()
    {
        RuleFor(command => command.AnalyserId).NotEmpty();
        RuleFor(command => command.MappingId).NotEmpty();
    }
}

internal static class MappingRules
{
    public static async Task<AnalyserTestMapping> LoadAsync(IAnalyserTestMappingRepository mappings, Guid analyserId, Guid mappingId, CancellationToken cancellationToken) =>
        await mappings.GetInAnalyserAsync(analyserId, mappingId, cancellationToken)
        ?? throw new DomainException(IntegrationCodes.MappingNotFound, "Mapping not found.", DomainFault.NotFound);

    /// <summary>INV-INT-003 with its code, the test (INT-042) and the unit (INT-043); the unique index is the backstop.</summary>
    public static async Task RequireAsync(MappingDefinition definition, Guid analyserId, Guid? exceptMappingId, IAnalyserTestMappingRepository mappings, IIntegrationReferenceLookup lookup, CancellationToken cancellationToken)
    {
        var code = AnalyserTestMapping.CleanAnalyserCode(definition.AnalyserCode);
        if (await mappings.CodeExistsAsync(analyserId, code, exceptMappingId, cancellationToken))
        {
            throw new DomainException(IntegrationCodes.MappingCodeExists, $"The analyser code '{code}' is already mapped on this analyser.", DomainFault.Conflict);
        }

        await IntegrationReferences.RequireAsync(lookup, ReferenceKind.Test, definition.TestId, cancellationToken);
        await IntegrationReferences.RequireAsync(lookup, ReferenceKind.Unit, definition.UnitId, cancellationToken);
    }
}

public sealed class MapAnalyserTestHandler(
    IAnalyserRepository analysers,
    IAnalyserTestMappingRepository mappings,
    IIntegrationReferenceLookup lookup,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<MapAnalyserTestCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(MapAnalyserTestCommand command, CancellationToken cancellationToken)
    {
        var analyser = await AnalyserReferences.LoadAsync(analysers, command.AnalyserId, cancellationToken);
        var definition = IntegrationMapping.ToDefinition(command.Mapping);
        await MappingRules.RequireAsync(definition, analyser.Id, null, mappings, lookup, cancellationToken);

        var mapping = AnalyserTestMapping.Map(analyser, definition, clock.UtcNow);
        mappings.Add(mapping);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(mapping.Id);
    }
}

public sealed class UpdateAnalyserTestMappingHandler(
    IAnalyserTestMappingRepository mappings,
    IIntegrationReferenceLookup lookup,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<UpdateAnalyserTestMappingCommand>
{
    public async Task Handle(UpdateAnalyserTestMappingCommand command, CancellationToken cancellationToken)
    {
        var mapping = await MappingRules.LoadAsync(mappings, command.AnalyserId, command.MappingId, cancellationToken);
        var definition = IntegrationMapping.ToDefinition(command.Mapping);
        await MappingRules.RequireAsync(definition, mapping.AnalyserId, mapping.Id, mappings, lookup, cancellationToken);

        mapping.Update(definition, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class SetMappingAutoSendHandler(IAnalyserTestMappingRepository mappings, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<SetMappingAutoSendCommand>
{
    public async Task Handle(SetMappingAutoSendCommand command, CancellationToken cancellationToken)
    {
        var mapping = await MappingRules.LoadAsync(mappings, command.AnalyserId, command.MappingId, cancellationToken);
        mapping.SetAutoSend(command.Enabled, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ReplaceMappingConversionsHandler(IAnalyserTestMappingRepository mappings, IUnitOfWork unitOfWork) : ICommandHandler<ReplaceMappingConversionsCommand>
{
    public async Task Handle(ReplaceMappingConversionsCommand command, CancellationToken cancellationToken)
    {
        var mapping = await MappingRules.LoadAsync(mappings, command.AnalyserId, command.MappingId, cancellationToken);
        mapping.ReplaceConversions(command.Conversions.Select(conversion => new ConversionSpec(conversion.FromValue, conversion.ToValue)).ToList());
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class SetMappingRangeOverrideHandler(IAnalyserTestMappingRepository mappings, IUnitOfWork unitOfWork) : ICommandHandler<SetMappingRangeOverrideCommand>
{
    public async Task Handle(SetMappingRangeOverrideCommand command, CancellationToken cancellationToken)
    {
        var mapping = await MappingRules.LoadAsync(mappings, command.AnalyserId, command.MappingId, cancellationToken);
        mapping.SetRangeOverride(command.RangeOverride is { } range ? new RangeOverride(range.Low, range.High, range.Text) : null);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class DeactivateMappingHandler(IAnalyserTestMappingRepository mappings, IUnitOfWork unitOfWork) : ICommandHandler<DeactivateMappingCommand>
{
    public async Task Handle(DeactivateMappingCommand command, CancellationToken cancellationToken)
    {
        var mapping = await MappingRules.LoadAsync(mappings, command.AnalyserId, command.MappingId, cancellationToken);
        mapping.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ReactivateMappingHandler(IAnalyserTestMappingRepository mappings, IUnitOfWork unitOfWork) : ICommandHandler<ReactivateMappingCommand>
{
    public async Task Handle(ReactivateMappingCommand command, CancellationToken cancellationToken)
    {
        var mapping = await MappingRules.LoadAsync(mappings, command.AnalyserId, command.MappingId, cancellationToken);
        mapping.Reactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
