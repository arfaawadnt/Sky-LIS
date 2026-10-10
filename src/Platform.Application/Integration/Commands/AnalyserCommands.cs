using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Contracts.Integration;
using Platform.Domain.Authorization;
using Platform.Domain.Integration;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Integration.Commands;

// Analysers (CP-58; intake rows A1–A3). Validators check shape; the aggregate decides (pitfall P-10).

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Edit)]
public sealed record CreateAnalyserCommand(SaveAnalyserRequest Analyser) : ICommand<CreatedResource>;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Edit)]
public sealed record UpdateAnalyserCommand(Guid AnalyserId, SaveAnalyserRequest Analyser) : ICommand;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Manage)]
public sealed record DeactivateAnalyserCommand(Guid AnalyserId) : ICommand;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Manage)]
public sealed record ReactivateAnalyserCommand(Guid AnalyserId) : ICommand;

/// <summary>Shape of an analyser body: names, enum names, lengths; the link's rules (9600,n,8,1 …) are the domain's.</summary>
public sealed class SaveAnalyserRequestValidator : AbstractValidator<SaveAnalyserRequest>
{
    public SaveAnalyserRequestValidator()
    {
        RuleFor(request => request.Code).NotEmpty().MaximumLength(Analyser.MaxCodeLength);
        RuleFor(request => request.NameEn).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(request => request.NameAr).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(request => request.ClinicalGroupId).NotEmpty();
        RuleFor(request => request.Mode).NotEmpty().Must(IntegrationNames.IsName<AnalyserMode>).WithMessage("Unknown mode.");
        RuleFor(request => request.Connection).NotNull();
        RuleFor(request => request.Connection.Kind).NotEmpty().Must(IntegrationNames.IsName<ConnectionKind>).WithMessage("Unknown connection kind.").When(request => request.Connection is not null);
        RuleFor(request => request.Connection.TcpRole).Must(IntegrationNames.IsName<TcpRole>).WithMessage("Unknown TCP role.").When(request => request.Connection is not null && !string.IsNullOrWhiteSpace(request.Connection.TcpRole));
        RuleFor(request => request.Connection.SerialSettings).MaximumLength(AnalyserConnection.MaxSerialSettingsLength).When(request => request.Connection is not null);
        RuleFor(request => request.Connection.SerialPort).MaximumLength(AnalyserConnection.MaxSerialPortLength).When(request => request.Connection is not null);
        RuleFor(request => request.Connection.Host).MaximumLength(AnalyserConnection.MaxHostLength).When(request => request.Connection is not null);
        RuleFor(request => request.Connection.FolderPath).MaximumLength(AnalyserConnection.MaxFolderLength).When(request => request.Connection is not null);
        RuleFor(request => request.PrintCopies).InclusiveBetween(0, Analyser.MaxPrintCopies);
        RuleFor(request => request.Flags).Must(flags => flags is null || flags.Count <= Analyser.MaxFlags).WithMessage($"At most {Analyser.MaxFlags} flags.");
        RuleForEach(request => request.Flags).ChildRules(flag =>
        {
            flag.RuleFor(item => item.Code).NotEmpty().MaximumLength(Analyser.MaxFlagCodeLength);
            flag.RuleFor(item => item.Action).NotEmpty().Must(IntegrationNames.IsName<FlagAction>).WithMessage("Unknown flag action.");
            flag.RuleFor(item => item.Description).MaximumLength(Analyser.MaxFlagDescriptionLength);
        });
    }
}

public sealed class CreateAnalyserValidator : AbstractValidator<CreateAnalyserCommand>
{
    public CreateAnalyserValidator() => RuleFor(command => command.Analyser).NotNull().SetValidator(new SaveAnalyserRequestValidator());
}

public sealed class UpdateAnalyserValidator : AbstractValidator<UpdateAnalyserCommand>
{
    public UpdateAnalyserValidator()
    {
        RuleFor(command => command.AnalyserId).NotEmpty();
        RuleFor(command => command.Analyser).NotNull().SetValidator(new SaveAnalyserRequestValidator());
    }
}

