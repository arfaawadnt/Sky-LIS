using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Pricing;

/// <summary>One term of a tier's formula, in priority order: a base list with the patient and payer factors.</summary>
public sealed record PricingFormulaTerm(Guid BaseListId, decimal PatientFactor, decimal PayerFactor);

/// <summary>What one Apply did (C2): rows priced after the run, rows added, updated and removed, excluded rows kept.</summary>
public sealed record ContractApplyOutcome(int Priced, int Added, int Updated, int Removed, int ExcludedKept);

/// <summary>
/// The pricing of one contract tier (SRS CP-34, LDM "Client Price List"): up to three base lists with patient and payer
/// factors, and the derived price of every service (BR-07). Prices change only by Apply, negotiation, restore or copy,
/// never by editing a base list (INV-PRC-011). Excluded (negotiated) prices survive every Apply (INV-PRC-010).
/// One per tier (INV-PRC-007). The formula terms live in <c>app.contract_pricing_sources</c> and the prices in
/// <c>app.contract_prices</c>; both change only through this root.
/// </summary>
public sealed class ContractPricing : AggregateRoot, ITenantScoped
{
    public const int MaxFormulaTerms = 3;
    public const int MaxAlertLength = 500;

    private readonly List<PricingSource> _sources = [];
    private readonly List<ContractPrice> _prices = [];

    private ContractPricing()
    {
    }

    private ContractPricing(Guid id, Guid payerId, Guid contractTierId)
        : base(id)
    {
        PayerId = payerId;
        ContractTierId = contractTierId;
    }

    public Guid TenantId { get; private set; }

    public Guid PayerId { get; private set; }

