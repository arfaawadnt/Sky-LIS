using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;
using Platform.Domain.Treasury;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Treasury.Commands;

// Treasury master (T6–T9). Each handler: load, call the aggregate, commit once (01 §3.3).

[RequirePermissionPolicy(PermissionCatalog.Treasury, PermissionAction.Manage)]
public sealed record CreateTreasuryCommand(string Code, string NameEn, string NameAr, Guid BranchId) : ICommand<CreatedResource>;

public sealed class CreateTreasuryValidator : AbstractValidator<CreateTreasuryCommand>
{
    public CreateTreasuryValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(Domain.Treasury.Treasury.MaxCodeLength);
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.BranchId).NotEmpty();
    }
}

public sealed class CreateTreasuryHandler(ITreasuryRepository treasuries, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<CreateTreasuryCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateTreasuryCommand command, CancellationToken cancellationToken)
    {
        var code = Domain.Treasury.Treasury.CleanCode(command.Code);
        if (await treasuries.CodeExistsAsync(code, cancellationToken))
        {
            throw new DomainException(TreasuryCodes.CodeExists, $"A treasury with code '{code}' already exists.", DomainFault.Conflict);
        }

        var treasury = Domain.Treasury.Treasury.Create(code, new LocalizedText(command.NameEn, command.NameAr), command.BranchId, clock.UtcNow);
        treasuries.Add(treasury);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(treasury.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Treasury, PermissionAction.Manage)]
public sealed record RenameTreasuryCommand(Guid TreasuryId, string NameEn, string NameAr) : ICommand;

public sealed class RenameTreasuryValidator : AbstractValidator<RenameTreasuryCommand>
{
    public RenameTreasuryValidator()
    {
        RuleFor(command => command.TreasuryId).NotEmpty();
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocalizedText.MaxLength);
    }
}

public sealed class RenameTreasuryHandler(ITreasuryRepository treasuries, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<RenameTreasuryCommand>
{
    public async Task Handle(RenameTreasuryCommand command, CancellationToken cancellationToken)
    {
        var treasury = await treasuries.GetForUpdateAsync(command.TreasuryId, cancellationToken)
            ?? throw new DomainException(TreasuryCodes.TreasuryNotFound, "Treasury not found.", DomainFault.NotFound);

        treasury.Rename(new LocalizedText(command.NameEn, command.NameAr), clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Treasury, PermissionAction.Manage)]
public sealed record DeactivateTreasuryCommand(Guid TreasuryId) : ICommand;

public sealed class DeactivateTreasuryValidator : AbstractValidator<DeactivateTreasuryCommand>
{
    public DeactivateTreasuryValidator() => RuleFor(command => command.TreasuryId).NotEmpty();
}

public sealed class DeactivateTreasuryHandler(ITreasuryRepository treasuries, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<DeactivateTreasuryCommand>
{
    public async Task Handle(DeactivateTreasuryCommand command, CancellationToken cancellationToken)
    {
        var treasury = await treasuries.GetForUpdateAsync(command.TreasuryId, cancellationToken)
            ?? throw new DomainException(TreasuryCodes.TreasuryNotFound, "Treasury not found.", DomainFault.NotFound);

        treasury.Deactivate(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Treasury, PermissionAction.Manage)]
public sealed record ReactivateTreasuryCommand(Guid TreasuryId) : ICommand;

public sealed class ReactivateTreasuryValidator : AbstractValidator<ReactivateTreasuryCommand>
{
    public ReactivateTreasuryValidator() => RuleFor(command => command.TreasuryId).NotEmpty();
}

public sealed class ReactivateTreasuryHandler(ITreasuryRepository treasuries, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<ReactivateTreasuryCommand>
{
    public async Task Handle(ReactivateTreasuryCommand command, CancellationToken cancellationToken)
    {
        var treasury = await treasuries.GetForUpdateAsync(command.TreasuryId, cancellationToken)
            ?? throw new DomainException(TreasuryCodes.TreasuryNotFound, "Treasury not found.", DomainFault.NotFound);

        treasury.Reactivate(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>The authenticated actor of a command, or the coded refusal (01 §3.3).</summary>
internal static class Actor
{
    public static Guid Of(ICurrentUser user) =>
        user.UserId ?? throw new DomainException(ProblemCodes.AuthenticatedActorRequired, "An authenticated user is required.", DomainFault.Unauthenticated);
}