public sealed class DeactivateAnalyserValidator : AbstractValidator<DeactivateAnalyserCommand>
{
    public DeactivateAnalyserValidator() => RuleFor(command => command.AnalyserId).NotEmpty();
}

public sealed class ReactivateAnalyserValidator : AbstractValidator<ReactivateAnalyserCommand>
{
    public ReactivateAnalyserValidator() => RuleFor(command => command.AnalyserId).NotEmpty();
}

/// <summary>The ids an analyser references: catalogue group and sample type, library model, own site agent.</summary>
internal static class AnalyserReferences
{
    public static async Task RequireAsync(AnalyserDefinition definition, IIntegrationReferenceLookup lookup, ISiteAgentRepository agents, CancellationToken cancellationToken)
    {
        await IntegrationReferences.RequireAsync(lookup, ReferenceKind.ClinicalGroup, definition.ClinicalGroupId, cancellationToken);
        await IntegrationReferences.RequireAsync(lookup, ReferenceKind.SampleType, definition.DefaultSampleTypeId, cancellationToken);
        if (definition.AnalyserModelId is { } model && model != Guid.Empty && !await lookup.AnalyserModelExistsAsync(model, cancellationToken))
        {
            throw new DomainException(IntegrationCodes.AnalyserModelUnknown, "The analyser model is not in the platform library.", DomainFault.Validation);
        }

        if (definition.SiteAgentId is { } agentId && agentId != Guid.Empty && await agents.GetForUpdateAsync(agentId, cancellationToken) is null)
        {
            throw new DomainException(IntegrationCodes.AgentNotFound, "Site agent not found.", DomainFault.NotFound);
        }
    }

    public static async Task<Analyser> LoadAsync(IAnalyserRepository analysers, Guid analyserId, CancellationToken cancellationToken) =>
        await analysers.GetForUpdateAsync(analyserId, cancellationToken)
        ?? throw new DomainException(IntegrationCodes.AnalyserNotFound, "Analyser not found.", DomainFault.NotFound);
}

public sealed class CreateAnalyserHandler(
    IAnalyserRepository analysers,
    ISiteAgentRepository agents,
    IIntegrationReferenceLookup lookup,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<CreateAnalyserCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateAnalyserCommand command, CancellationToken cancellationToken)
    {
        var code = Analyser.CleanCode(command.Analyser.Code);
        if (await analysers.CodeExistsAsync(code, cancellationToken))
        {
            throw new DomainException(IntegrationCodes.AnalyserCodeExists, $"An analyser with code '{code}' already exists.", DomainFault.Conflict);
        }

        var definition = IntegrationMapping.ToDefinition(command.Analyser);
        await AnalyserReferences.RequireAsync(definition, lookup, agents, cancellationToken);

        var analyser = Analyser.Create(code, definition, clock.UtcNow);
        analysers.Add(analyser);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(analyser.Id);
    }
}

public sealed class UpdateAnalyserHandler(
    IAnalyserRepository analysers,
    ISiteAgentRepository agents,
    IIntegrationReferenceLookup lookup,
    IUnitOfWork unitOfWork) : ICommandHandler<UpdateAnalyserCommand>
{
    public async Task Handle(UpdateAnalyserCommand command, CancellationToken cancellationToken)
    {
        var analyser = await AnalyserReferences.LoadAsync(analysers, command.AnalyserId, cancellationToken);
        var definition = IntegrationMapping.ToDefinition(command.Analyser);
        await AnalyserReferences.RequireAsync(definition, lookup, agents, cancellationToken);

        analyser.Update(definition);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class DeactivateAnalyserHandler(IAnalyserRepository analysers, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<DeactivateAnalyserCommand>
{
    public async Task Handle(DeactivateAnalyserCommand command, CancellationToken cancellationToken)
    {
        var analyser = await AnalyserReferences.LoadAsync(analysers, command.AnalyserId, cancellationToken);
        analyser.Deactivate(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ReactivateAnalyserHandler(IAnalyserRepository analysers, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<ReactivateAnalyserCommand>
{
    public async Task Handle(ReactivateAnalyserCommand command, CancellationToken cancellationToken)
    {
        var analyser = await AnalyserReferences.LoadAsync(analysers, command.AnalyserId, cancellationToken);
        analyser.Reactivate(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
