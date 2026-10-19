using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Billing;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;
using Platform.Domain.Billing;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Billing.Commands;

// Payment method definitions (set-up list CP-30b, change request 2026-10-19 P-C10; intake LIS-INT-BIL-001 §17, T10–T14).
// Validators check shape; the aggregate decides (pitfall P-10). Each handler: load, call the aggregate, commit once.

/// <summary>T10 Define a payment method (<c>billing.manage</c>); the code is upper-cased and unique per tenant (BIL-121).</summary>
[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.Manage)]
public sealed record CreatePaymentMethodCommand(string Code, string NameEn, string NameAr, string Kind, bool RequiresReference, int? Sequence) : ICommand<CreatedResource>;

public sealed class CreatePaymentMethodValidator : AbstractValidator<CreatePaymentMethodCommand>
{
    public CreatePaymentMethodValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(PaymentMethodDefinition.MaxCodeLength);
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(PaymentMethodDefinition.MaxNameLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(PaymentMethodDefinition.MaxNameLength);
        RuleFor(command => command.Kind).NotEmpty().IsEnumName(typeof(PaymentMethod), caseSensitive: false);
        RuleFor(command => command.Sequence).InclusiveBetween(0, PaymentMethodDefinition.MaxSequence).When(command => command.Sequence is not null);
    }
}

public sealed class CreatePaymentMethodHandler(IPaymentMethodRepository methods, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<CreatePaymentMethodCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreatePaymentMethodCommand command, CancellationToken cancellationToken)
    {
        var code = PaymentMethodDefinition.CleanCode(command.Code);
        if (await methods.CodeExistsAsync(code, cancellationToken))
        {
            throw new DomainException(BillingCodes.MethodCodeExists, $"A payment method with code '{code}' already exists.", DomainFault.Conflict);
        }

        var sequence = command.Sequence ?? await NextSequenceAsync(methods, cancellationToken);
        var definition = PaymentMethodDefinition.Define(code, new LocalizedText(command.NameEn, command.NameAr), BillingNames.Method(command.Kind), command.RequiresReference, sequence, clock.UtcNow);
        methods.Add(definition);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(definition.Id);
    }

    /// <summary>After the last option of the pick list (the options are the active ones; a new method goes last).</summary>
    private static async Task<int> NextSequenceAsync(IPaymentMethodRepository methods, CancellationToken cancellationToken)
    {
        var options = await methods.OptionsAsync(cancellationToken);
        return Math.Min(options.Count + 1, PaymentMethodDefinition.MaxSequence);
    }
}

/// <summary>T11 Update names, kind, reference rule and order (<c>billing.manage</c>); BIL-052 when re-kinding the last active cash method.</summary>
[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.Manage)]
public sealed record UpdatePaymentMethodCommand(Guid MethodId, string NameEn, string NameAr, string Kind, bool RequiresReference, int Sequence) : ICommand;

