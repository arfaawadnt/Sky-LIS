namespace Platform.Application.Registration;

/// <summary>
/// The payment balance of a visit (BR-14, MODULES.md §5, single definition owned by Billing):
/// <c>Remaining = PatientShareTotal − Paid + Refunded</c> where <c>Paid</c> counts posted payments only;
/// <c>Refundable = max(0, −Remaining)</c>. A negative remaining is a refund due.
/// </summary>
public sealed record VisitBalance(Guid VisitId, decimal PatientShareTotal, decimal Paid, decimal Refunded, decimal Remaining, decimal Refundable);

/// <summary>
/// Read port declared for Registration (search registrations, the success dialog, visit completion) and implemented by
/// Billing (<c>Platform.Infrastructure.Persistence.Billing.VisitBalanceLookup</c>, over <c>app.visits</c>,
/// <c>app.payments</c> and <c>app.refunds</c> of the current tenant). Unknown visits are absent; a visit without any
/// movement has Paid = Refunded = 0. A raw-SQL stand-in Registration may ship carries <c>[FallbackAdapter]</c> and is
/// replaced by the Billing adapter automatically.
/// </summary>
public interface IVisitBalanceLookup
{
    Task<VisitBalance?> GetAsync(Guid visitId, CancellationToken cancellationToken);

    /// <summary>Balances of the known ids (at most 1000 per call), keyed by visit id.</summary>
    Task<IReadOnlyDictionary<Guid, VisitBalance>> GetManyAsync(IReadOnlyCollection<Guid> visitIds, CancellationToken cancellationToken);
}