    /// <summary>Unique per tenant (INV-PRC-007).</summary>
    public Guid ContractTierId { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public DateOnly? EffectiveTo { get; private set; }

    /// <summary>When the derived prices were last computed (Apply or copy); <see langword="null"/> until then.</summary>
    public DateTimeOffset? AppliedAtUtc { get; private set; }

    /// <summary>
    /// The report design every report of a visit under this tier prints in (LIS-CR-010, C-14), or <see langword="null"/> to
    /// follow the report template, then the laboratory parameter (<see cref="Catalogue.ReportDesigns.Resolve"/>). Not part of
    /// the formula: set by C7 only, kept by Apply and by a copy onto this tier.
    /// </summary>
    public Catalogue.ReportDesign? ReportDesign { get; private set; }

    public uint RowVersion { get; private set; }

    public IReadOnlyCollection<PricingSource> Sources => _sources.AsReadOnly();

    public IReadOnlyCollection<ContractPrice> Prices => _prices.AsReadOnly();

    /// <summary>C1 on a tier without pricing (<c>pricing.edit</c>): records the formula; nothing is priced until Apply.</summary>
    public static ContractPricing Create(Guid payerId, Guid contractTierId, DateOnly effectiveFrom, DateOnly? effectiveTo, IReadOnlyList<PricingFormulaTerm> terms, DateTimeOffset nowUtc)
    {
        Require(payerId != Guid.Empty && contractTierId != Guid.Empty, PricingCodes.TierNotFound, "A contract pricing belongs to a payer's tier.", DomainFault.Validation);
        var pricing = new ContractPricing(Guid.CreateVersion7(), payerId, contractTierId);
        pricing.SetFormula(effectiveFrom, effectiveTo, terms, nowUtc);
        return pricing;
    }

    /// <summary>
    /// C1 Set formula (<c>pricing.edit</c>): 1–3 distinct lists (PRC-008/009), factors 0–10 (PRC-007), EffectiveTo not
    /// before EffectiveFrom (PRC-010). Prices are untouched until the next Apply (BR-07). The handler checked that the lists exist.
    /// </summary>
    public void SetFormula(DateOnly effectiveFrom, DateOnly? effectiveTo, IReadOnlyList<PricingFormulaTerm> terms, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(terms);
        Require(terms.Count is >= 1 and <= MaxFormulaTerms, PricingCodes.FormulaSizeInvalid, $"A formula uses 1 to {MaxFormulaTerms} base lists.", DomainFault.Validation);
        Require(terms.All(term => term.BaseListId != Guid.Empty), PricingCodes.FormulaListUnknown, "Every formula term needs a base list.", DomainFault.Validation);
        Require(terms.Select(term => term.BaseListId).Distinct().Count() == terms.Count, PricingCodes.FormulaDuplicateList, "A base list appears more than once in the formula.", DomainFault.Validation);
        foreach (var term in terms)
        {
            PriceMath.RequireFactor(term.PatientFactor);
            PriceMath.RequireFactor(term.PayerFactor);
        }

        Require(effectiveTo is null || effectiveTo.Value >= effectiveFrom, PricingCodes.EffectiveDatesInvalid, "The end date must not be before the start date.", DomainFault.Validation);

        _sources.Clear();
        for (var index = 0; index < terms.Count; index++)
        {
            _sources.Add(new PricingSource(Id, index + 1, terms[index].BaseListId, terms[index].PatientFactor, terms[index].PayerFactor));
        }

        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        Raise(new ContractFormulaSet(Id, PayerId, ContractTierId, terms.Count, nowUtc));
    }

    /// <summary>
    /// C2 Apply (<c>pricing.approve</c>, gate G1): recomputes every non-excluded service from the referenced ACTIVE lists
    /// (first list holding an active line wins), removes rows no list holds any more, keeps excluded rows (BR-07).
    /// <paramref name="lists"/> must contain the snapshot of every list of the formula that still exists.
    /// </summary>
    public ContractApplyOutcome Apply(IReadOnlyDictionary<Guid, PriceListSnapshot> lists, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(lists);
        Require(_sources.Count > 0, PricingCodes.FormulaRequired, "Set the formula before applying.", DomainFault.Conflict);
        Require(_sources.Any(source => lists.TryGetValue(source.BasePriceListId, out var list) && list.IsActive), PricingCodes.NoActiveList, "None of the formula's base lists is active.", DomainFault.Conflict);

        var derived = Derive(lists);
        var (added, updated, removed, kept) = (0, 0, 0, 0);
        foreach (var row in _prices.ToList())
        {
            if (row.IsExcluded)
            {
                kept++;
                continue;
            }

            if (derived.TryGetValue(row.ServiceId, out var price))
            {
                if (row.SetDerived(price.ListPrice, price.PatientShare, price.PayerShare, price.SourceListId))
                {
                    updated++;
                }
            }
            else
            {
                _prices.Remove(row);
                removed++;
            }
        }

        var held = _prices.Select(row => row.ServiceId).ToHashSet();
        foreach (var (serviceId, price) in derived.Where(pair => !held.Contains(pair.Key)))
        {
            _prices.Add(ContractPrice.Derived(Id, serviceId, price.ListPrice, price.PatientShare, price.PayerShare, price.SourceListId));
            added++;
        }

        AppliedAtUtc = nowUtc;
        var outcome = new ContractApplyOutcome(_prices.Count, added, updated, removed, kept);
        Raise(new ContractPricingApplied(Id, PayerId, ContractTierId, outcome.Priced, added, updated, removed, kept, nowUtc));
        return outcome;
    }

    /// <summary>C3 Negotiate (<c>pricing.edit</c>): a hand-typed price immune to Apply (LDM "Exclude"). Creates the row when the contract does not price the service yet.</summary>
    public void Negotiate(Guid serviceId, decimal patientShare, decimal payerShare, DateTimeOffset nowUtc)
    {
        Require(serviceId != Guid.Empty, PricingCodes.ServiceUnknown, "A negotiated price needs a service.", DomainFault.Validation);
        PriceMath.RequireAmount(patientShare, PricingCodes.SharesInvalid, $"Shares must be between 0 and {PriceMath.MaxAmount} with at most two decimals.");
        PriceMath.RequireAmount(payerShare, PricingCodes.SharesInvalid, $"Shares must be between 0 and {PriceMath.MaxAmount} with at most two decimals.");
        Require(patientShare + payerShare <= PriceMath.MaxAmount, PricingCodes.SharesInvalid, $"The total must not exceed {PriceMath.MaxAmount}.", DomainFault.Validation);

        var row = FindPrice(serviceId);
        if (row is null)
        {
            _prices.Add(ContractPrice.Negotiated(Id, serviceId, patientShare, payerShare));
        }
        else
        {
            row.Negotiate(patientShare, payerShare);
        }

        Raise(new ContractPriceNegotiated(Id, ContractTierId, serviceId, patientShare, payerShare, nowUtc));
    }

    /// <summary>
    /// C4 Clear exclusion (<c>pricing.edit</c>): restores the service from the formula now (LDM "Restore from main price
    /// list"); when no active list of the formula holds it, the row is removed (intake Q7).
    /// </summary>
    public void ClearExclusion(Guid serviceId, IReadOnlyDictionary<Guid, PriceListSnapshot> lists, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(lists);
        var row = RequirePrice(serviceId);
        Require(row.IsExcluded, PricingCodes.PriceNotExcluded, "The price is not excluded.", DomainFault.Conflict);

        if (Derive(lists).TryGetValue(serviceId, out var price))
        {
            row.SetDerived(price.ListPrice, price.PatientShare, price.PayerShare, price.SourceListId);
        }
        else
        {
            _prices.Remove(row);
        }

        Raise(new ContractPriceExclusionCleared(Id, ContractTierId, serviceId, nowUtc));
    }

    /// <summary>C5 Set or clear the alert condition shown at ordering (<c>pricing.edit</c>); the service must be priced (PRC-032).</summary>
    public void SetAlert(Guid serviceId, string? alertCondition)
    {
        var row = RequirePrice(serviceId);
        var cleaned = string.IsNullOrWhiteSpace(alertCondition) ? null : alertCondition.Trim();
        Require(cleaned is null || cleaned.Length <= MaxAlertLength, PricingCodes.AlertInvalid, $"An alert condition has at most {MaxAlertLength} characters.", DomainFault.Validation);
        row.SetAlert(cleaned);
    }

    /// <summary>C6 on a tier without pricing (<c>pricing.edit</c>, LDM "Client Copy"): a copy of another tier scaled by <paramref name="factor"/>.</summary>
    public static ContractPricing CopyOf(ContractPricing source, Guid payerId, Guid contractTierId, decimal factor, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(source);
        Require(source.ContractTierId != contractTierId, PricingCodes.CopySameTier, "A tier cannot be copied onto itself.", DomainFault.Conflict);
        var pricing = new ContractPricing(Guid.CreateVersion7(), payerId, contractTierId);
        pricing.CopyFrom(source, factor, nowUtc);
        return pricing;
    }

    /// <summary>
    /// C6 Copy from another tier (<c>pricing.edit</c>): the formula (factors × factor), the effective dates, every price
    /// (shares × factor), exclusions and alerts; the copy counts as applied now (intake Q8).
    /// </summary>
    public void CopyFrom(ContractPricing source, decimal factor, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(source);
        Require(source.ContractTierId != ContractTierId, PricingCodes.CopySameTier, "A tier cannot be copied onto itself.", DomainFault.Conflict);
        Require(factor > 0 && PriceMath.IsValidFactor(factor), PricingCodes.FactorInvalid, $"The copy factor must be above 0 and at most {PriceMath.MaxFactor}.", DomainFault.Validation);

        var terms = source._sources.OrderBy(term => term.Priority)
            .Select(term => new PricingFormulaTerm(term.BasePriceListId, ScaleFactor(term.PatientFactor, factor), ScaleFactor(term.PayerFactor, factor)))
            .ToList();
        var prices = source._prices
            .Select(row => (row, patient: ScaleAmount(row.PatientShare, factor), payer: ScaleAmount(row.PayerShare, factor)))
            .ToList();

        _sources.Clear();
        for (var index = 0; index < terms.Count; index++)
        {
            _sources.Add(new PricingSource(Id, index + 1, terms[index].BaseListId, terms[index].PatientFactor, terms[index].PayerFactor));
        }

        // Rows of services both tiers price are updated in place (no delete + insert of the same unique key).
        var incoming = prices.ToDictionary(entry => entry.row.ServiceId);
        _prices.RemoveAll(row => !incoming.ContainsKey(row.ServiceId));
        var existing = _prices.ToDictionary(row => row.ServiceId);
        foreach (var (row, patient, payer) in prices)
        {
            if (existing.TryGetValue(row.ServiceId, out var target))
            {
                target.CopyValues(row, patient, payer);
            }
            else
            {
                _prices.Add(ContractPrice.Copied(Id, row, patient, payer));
            }
        }

        EffectiveFrom = source.EffectiveFrom;
        EffectiveTo = source.EffectiveTo;
        AppliedAtUtc = nowUtc;
        Raise(new ContractPricingCopied(Id, ContractTierId, source.Id, source.ContractTierId, factor, nowUtc));
    }

    /// <summary>C7 Report design (<c>pricing.edit</c>, LIS-CR-010): sets or clears (null) the tier's report design override (PRC-047 for an unknown design).</summary>
    public void SetReportDesign(Catalogue.ReportDesign? design, DateTimeOffset nowUtc)
    {
        Require(design is null || Enum.IsDefined(design.Value), PricingCodes.ReportDesignInvalid, "The report design is unknown.", DomainFault.Validation);
        if (ReportDesign == design)
        {
            return;
        }

        ReportDesign = design;
        Raise(new ContractReportDesignSet(Id, ContractTierId, design, nowUtc));
    }

    /// <summary>The tier quotes its prices only between the effective dates, inclusive (intake Q5).</summary>
    public bool IsInEffect(DateOnly today) => today >= EffectiveFrom && (EffectiveTo is null || today <= EffectiveTo.Value);

    public ContractPrice? FindPrice(Guid serviceId) => _prices.FirstOrDefault(row => row.ServiceId == serviceId);

    /// <summary>The derivation of BR-07 over the formula's active lists: first list holding an active line wins; shares rounded to 0.01.</summary>
    private Dictionary<Guid, DerivedPrice> Derive(IReadOnlyDictionary<Guid, PriceListSnapshot> lists)
    {
        var derived = new Dictionary<Guid, DerivedPrice>();
        foreach (var source in _sources.OrderBy(source => source.Priority))
        {
            if (!lists.TryGetValue(source.BasePriceListId, out var list) || !list.IsActive)
            {
                continue;
            }

            foreach (var (serviceId, basePrice) in list.Prices)
            {
                derived.TryAdd(serviceId, new DerivedPrice(
                    basePrice,
                    PriceMath.RoundAmount(basePrice * source.PatientFactor),
                    PriceMath.RoundAmount(basePrice * source.PayerFactor),
                    list.ListId));
            }
        }

        return derived;
    }

    private ContractPrice RequirePrice(Guid serviceId) =>
        FindPrice(serviceId) ?? throw new DomainException(PricingCodes.ServiceNotPriced, "This contract does not price the service.", DomainFault.Conflict);

    private static decimal ScaleFactor(decimal value, decimal factor) => PriceMath.RequireFactor(Math.Round(value * factor, PriceMath.FactorScale, MidpointRounding.AwayFromZero));

    private static decimal ScaleAmount(decimal value, decimal factor) =>
        PriceMath.RequireAmount(PriceMath.RoundAmount(value * factor), PricingCodes.SharesInvalid, $"The copy would exceed {PriceMath.MaxAmount}.");

    private readonly record struct DerivedPrice(decimal ListPrice, decimal PatientShare, decimal PayerShare, Guid SourceListId);
}

/// <summary>One term of the formula (priority 1–3). Replaced as a whole by <see cref="ContractPricing.SetFormula"/>.</summary>
public sealed class PricingSource : Entity, ITenantScoped
{
    private PricingSource()
    {
    }