public sealed class UpdatePaymentMethodValidator : AbstractValidator<UpdatePaymentMethodCommand>
{
    public UpdatePaymentMethodValidator()
    {
        RuleFor(command => command.MethodId).NotEmpty();
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(PaymentMethodDefinition.MaxNameLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(PaymentMethodDefinition.MaxNameLength);
        RuleFor(command => command.Kind).NotEmpty().IsEnumName(typeof(PaymentMethod), caseSensitive: false);
        RuleFor(command => command.Sequence).InclusiveBetween(0, PaymentMethodDefinition.MaxSequence);
    }
}

public sealed class UpdatePaymentMethodHandler(IPaymentMethodRepository methods, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<UpdatePaymentMethodCommand>
{
    public async Task Handle(UpdatePaymentMethodCommand command, CancellationToken cancellationToken)
    {
        var definition = await methods.GetForUpdateAsync(command.MethodId, cancellationToken) ?? throw BillingNotFound.Method();
        var otherCash = await methods.OtherActiveCashExistsAsync(definition.Id, cancellationToken);
        definition.Update(new LocalizedText(command.NameEn, command.NameAr), BillingNames.Method(command.Kind), command.RequiresReference, command.Sequence, otherCash, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>T12 Deactivate (<c>billing.manage</c>); idempotent; BIL-052 for the last active cash method.</summary>
[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.Manage)]
public sealed record DeactivatePaymentMethodCommand(Guid MethodId) : ICommand;

public sealed class DeactivatePaymentMethodValidator : AbstractValidator<DeactivatePaymentMethodCommand>
{
    public DeactivatePaymentMethodValidator() => RuleFor(command => command.MethodId).NotEmpty();
}

public sealed class DeactivatePaymentMethodHandler(IPaymentMethodRepository methods, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<DeactivatePaymentMethodCommand>
{
    public async Task Handle(DeactivatePaymentMethodCommand command, CancellationToken cancellationToken)
    {
        var definition = await methods.GetForUpdateAsync(command.MethodId, cancellationToken) ?? throw BillingNotFound.Method();
        var otherCash = await methods.OtherActiveCashExistsAsync(definition.Id, cancellationToken);
        definition.Deactivate(otherCash, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>T13 Reactivate (<c>billing.manage</c>); idempotent.</summary>
[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.Manage)]
public sealed record ReactivatePaymentMethodCommand(Guid MethodId) : ICommand;

public sealed class ReactivatePaymentMethodValidator : AbstractValidator<ReactivatePaymentMethodCommand>
{
    public ReactivatePaymentMethodValidator() => RuleFor(command => command.MethodId).NotEmpty();
}

public sealed class ReactivatePaymentMethodHandler(IPaymentMethodRepository methods, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<ReactivatePaymentMethodCommand>
{
    public async Task Handle(ReactivatePaymentMethodCommand command, CancellationToken cancellationToken)
    {
        var definition = await methods.GetForUpdateAsync(command.MethodId, cancellationToken) ?? throw BillingNotFound.Method();
        definition.Reactivate(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// T14 Ensure the default methods (<c>billing.manage</c>; as <c>POST /api/v1/pricing/payers/default</c>): adds the default
/// definitions whose code the tenant does not have yet (CASH, CARD, POS, TRANSFER), idempotent and safe to call from
/// the set-up page when its list is empty. A concurrent first call loses on the unique index and answers 0.
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.Manage)]
public sealed record EnsureDefaultPaymentMethodsCommand : ICommand<PaymentMethodDefaultsResult>;

public sealed class EnsureDefaultPaymentMethodsHandler(IPaymentMethodRepository methods, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<EnsureDefaultPaymentMethodsCommand, PaymentMethodDefaultsResult>
{
    public async Task<PaymentMethodDefaultsResult> Handle(EnsureDefaultPaymentMethodsCommand command, CancellationToken cancellationToken)
    {
        var missing = await PaymentMethodDefaults.MissingAsync(methods, clock.UtcNow, cancellationToken);
        foreach (var definition in missing)
        {
            methods.Add(definition);
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException refusal) when (refusal.Code == ProblemCodes.UniqueViolation && missing.Count > 0)
        {
            // Another request seeded the defaults between the read and the commit: they exist, which is what was asked.
            return new PaymentMethodDefaultsResult(0);
        }

        return new PaymentMethodDefaultsResult(missing.Count);
    }
}

/// <summary>The default definitions a tenant still lacks (by code), shared by the command and the provisioning policy.</summary>
internal static class PaymentMethodDefaults
{
    public static async Task<IReadOnlyList<PaymentMethodDefinition>> MissingAsync(IPaymentMethodRepository methods, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var existing = await methods.ExistingCodesAsync(cancellationToken);
        var known = new HashSet<string>(existing, StringComparer.Ordinal);
        return PaymentMethodDefinition.Defaults(nowUtc).Where(definition => !known.Contains(definition.Code)).ToList();
    }
}
