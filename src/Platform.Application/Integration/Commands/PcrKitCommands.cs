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

// PCR kits (CP-62; intake rows K1–K3).

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Edit)]
public sealed record DefinePcrKitCommand(SavePcrKitRequest Kit) : ICommand<CreatedResource>;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Edit)]
public sealed record UpdatePcrKitCommand(Guid KitId, SavePcrKitRequest Kit) : ICommand;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Manage)]
public sealed record DeactivatePcrKitCommand(Guid KitId) : ICommand;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Manage)]
public sealed record ReactivatePcrKitCommand(Guid KitId) : ICommand;

public sealed class SavePcrKitRequestValidator : AbstractValidator<SavePcrKitRequest>
{
    public SavePcrKitRequestValidator()
    {
        RuleFor(request => request.Name).NotEmpty().MaximumLength(PcrKit.MaxNameLength);
        RuleFor(request => request.AnalyserId).NotEmpty();
        RuleFor(request => request.AnalyserTestCode).NotEmpty().MaximumLength(AnalyserTestMapping.MaxCodeLength);
        RuleFor(request => request.SerialLock).MaximumLength(PcrKit.MaxSerialLength);
        RuleFor(request => request.ValueColumn).NotEmpty().MaximumLength(PcrKit.MaxColumnLength);
        RuleFor(request => request.Layout).NotNull();
        RuleFor(request => request.Layout.SampleColumn).NotEmpty().MaximumLength(PcrKit.MaxColumnLength).When(request => request.Layout is not null);
        RuleFor(request => request.Layout.GeneColumn).NotEmpty().MaximumLength(PcrKit.MaxColumnLength).When(request => request.Layout is not null);
        RuleFor(request => request.Rules).NotNull();
        RuleFor(request => request.Genes).NotNull().Must(genes => genes is { Count: >= 1 and <= PcrKit.MaxGenes }).WithMessage($"A kit has 1 to {PcrKit.MaxGenes} genes.");
        RuleForEach(request => request.Genes).ChildRules(gene =>
        {
            gene.RuleFor(item => item.Name).NotEmpty().MaximumLength(PcrKit.MaxGeneNameLength);
            gene.RuleFor(item => item.PositiveCtMax).GreaterThan(0m).LessThanOrEqualTo(PcrKit.MaxCt);
            gene.RuleFor(item => item.BorderlineCtMax).GreaterThan(0m).LessThanOrEqualTo(PcrKit.MaxCt);
        });
        RuleFor(request => request.ResultFolder).NotEmpty().MaximumLength(PcrKit.MaxFolderLength);
        RuleFor(request => request.BackupFolder).NotEmpty().MaximumLength(PcrKit.MaxFolderLength);
        RuleFor(request => request.FailedFolder).NotEmpty().MaximumLength(PcrKit.MaxFolderLength);
    }
}

public sealed class DefinePcrKitValidator : AbstractValidator<DefinePcrKitCommand>
{
    public DefinePcrKitValidator() => RuleFor(command => command.Kit).NotNull().SetValidator(new SavePcrKitRequestValidator());
}

public sealed class UpdatePcrKitValidator : AbstractValidator<UpdatePcrKitCommand>
{
    public UpdatePcrKitValidator()
    {
        RuleFor(command => command.KitId).NotEmpty();
        RuleFor(command => command.Kit).NotNull().SetValidator(new SavePcrKitRequestValidator());
    }
}

public sealed class DeactivatePcrKitValidator : AbstractValidator<DeactivatePcrKitCommand>
{
    public DeactivatePcrKitValidator() => RuleFor(command => command.KitId).NotEmpty();
}

public sealed class ReactivatePcrKitValidator : AbstractValidator<ReactivatePcrKitCommand>
{
    public ReactivatePcrKitValidator() => RuleFor(command => command.KitId).NotEmpty();
}

internal static class PcrKitRules
{
    public static async Task<PcrKit> LoadAsync(IPcrKitRepository kits, Guid kitId, CancellationToken cancellationToken) =>
        await kits.GetForUpdateAsync(kitId, cancellationToken)
        ?? throw new DomainException(IntegrationCodes.KitNotFound, "PCR kit not found.", DomainFault.NotFound);

    public static async Task RequireUniqueAsync(IPcrKitRepository kits, string name, Guid? exceptKitId, CancellationToken cancellationToken)
    {
        var cleaned = PcrKit.CleanName(name);
        if (await kits.NameExistsAsync(cleaned, exceptKitId, cancellationToken))
        {
            throw new DomainException(IntegrationCodes.KitNameExists, $"A PCR kit named '{cleaned}' already exists.", DomainFault.Conflict);
        }
    }
}

public sealed class DefinePcrKitHandler(IAnalyserRepository analysers, IPcrKitRepository kits, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<DefinePcrKitCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(DefinePcrKitCommand command, CancellationToken cancellationToken)
    {
        var analyser = await AnalyserReferences.LoadAsync(analysers, command.Kit.AnalyserId, cancellationToken);
        await PcrKitRules.RequireUniqueAsync(kits, command.Kit.Name, null, cancellationToken);

        var kit = PcrKit.Define(analyser, IntegrationMapping.ToDefinition(command.Kit), clock.UtcNow);
        kits.Add(kit);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(kit.Id);
    }
}

public sealed class UpdatePcrKitHandler(IAnalyserRepository analysers, IPcrKitRepository kits, IUnitOfWork unitOfWork) : ICommandHandler<UpdatePcrKitCommand>
{
    public async Task Handle(UpdatePcrKitCommand command, CancellationToken cancellationToken)
    {
        var kit = await PcrKitRules.LoadAsync(kits, command.KitId, cancellationToken);
        var analyser = await AnalyserReferences.LoadAsync(analysers, command.Kit.AnalyserId, cancellationToken);
        await PcrKitRules.RequireUniqueAsync(kits, command.Kit.Name, kit.Id, cancellationToken);

        kit.Update(analyser, IntegrationMapping.ToDefinition(command.Kit));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class DeactivatePcrKitHandler(IPcrKitRepository kits, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<DeactivatePcrKitCommand>
{
    public async Task Handle(DeactivatePcrKitCommand command, CancellationToken cancellationToken)
    {
        var kit = await PcrKitRules.LoadAsync(kits, command.KitId, cancellationToken);
        kit.Deactivate(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ReactivatePcrKitHandler(IPcrKitRepository kits, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<ReactivatePcrKitCommand>
{
    public async Task Handle(ReactivatePcrKitCommand command, CancellationToken cancellationToken)
    {
        var kit = await PcrKitRules.LoadAsync(kits, command.KitId, cancellationToken);
        kit.Reactivate(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