    internal PricingSource(Guid contractPricingId, int priority, Guid basePriceListId, decimal patientFactor, decimal payerFactor)
        : base(Guid.CreateVersion7())
    {
        ContractPricingId = contractPricingId;
        Priority = priority;
        BasePriceListId = basePriceListId;
        PatientFactor = patientFactor;
        PayerFactor = payerFactor;
    }

    public Guid TenantId { get; private set; }

    public Guid ContractPricingId { get; private set; }

    /// <summary>1 = first list (the main source), 2 and 3 = supplements for services the earlier lists do not hold.</summary>
    public int Priority { get; private set; }

    public Guid BasePriceListId { get; private set; }

    public decimal PatientFactor { get; private set; }

    public decimal PayerFactor { get; private set; }
}

/// <summary>The derived (or negotiated) price of one service in a tier. Changed only through <see cref="ContractPricing"/>.</summary>
public sealed class ContractPrice : Entity, ITenantScoped
{
    private ContractPrice()
    {
    }

    private ContractPrice(Guid contractPricingId, Guid serviceId, decimal listPrice, decimal patientShare, decimal payerShare, bool isExcluded, string? alertCondition, Guid? sourceListId)
        : base(Guid.CreateVersion7())
    {
        ContractPricingId = contractPricingId;
        ServiceId = serviceId;
        ListPrice = listPrice;
        PatientShare = patientShare;
        PayerShare = payerShare;
        IsExcluded = isExcluded;
        AlertCondition = alertCondition;
        SourceListId = sourceListId;
    }

