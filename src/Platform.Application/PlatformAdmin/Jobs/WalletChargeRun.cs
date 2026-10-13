using System.Globalization;
using Platform.Application.Abstractions.Audit;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.Authorization;
using Platform.Domain.PlatformAdmin;

namespace Platform.Application.PlatformAdmin.Jobs;

// W2 Monthly subscription charge (intake LIS-INT-PLT-001 §7; SRS SA-05 "the monthly fee debits the wallet"). The run is
// a system job (Lis:Jobs:WalletCharge, hosted by the WebApi) and the same code answers SA-05 "Run charges now".
// This file is deliberately outside a Commands folder: the run commits once PER WALLET (WalletCharger, one
// SaveChangesAsync each, in its own scope) so one client's failure never rolls back another client's charge; the
// command handler itself changes nothing (TransitionAtomicityTests covers the Commands folders).

/// <summary>The charge run: lists the due wallets and charges each in its own platform-tenant scope. Implemented by the infrastructure job.</summary>
public interface IWalletChargeRun
{
    /// <summary>One pass at the clock's time; serialised in-process, idempotent across processes (see <see cref="WalletCharger"/>).</summary>
    Task<WalletChargeRunSummary> RunOnceAsync(CancellationToken cancellationToken);
}

/// <summary>SA-05 "Run charges now" (<c>tenancy.manage</c>, platform tenant only): runs the same pass as the hosted job and returns its summary.</summary>
[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.Manage)]
public sealed record RunWalletChargesCommand : ICommand<WalletChargeRunSummary>, IPlatformAdministrationRequest;

public sealed class RunWalletChargesHandler(IWalletChargeRun run, ICurrentTenant currentTenant) : ICommandHandler<RunWalletChargesCommand, WalletChargeRunSummary>
{
    public async Task<WalletChargeRunSummary> Handle(RunWalletChargesCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        return await run.RunOnceAsync(cancellationToken);
    }
}

/// <summary>
/// Charges ONE client's wallet for the period that is due, in the platform tenant's scope (the saas tables carry no
/// tenant and no RLS: no elevation is involved). Rules, in order:
/// <list type="number">
/// <item>No wallet → skipped (<c>no-wallet</c>). Next charge still in the future → skipped (<c>not-due</c>): the period was
/// charged by another run in the meantime.</item>
/// <item>No client record or plan → failed (<c>no-plan</c>).</item>
/// <item>Trial or Inactive client → skipped (<c>trial</c> / <c>inactive</c>); the wallet stays due and is charged once the
/// client is Active (one period per run).</item>
/// <item>Plan currency differs from the wallet currency → failed (<c>currency-mismatch</c>): conversion is open question
/// OQ-02 / intake Q5, so nothing is debited.</item>
/// <item>Otherwise <see cref="Wallet.ChargeSubscription"/> debits the plan's monthly fee (kind Subscription, reference
/// <c>SUB-yyyy-MM-PLAN</c>), even below zero (the domain rule: the portal keeps working; metered usage pauses at zero,
/// WAL-010), moves the next charge one month on, raises <c>WalletDebited</c>, and the unit of work commits once.</item>
/// </list>
/// Idempotency per tenant and period: the charge happens only when the stored next charge date is due, and the same
/// commit moves that date on. Two runs that load the same wallet race on its row version (xmin): the loser gets
/// CONCURRENCY-409, nothing of it is written, and its retry reads the moved date (<c>not-due</c>). No period key or
/// migration is needed. A failure writes <c>WALLET_CHARGE_FAILED</c> to the security ledger and changes nothing.
/// </summary>
public sealed class WalletCharger(IWalletRepository wallets, ICurrentTenant currentTenant, IUnitOfWork unitOfWork, ISecurityEventLog events)
{
    public const string Charged = "Charged";
    public const string Skipped = "Skipped";
    public const string Failed = "Failed";

    public async Task<WalletChargeRunItem> ChargeAsync(Guid clientId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var wallet = await wallets.GetByTenantForUpdateAsync(clientId, cancellationToken);
        if (wallet is null)
        {
            return new WalletChargeRunItem(clientId, Skipped, "no-wallet", null, null, null);
        }

        var period = Period(wallet.NextChargeAtUtc);
        if (wallet.NextChargeAtUtc > nowUtc)
        {
            return new WalletChargeRunItem(clientId, Skipped, "not-due", period, null, null);
        }

        var terms = await wallets.FindChargeTermsAsync(clientId, cancellationToken);
        if (terms is null)
        {
            return await FailAsync(clientId, "no-plan", period, cancellationToken);
        }

        if (terms.Status != TenantStatus.Active)
        {
            return new WalletChargeRunItem(clientId, Skipped, terms.Status == TenantStatus.Trial ? "trial" : "inactive", period, null, null);
        }

        if (!string.Equals(terms.PlanCurrencyCode.Trim(), wallet.CurrencyCode.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return await FailAsync(clientId, "currency-mismatch", period, cancellationToken);
        }

        var transaction = wallet.ChargeSubscription(terms.MonthlyFee, $"SUB-{period}-{terms.PlanCode}", nowUtc);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new WalletChargeRunItem(clientId, Charged, null, period, transaction.Amount, transaction.BalanceAfter);
    }

    /// <summary>The period a due date charges: its UTC month (<c>yyyy-MM</c>).</summary>
    public static string Period(DateTimeOffset dueAtUtc) => dueAtUtc.ToUniversalTime().ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>The summary of a pass from its rows.</summary>
    public static WalletChargeRunSummary Summarise(DateTimeOffset runAtUtc, IReadOnlyList<WalletChargeRunItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new WalletChargeRunSummary(
            runAtUtc,
            items.Count(item => item.Outcome == Charged),
            items.Count(item => item.Outcome == Skipped),
            items.Count(item => item.Outcome == Failed),
            items.Count(item => item.Outcome == Charged && item.BalanceAfter <= 0),
            items);
    }

    private async Task<WalletChargeRunItem> FailAsync(Guid clientId, string reason, string period, CancellationToken cancellationToken)
    {
        await events.WriteAsync(new SecurityEvent(PlatformAdminSecurityEvents.WalletChargeFailed, SecurityEvent.Warning, Detail: $"tenant:{clientId} period:{period} reason:{reason}"), cancellationToken);
        return new WalletChargeRunItem(clientId, Failed, reason, period, null, null);
    }
}
