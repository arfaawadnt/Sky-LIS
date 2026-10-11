using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.PlatformAdmin.Commands;

// Plans and client wallets (SA-05, intake §7 M1/M2, W1, W5).

[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.Manage)]
public sealed record CreatePlanCommand(string Code, string NameEn, string NameAr, decimal MonthlyFee, string CurrencyCode, int MaxBranches, int MaxUsers, int SmsQuota, int MaxAnalysers)
    : ICommand<CreatedResource>, IPlatformAdministrationRequest;

public sealed class CreatePlanValidator : AbstractValidator<CreatePlanCommand>
{
    public CreatePlanValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(Plan.MaxCodeLength);
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(Plan.MaxNameLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(Plan.MaxNameLength);
        RuleFor(command => command.MonthlyFee).GreaterThanOrEqualTo(0);
        RuleFor(command => command.CurrencyCode).NotEmpty().Length(3);
        RuleFor(command => command.MaxBranches).InclusiveBetween(1, Plan.MaxLimit);
        RuleFor(command => command.MaxUsers).InclusiveBetween(1, Plan.MaxLimit);
        RuleFor(command => command.SmsQuota).InclusiveBetween(0, Plan.MaxLimit);
        RuleFor(command => command.MaxAnalysers).InclusiveBetween(0, Plan.MaxLimit);
    }
}

public sealed class CreatePlanHandler(IPlanRepository plans, ICurrentTenant currentTenant, IUnitOfWork unitOfWork) : ICommandHandler<CreatePlanCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreatePlanCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var code = Plan.CleanCode(command.Code);
        if (await plans.CodeExistsAsync(code, cancellationToken))
        {
            throw new DomainException(PlatformAdminCodes.PlanCodeExists, $"A plan with code '{code}' already exists.", DomainFault.Conflict);
        }

        var plan = Plan.Create(code, new PlanTerms(new LocalizedText(command.NameEn, command.NameAr), command.MonthlyFee, command.CurrencyCode, command.MaxBranches, command.MaxUsers, command.SmsQuota, command.MaxAnalysers));
        plans.Add(plan);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(plan.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.Manage)]
public sealed record UpdatePlanCommand(Guid PlanId, string NameEn, string NameAr, decimal MonthlyFee, string CurrencyCode, int MaxBranches, int MaxUsers, int SmsQuota, int MaxAnalysers)
    : ICommand, IPlatformAdministrationRequest;

