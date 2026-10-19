using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Billing.Commands;
using Platform.Contracts.Common;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Billing.Policies;

/// <summary>
/// P2 (change request 2026-10-19, P-C10): when a client is provisioned, then its cash desks start with the four default
/// payment methods (CASH, CARD, POS, TRANSFER), so the first registration can take money without a set-up step. The
/// <see cref="TenantProvisioned"/> row carries no tenant (the client aggregate is platform-wide), so the policy scopes
/// itself to the new tenant without elevation. Idempotent on the code: a tenant that already holds a default keeps it,
/// and a concurrent seeding that loses on the unique index is accepted as done. The platform tenant takes none.
/// </summary>
public sealed class SeedPaymentMethodsPolicy(IPaymentMethodRepository methods, ICurrentTenantSetter tenantScope, IUnitOfWork unitOfWork, IClock clock) : IDomainEventHandler<TenantProvisioned>
{
    public async Task HandleAsync(TenantProvisioned domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        if (domainEvent.TenantId == Guid.Empty || TenantIds.IsPlatform(domainEvent.TenantId))
        {
            return;
        }

        using (tenantScope.ForTenant(domainEvent.TenantId))
        {
            var missing = await PaymentMethodDefaults.MissingAsync(methods, clock.UtcNow, cancellationToken);
            if (missing.Count == 0)
            {
                return;
            }

            foreach (var definition in missing)
            {
                methods.Add(definition);
            }

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DomainException refusal) when (refusal.Code == ProblemCodes.UniqueViolation)
            {
                // Seeded by the defaults endpoint or a retry of this very event in the meantime: the methods exist.
            }
        }
    }
}
