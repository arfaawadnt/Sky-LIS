using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Contracts.Pricing;
using Platform.Domain.Authorization;
using Platform.Domain.Pricing;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Pricing.Commands;

// Payers (P1–P4, P8). Each handler: load, call the aggregate, commit once (01 §3.3). Validators check shape only.
// The contact fields (CR 2026-10-19 C19) are optional on both commands; the aggregate cleans and validates them (PRC-017 / PRC-018).

[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.Create)]
public sealed record CreatePayerCommand(string NameEn, string NameAr, string SearchKey, string Type, bool WebPortalEnabled, string? WebUsername, string? Phone1 = null, string? Phone2 = null, string? WhatsApp = null, string? Email = null) : ICommand<CreatedResource>;

public sealed class CreatePayerValidator : AbstractValidator<CreatePayerCommand>
{
    public CreatePayerValidator()
    {
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.SearchKey).NotEmpty().MaximumLength(Payer.MaxSearchKeyLength);
        RuleFor(command => command.Type).NotEmpty().IsEnumName(typeof(PayerType), caseSensitive: false);
        RuleFor(command => command.WebUsername).MaximumLength(Payer.MaxWebUsernameLength);
        RuleFor(command => command.WebUsername).NotEmpty().When(command => command.WebPortalEnabled);
        RuleFor(command => command.Phone1).MaximumLength(Payer.MaxContactNumberLength);
        RuleFor(command => command.Phone2).MaximumLength(Payer.MaxContactNumberLength);
        RuleFor(command => command.WhatsApp).MaximumLength(Payer.MaxContactNumberLength);
        RuleFor(command => command.Email).MaximumLength(Payer.MaxEmailLength);
    }
}

public sealed class CreatePayerHandler(IPayerRepository payers, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<CreatePayerCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreatePayerCommand command, CancellationToken cancellationToken)
    {
        var searchKey = Payer.CleanSearchKey(command.SearchKey);
        var username = Payer.CleanWebUsername(command.WebPortalEnabled, command.WebUsername);
        await PayerUniqueness.RequireAsync(payers, searchKey, username, null, cancellationToken);

        var contacts = new PayerContacts(command.Phone1, command.Phone2, command.WhatsApp, command.Email);
        var payer = Payer.Create(new LocalizedText(command.NameEn, command.NameAr), searchKey, Enum.Parse<PayerType>(command.Type, ignoreCase: true), command.WebPortalEnabled, username, contacts, clock.UtcNow);
        payers.Add(payer);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(payer.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.Edit)]
public sealed record UpdatePayerCommand(Guid PayerId, string NameEn, string NameAr, string SearchKey, string Type, bool WebPortalEnabled, string? WebUsername, string? Phone1 = null, string? Phone2 = null, string? WhatsApp = null, string? Email = null) : ICommand;

public sealed class UpdatePayerValidator : AbstractValidator<UpdatePayerCommand>
{
    public UpdatePayerValidator()
    {
        RuleFor(command => command.PayerId).NotEmpty();
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.SearchKey).NotEmpty().MaximumLength(Payer.MaxSearchKeyLength);
        RuleFor(command => command.Type).NotEmpty().IsEnumName(typeof(PayerType), caseSensitive: false);
        RuleFor(command => command.WebUsername).MaximumLength(Payer.MaxWebUsernameLength);
        RuleFor(command => command.WebUsername).NotEmpty().When(command => command.WebPortalEnabled);
        RuleFor(command => command.Phone1).MaximumLength(Payer.MaxContactNumberLength);
        RuleFor(command => command.Phone2).MaximumLength(Payer.MaxContactNumberLength);
        RuleFor(command => command.WhatsApp).MaximumLength(Payer.MaxContactNumberLength);
        RuleFor(command => command.Email).MaximumLength(Payer.MaxEmailLength);
    }
}