public sealed class UpdatePlanValidator : AbstractValidator<UpdatePlanCommand>
{
    public UpdatePlanValidator()
    {
        RuleFor(command => command.PlanId).NotEmpty();
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(Plan.MaxNameLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(Plan.MaxNameLength);
        RuleFor(command => command.MonthlyFee).GreaterThanOrEqualTo(0);
        RuleFor(command => command.CurrencyCode).NotEmpty().Length(3);
        RuleFor(command => command.MaxBranches).InclusiveBetween(1, Plan.MaxLimit);
        RuleFor(command => command.MaxUsers).InclusiveBetween(1, Plan.MaxLimit);
        RuleFor(command => command.SmsQuota).InclusiveBetween(0, Plan.MaxLimit);
        RuleFor(command => command.MaxAnalysers).InclusiveBetween(0, Plan.MaxLimit);
    }
}

public sealed class UpdatePlanHandler(IPlanRepository plans, ICurrentTenant currentTenant, IUnitOfWork unitOfWork) : ICommandHandler<UpdatePlanCommand>
{
    public async Task Handle(UpdatePlanCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var plan = await plans.GetForUpdateAsync(command.PlanId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.PlanNotFound, "Plan not found.", DomainFault.NotFound);
        plan.Update(new PlanTerms(new LocalizedText(command.NameEn, command.NameAr), command.MonthlyFee, command.CurrencyCode, command.MaxBranches, command.MaxUsers, command.SmsQuota, command.MaxAnalysers));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>M2 Deactivate (<paramref name="Active"/> false) or reactivate a plan; clients keep an inactive plan, new ones cannot get it.</summary>
[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.Manage)]
public sealed record SetPlanActiveCommand(Guid PlanId, bool Active) : ICommand, IPlatformAdministrationRequest;

public sealed class SetPlanActiveValidator : AbstractValidator<SetPlanActiveCommand>
{
    public SetPlanActiveValidator() => RuleFor(command => command.PlanId).NotEmpty();
}

public sealed class SetPlanActiveHandler(IPlanRepository plans, ICurrentTenant currentTenant, IUnitOfWork unitOfWork) : ICommandHandler<SetPlanActiveCommand>
{
    public async Task Handle(SetPlanActiveCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var plan = await plans.GetForUpdateAsync(command.PlanId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.PlanNotFound, "Plan not found.", DomainFault.NotFound);
        if (command.Active)
        {
            plan.Reactivate();
        }
        else
        {
            plan.Deactivate();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>W1 Top up a client's wallet; settles the laboratory's pending request when <paramref name="TopUpRequestId"/> is given.</summary>
[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.Manage)]
public sealed record TopUpWalletCommand(Guid ClientId, decimal Amount, string Reference, string? Note, Guid? TopUpRequestId) : ICommand<CreatedResource>, IPlatformAdministrationRequest;

public sealed class TopUpWalletValidator : AbstractValidator<TopUpWalletCommand>
{
    public TopUpWalletValidator()
    {
        RuleFor(command => command.ClientId).NotEmpty();
        RuleFor(command => command.Amount).GreaterThan(0).LessThanOrEqualTo(100_000_000m)
            .Must(amount => decimal.Round(amount, 2) == amount).WithMessage("The amount has at most two decimals.");   // C-19: refused, never rounded
        RuleFor(command => command.Reference).NotEmpty().MaximumLength(Wallet.MaxReferenceLength);
        RuleFor(command => command.Note).MaximumLength(Wallet.MaxNoteLength);
    }
}

public sealed class TopUpWalletHandler(IWalletRepository wallets, ICurrentTenant currentTenant, ICurrentUser user, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<TopUpWalletCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(TopUpWalletCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var wallet = await wallets.GetByTenantForUpdateAsync(command.ClientId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.WalletNotFound, "The client has no wallet.", DomainFault.NotFound);

        WalletTopUpRequest? request = null;
        if (command.TopUpRequestId is { } requestId)
        {
            request = await wallets.FindTopUpRequestAsync(wallet.Id, requestId, cancellationToken)
                ?? throw new DomainException(PlatformAdminCodes.TopUpRequestNotFound, "Top-up request not found.", DomainFault.NotFound);
            wallet.Attach(request);
        }

        var transaction = wallet.TopUp(command.Amount, command.Reference, command.Note, user.UserId, clock.UtcNow, request);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(transaction.Id);
    }
}

/// <summary>W5 Decline a laboratory's pending top-up request.</summary>
[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.Manage)]
public sealed record DeclineTopUpRequestCommand(Guid ClientId, Guid TopUpRequestId) : ICommand, IPlatformAdministrationRequest;

public sealed class DeclineTopUpRequestValidator : AbstractValidator<DeclineTopUpRequestCommand>
{
    public DeclineTopUpRequestValidator()
    {
        RuleFor(command => command.ClientId).NotEmpty();
        RuleFor(command => command.TopUpRequestId).NotEmpty();
    }
}

public sealed class DeclineTopUpRequestHandler(IWalletRepository wallets, ICurrentTenant currentTenant, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<DeclineTopUpRequestCommand>
{
    public async Task Handle(DeclineTopUpRequestCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var wallet = await wallets.GetByTenantForUpdateAsync(command.ClientId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.WalletNotFound, "The client has no wallet.", DomainFault.NotFound);
        var request = await wallets.FindTopUpRequestAsync(wallet.Id, command.TopUpRequestId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.TopUpRequestNotFound, "Top-up request not found.", DomainFault.NotFound);

        wallet.Attach(request);
        wallet.DeclineTopUp(request, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