    public Guid TenantId { get; private set; }

    public Guid ContractPricingId { get; private set; }

    public Guid ServiceId { get; private set; }

    /// <summary>The base price the shares were derived from (for a negotiated row without a derivation: patient + payer).</summary>
    public decimal ListPrice { get; private set; }

    public decimal PatientShare { get; private set; }

    public decimal PayerShare { get; private set; }

    /// <summary>A negotiated price kept through every Apply (BR-07).</summary>
    public bool IsExcluded { get; private set; }

    /// <summary>Text shown to the reception clerk when the service is ordered under this tier.</summary>
    public string? AlertCondition { get; private set; }

    /// <summary>The base list the derived price came from; <see langword="null"/> for a price negotiated before any derivation.</summary>
    public Guid? SourceListId { get; private set; }

    internal static ContractPrice Derived(Guid contractPricingId, Guid serviceId, decimal listPrice, decimal patientShare, decimal payerShare, Guid sourceListId) =>
        new(contractPricingId, serviceId, listPrice, patientShare, payerShare, isExcluded: false, alertCondition: null, sourceListId);

    internal static ContractPrice Negotiated(Guid contractPricingId, Guid serviceId, decimal patientShare, decimal payerShare) =>
        new(contractPricingId, serviceId, patientShare + payerShare, patientShare, payerShare, isExcluded: true, alertCondition: null, sourceListId: null);