public sealed class UpdatePayerHandler(IPayerRepository payers, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<UpdatePayerCommand>
{
    public async Task Handle(UpdatePayerCommand command, CancellationToken cancellationToken)
    {
        var payer = await payers.GetForUpdateAsync(command.PayerId, cancellationToken)
            ?? throw new DomainException(PricingCodes.PayerNotFound, "Payer not found.", DomainFault.NotFound);

        var searchKey = Payer.CleanSearchKey(command.SearchKey);
        var username = Payer.CleanWebUsername(command.WebPortalEnabled, command.WebUsername);
        await PayerUniqueness.RequireAsync(payers, searchKey, username, payer.Id, cancellationToken);

        var contacts = new PayerContacts(command.Phone1, command.Phone2, command.WhatsApp, command.Email);
        payer.Update(new LocalizedText(command.NameEn, command.NameAr), searchKey, Enum.Parse<PayerType>(command.Type, ignoreCase: true), command.WebPortalEnabled, username, contacts, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.Manage)]
public sealed record DeactivatePayerCommand(Guid PayerId) : ICommand;

public sealed class DeactivatePayerValidator : AbstractValidator<DeactivatePayerCommand>
{
    public DeactivatePayerValidator() => RuleFor(command => command.PayerId).NotEmpty();
}

public sealed class DeactivatePayerHandler(IPayerRepository payers, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<DeactivatePayerCommand>
{
    public async Task Handle(DeactivatePayerCommand command, CancellationToken cancellationToken)
    {
        var payer = await payers.GetForUpdateAsync(command.PayerId, cancellationToken)
            ?? throw new DomainException(PricingCodes.PayerNotFound, "Payer not found.", DomainFault.NotFound);

        payer.Deactivate(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.Manage)]
public sealed record ReactivatePayerCommand(Guid PayerId) : ICommand;

public sealed class ReactivatePayerValidator : AbstractValidator<ReactivatePayerCommand>
{
    public ReactivatePayerValidator() => RuleFor(command => command.PayerId).NotEmpty();
}

public sealed class ReactivatePayerHandler(IPayerRepository payers, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<ReactivatePayerCommand>
{
    public async Task Handle(ReactivatePayerCommand command, CancellationToken cancellationToken)
    {
        var payer = await payers.GetForUpdateAsync(command.PayerId, cancellationToken)
            ?? throw new DomainException(PricingCodes.PayerNotFound, "Payer not found.", DomainFault.NotFound);

        payer.Reactivate(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// P8 Ensure the tenant's cash payer (intake Q1): idempotent, no input. Sent by the quote endpoint when neither payer nor
/// tier is given and by the Payers page when it opens; gated by <c>pricing.view</c> because it only provisions a tenant
/// invariant. A concurrent first call loses on the partial unique index and answers the winner's ids.
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.View)]
public sealed record EnsureDefaultPayerCommand : ICommand<DefaultPayerIds>;

public sealed class EnsureDefaultPayerHandler(IPayerRepository payers, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<EnsureDefaultPayerCommand, DefaultPayerIds>
{
    private const int MaxKeyAttempts = 9;

    public async Task<DefaultPayerIds> Handle(EnsureDefaultPayerCommand command, CancellationToken cancellationToken)
    {
        var existing = await payers.FindDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var searchKey = await FreeSearchKeyAsync(cancellationToken);
        var payer = Payer.CreateDefaultCash(searchKey, clock.UtcNow);
        payers.Add(payer);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException refusal) when (refusal.Code == ProblemCodes.UniqueViolation)
        {
            // Another request provisioned the cash payer between the read and the commit: answer the winner's ids.
            var winner = await payers.FindDefaultAsync(cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return winner;
        }

        return new DefaultPayerIds(payer.Id, payer.Tiers.Single().Id, Created: true);
    }

    private async Task<string> FreeSearchKeyAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxKeyAttempts; attempt++)
        {
            var candidate = attempt == 1 ? Payer.DefaultSearchKey : $"{Payer.DefaultSearchKey}-{attempt}";
            if (!await payers.SearchKeyExistsAsync(candidate, null, cancellationToken))
            {
                return candidate;
            }
        }

        throw new DomainException(PricingCodes.SearchKeyExists, "No free search key is left for the cash payer; rename the payers using CASH … CASH-9.", DomainFault.Conflict);
    }
}

/// <summary>INV-PRC-001 and INV-PRC-002 decided by the handler with their codes; the unique indexes are the backstop.</summary>
internal static class PayerUniqueness
{
    public static async Task RequireAsync(IPayerRepository payers, string searchKey, string? webUsername, Guid? exceptPayerId, CancellationToken cancellationToken)
    {
        if (await payers.SearchKeyExistsAsync(searchKey, exceptPayerId, cancellationToken))
        {
            throw new DomainException(PricingCodes.SearchKeyExists, $"Another payer uses the search key '{searchKey}'.", DomainFault.Conflict);
        }

        if (webUsername is not null && await payers.WebUsernameExistsAsync(webUsername, exceptPayerId, cancellationToken))
        {
            throw new DomainException(PricingCodes.WebUsernameExists, $"Another payer uses the web username '{webUsername}'.", DomainFault.Conflict);
        }
    }
}
