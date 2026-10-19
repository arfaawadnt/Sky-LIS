using Platform.Contracts.Pricing;

namespace Platform.Application.Pricing;

/// <summary>
/// Read port of BR-06 for Registration, Billing and Claims (later waves): the patient and payer share of each service
/// for a payer/tier, as of today (intake §5 "Quote resolution"). Total: unknown, inactive, out-of-effect or mismatched
/// payers/tiers and unpriced services answer <c>Priced = false</c> with zero amounts, never an exception; callers validate
/// the payer and tier through <see cref="IPayerLookup"/> first.
/// <list type="bullet">
/// <item>tier given → that tier (a given <paramref name="payerId"/> must be the tier's payer);</item>
/// <item>payer only → the payer's default tier, else its only active tier, else nothing is priced;</item>
/// <item>neither → the tenant's cash payer's default tier; before it is provisioned, the default list at 100 % / 0 %.</item>
/// </list>
/// A tier with an applied contract pricing answers its stored prices; the cash default tier without one answers the
/// default list (the active base list with the highest year, ties: most recently created; active lines only) at 100 % / 0 %;
/// any other tier without an applied pricing prices nothing. The result has one line per distinct requested id, in order.
/// </summary>
public interface IPriceLookup
{
    Task<IReadOnlyList<PriceQuoteLine>> QuoteAsync(Guid? payerId, Guid? contractTierId, IReadOnlyList<Guid> serviceIds, CancellationToken cancellationToken);
}

/// <summary>Read port onto payers and tiers for other modules (Registration: payer/tier pickers and validation; Claims: names).</summary>
public interface IPayerLookup
{
    Task<PayerSummary?> FindPayerAsync(Guid payerId, CancellationToken cancellationToken);

    Task<ContractTierSummary?> FindTierAsync(Guid contractTierId, CancellationToken cancellationToken);

    /// <summary>The tiers of a payer (active and inactive), default first, then by English name.</summary>
    Task<IReadOnlyList<ContractTierSummary>> ListTiersAsync(Guid payerId, CancellationToken cancellationToken);

    /// <summary>The cash payer and its default tier, or <see langword="null"/> until <c>EnsureDefaultPayerCommand</c> has run (intake Q1).</summary>
    Task<DefaultPayerIds?> FindDefaultAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Read side of the service prices page (CR 2026-10-19 C22): one catalogue service with its line in every base list and its
/// derived price in every contract tier. The adapter reads <c>app.services</c> by raw SQL (same-wave rule) and the module's own
/// tables; <see langword="null"/> when the service is not the tenant's. <c>Currency</c> is left empty for the handler to fill.
/// </summary>
public interface IServicePricesReader
{
    Task<ServicePricesView?> ReadAsync(Guid serviceId, CancellationToken cancellationToken);
}
