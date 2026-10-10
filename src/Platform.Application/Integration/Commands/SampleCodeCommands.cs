using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;
using Platform.Domain.Integration;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Integration.Commands;

// Analyser sample codes (CP-60; intake row S1).

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Edit)]
public sealed record AddAnalyserSampleCodeCommand(Guid AnalyserId, Guid SampleTypeId, string Code) : ICommand<CreatedResource>;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Edit)]
public sealed record ChangeAnalyserSampleCodeCommand(Guid AnalyserId, Guid SampleCodeId, string Code) : ICommand;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Manage)]
public sealed record DeactivateAnalyserSampleCodeCommand(Guid AnalyserId, Guid SampleCodeId) : ICommand;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Manage)]
public sealed record ReactivateAnalyserSampleCodeCommand(Guid AnalyserId, Guid SampleCodeId) : ICommand;

public sealed class AddAnalyserSampleCodeValidator : AbstractValidator<AddAnalyserSampleCodeCommand>
{
    public AddAnalyserSampleCodeValidator()
    {
        RuleFor(command => command.AnalyserId).NotEmpty();
        RuleFor(command => command.SampleTypeId).NotEmpty();
        RuleFor(command => command.Code).NotEmpty().MaximumLength(AnalyserSampleCode.MaxCodeLength);
    }
}

public sealed class ChangeAnalyserSampleCodeValidator : AbstractValidator<ChangeAnalyserSampleCodeCommand>
{
    public ChangeAnalyserSampleCodeValidator()
    {
        RuleFor(command => command.AnalyserId).NotEmpty();
        RuleFor(command => command.SampleCodeId).NotEmpty();
        RuleFor(command => command.Code).NotEmpty().MaximumLength(AnalyserSampleCode.MaxCodeLength);
    }
}

public sealed class DeactivateAnalyserSampleCodeValidator : AbstractValidator<DeactivateAnalyserSampleCodeCommand>
{
    public DeactivateAnalyserSampleCodeValidator()
    {
        RuleFor(command => command.AnalyserId).NotEmpty();
        RuleFor(command => command.SampleCodeId).NotEmpty();
    }
}

public sealed class ReactivateAnalyserSampleCodeValidator : AbstractValidator<ReactivateAnalyserSampleCodeCommand>
{
    public ReactivateAnalyserSampleCodeValidator()
    {
        RuleFor(command => command.AnalyserId).NotEmpty();
        RuleFor(command => command.SampleCodeId).NotEmpty();
    }
}

internal static class SampleCodeRules
{
    public static async Task<AnalyserSampleCode> LoadAsync(IAnalyserSampleCodeRepository codes, Guid analyserId, Guid sampleCodeId, CancellationToken cancellationToken) =>
        await codes.GetInAnalyserAsync(analyserId, sampleCodeId, cancellationToken)
        ?? throw new DomainException(IntegrationCodes.SampleCodeNotFound, "Sample code not found.", DomainFault.NotFound);
}

public sealed class AddAnalyserSampleCodeHandler(
    IAnalyserRepository analysers,
    IAnalyserSampleCodeRepository codes,
    IIntegrationReferenceLookup lookup,
    IUnitOfWork unitOfWork) : ICommandHandler<AddAnalyserSampleCodeCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(AddAnalyserSampleCodeCommand command, CancellationToken cancellationToken)
    {
        var analyser = await AnalyserReferences.LoadAsync(analysers, command.AnalyserId, cancellationToken);
        await IntegrationReferences.RequireAsync(lookup, ReferenceKind.SampleType, command.SampleTypeId, cancellationToken);
        if (await codes.ExistsAsync(analyser.Id, command.SampleTypeId, cancellationToken))
        {
            throw new DomainException(IntegrationCodes.SampleCodeExists, "This sample type already has a code on this analyser.", DomainFault.Conflict);
        }

        var code = AnalyserSampleCode.Create(analyser, command.SampleTypeId, command.Code);
        codes.Add(code);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(code.Id);
    }
}

public sealed class ChangeAnalyserSampleCodeHandler(IAnalyserSampleCodeRepository codes, IUnitOfWork unitOfWork) : ICommandHandler<ChangeAnalyserSampleCodeCommand>
{
    public async Task Handle(ChangeAnalyserSampleCodeCommand command, CancellationToken cancellationToken)
    {
        var code = await SampleCodeRules.LoadAsync(codes, command.AnalyserId, command.SampleCodeId, cancellationToken);
        code.ChangeCode(command.Code);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class DeactivateAnalyserSampleCodeHandler(IAnalyserSampleCodeRepository codes, IUnitOfWork unitOfWork) : ICommandHandler<DeactivateAnalyserSampleCodeCommand>
{
    public async Task Handle(DeactivateAnalyserSampleCodeCommand command, CancellationToken cancellationToken)
    {
        var code = await SampleCodeRules.LoadAsync(codes, command.AnalyserId, command.SampleCodeId, cancellationToken);
        code.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ReactivateAnalyserSampleCodeHandler(IAnalyserSampleCodeRepository codes, IUnitOfWork unitOfWork) : ICommandHandler<ReactivateAnalyserSampleCodeCommand>
{
    public async Task Handle(ReactivateAnalyserSampleCodeCommand command, CancellationToken cancellationToken)
    {
        var code = await SampleCodeRules.LoadAsync(codes, command.AnalyserId, command.SampleCodeId, cancellationToken);
        code.Reactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