    internal static ContractPrice Copied(Guid contractPricingId, ContractPrice source, decimal patientShare, decimal payerShare) =>
        new(contractPricingId, source.ServiceId, source.ListPrice, patientShare, payerShare, source.IsExcluded, source.AlertCondition, source.SourceListId);

    /// <summary>Returns true when anything changed; also lifts an exclusion (restore).</summary>
    internal bool SetDerived(decimal listPrice, decimal patientShare, decimal payerShare, Guid sourceListId)
    {
        if (ListPrice == listPrice && PatientShare == patientShare && PayerShare == payerShare && SourceListId == sourceListId && !IsExcluded)
        {
            return false;
        }

        ListPrice = listPrice;
        PatientShare = patientShare;
        PayerShare = payerShare;
        SourceListId = sourceListId;
        IsExcluded = false;
        return true;
    }

    internal void Negotiate(decimal patientShare, decimal payerShare)
    {
        PatientShare = patientShare;
        PayerShare = payerShare;
        IsExcluded = true;
    }

    internal void SetAlert(string? alertCondition) => AlertCondition = alertCondition;

    internal void CopyValues(ContractPrice source, decimal patientShare, decimal payerShare)
    {
        ListPrice = source.ListPrice;
        PatientShare = patientShare;
        PayerShare = payerShare;
        IsExcluded = source.IsExcluded;
        AlertCondition = source.AlertCondition;
        SourceListId = source.SourceListId;
    }
}
