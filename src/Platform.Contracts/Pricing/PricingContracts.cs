namespace Platform.Contracts.Pricing;

// Request and response shapes of the Pricing module (intake LIS-INT-PRC-001). Validation here is shape only; rules
// live in the aggregates. Enum values travel as their names: PayerType Cash|Credit|Insurance|Lab, CopyOperation Multiply|Add.

// ---- payers and tiers (CP-32) ----

/// <summary>The four contact values (CR 2026-10-19 C19) are optional: phones and WhatsApp as typed (≤ 32: digits, spaces, + - ( )), e-mail ≤ 254 of basic shape.</summary>
public sealed record CreatePayerRequest(string NameEn, string NameAr, string SearchKey, string Type, bool WebPortalEnabled, string? WebUsername, string? Phone1 = null, string? Phone2 = null, string? WhatsApp = null, string? Email = null);

public sealed record UpdatePayerRequest(string NameEn, string NameAr, string SearchKey, string Type, bool WebPortalEnabled, string? WebUsername, string? Phone1 = null, string? Phone2 = null, string? WhatsApp = null, string? Email = null);

public sealed record ContractTierRequest(string NameEn, string NameAr);

public sealed record ContractTierItem(Guid Id, Guid PayerId, string NameEn, string NameAr, bool IsActive, bool IsDefault);

public sealed record PayerListItem(
    Guid Id,
    string NameEn,
    string NameAr,
    string SearchKey,
    string Type,
    bool WebPortalEnabled,
    string? WebUsername,
    string? Phone1,
    string? Phone2,
    string? WhatsApp,
    string? Email,
    bool IsActive,
    bool IsDefault,
    IReadOnlyList<ContractTierItem> Tiers);

/// <summary>A tier with its payer, for pickers (contract pricing, copy-from, registration).</summary>
public sealed record TierOption(Guid Id, Guid PayerId, string PayerNameEn, string PayerNameAr, string PayerType, string NameEn, string NameAr, bool IsActive, bool PayerIsActive, bool IsDefault, bool HasPricing);

/// <summary>The tenant's cash payer and its default tier (intake Q1); <paramref name="Created"/> when this call provisioned them.</summary>
public sealed record DefaultPayerIds(Guid PayerId, Guid ContractTierId, bool Created);

// ---- read-port views (Registration, Billing, Claims) ----

public sealed record PayerSummary(Guid Id, string NameEn, string NameAr, string SearchKey, string Type, bool IsActive, bool IsDefault);

public sealed record ContractTierSummary(Guid Id, Guid PayerId, string PayerType, string NameEn, string NameAr, bool IsActive, bool PayerIsActive, bool IsDefault);

// ---- base price lists (CP-33) ----

public sealed record CreateBasePriceListRequest(string Code, string NameEn, string NameAr, int Year);

public sealed record UpdateBasePriceListRequest(string NameEn, string NameAr, int Year);

/// <summary>Copy into a new list (L4): <paramref name="Operation"/> Multiply|Add, <paramref name="RoundingStep"/> 0.01|0.25|0.5|1.</summary>
public sealed record CopyBasePriceListRequest(string Code, string NameEn, string NameAr, int Year, string Operation, decimal Value, decimal RoundingStep);

public sealed record PriceLineRequest(Guid ServiceId, decimal Price, bool IsActive);

public sealed record UpsertPriceLinesRequest(IReadOnlyList<PriceLineRequest> Lines);

public sealed record UpsertPriceLinesResult(int Added, int Changed);

/// <summary>A line of a list with the catalogue code and names of its service (null when the catalogue does not know the id).</summary>
public sealed record PriceLineItem(Guid Id, Guid ServiceId, decimal Price, bool IsActive, string? ServiceCode, string? ServiceNameEn, string? ServiceNameAr);

/// <summary>A tier whose formula references a list ("derived contracts").</summary>
public sealed record DerivedContractItem(Guid ContractPricingId, Guid PayerId, Guid ContractTierId, string PayerNameEn, string PayerNameAr, string TierNameEn, string TierNameAr, DateTimeOffset? AppliedAtUtc);

public sealed record BasePriceListItem(
    Guid Id,
    string Code,
    string NameEn,
    string NameAr,
    int Year,
    bool IsActive,
    int LineCount,
    int ActiveLineCount,
    int DerivedContractCount,
    Guid? CopiedFromListId,
    string? CopiedFromCode);

public sealed record BasePriceListDetail(
    Guid Id,
    string Code,
    string NameEn,
    string NameAr,
    int Year,
    bool IsActive,
    int LineCount,
    int ActiveLineCount,
    Guid? CopiedFromListId,
    string? CopiedFromCode,
    bool IsCashDefault,
    IReadOnlyList<DerivedContractItem> DerivedContracts);

public sealed record ApplyBasePriceListResult(int ContractsApplied, int PricesChanged);

// ---- contract pricing (CP-34) ----

public sealed record PricingFormulaTermRequest(Guid BaseListId, decimal PatientFactor, decimal PayerFactor);

public sealed record SetContractFormulaRequest(DateOnly EffectiveFrom, DateOnly? EffectiveTo, IReadOnlyList<PricingFormulaTermRequest> Terms);

public sealed record NegotiatePriceRequest(decimal PatientShare, decimal PayerShare);

public sealed record SetPriceAlertRequest(string? AlertCondition);

public sealed record CopyContractPricingRequest(decimal? Factor);

public sealed record ContractApplyResult(int Priced, int Added, int Updated, int Removed, int ExcludedKept);

public sealed record PricingFormulaTermView(int Priority, Guid BaseListId, string ListCode, string ListNameEn, string ListNameAr, int ListYear, bool ListIsActive, decimal PatientFactor, decimal PayerFactor);

/// <summary>A derived or negotiated price with the catalogue code and names of its service (null when the catalogue does not know the id).</summary>
public sealed record ContractPriceItem(
    Guid ServiceId,
    decimal ListPrice,
    decimal PatientShare,
    decimal PayerShare,
    bool IsExcluded,
    string? AlertCondition,
    Guid? SourceListId,
    string? SourceListCode,
    string? ServiceCode,
    string? ServiceNameEn,
    string? ServiceNameAr);

/// <summary>
/// The page of one tier (CP-34). <paramref name="Exists"/> is false while the tier has no formula yet (then the dates are
/// null and the lists empty). <paramref name="InEffect"/> compares today with the effective dates (intake Q5).
/// </summary>
public sealed record ContractPricingDetail(
    bool Exists,
    Guid? ContractPricingId,
    Guid PayerId,
    string PayerNameEn,
    string PayerNameAr,
    string PayerType,
    bool PayerIsActive,
    Guid ContractTierId,
    string TierNameEn,
    string TierNameAr,
    bool TierIsActive,
    bool TierIsDefault,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    bool InEffect,
    DateTimeOffset? AppliedAtUtc,
    string Currency,
    IReadOnlyList<PricingFormulaTermView> Terms,
    IReadOnlyList<ContractPriceItem> Prices);

public sealed record ContractPricingListItem(
    Guid Id,
    Guid PayerId,
    Guid ContractTierId,
    string PayerNameEn,
    string PayerNameAr,
    string TierNameEn,
    string TierNameAr,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    DateTimeOffset? AppliedAtUtc,
    int PricedCount,
    int ExcludedCount,
    int AlertCount);

/// <summary>Data of the printable review report (CP-34 "review report", LDM yearly review print); rendered by the SPA's print kit.</summary>
public sealed record ContractPricingReviewDocument(
    string PayerNameEn,
    string PayerNameAr,
    string PayerSearchKey,
    string TierNameEn,
    string TierNameAr,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    DateTimeOffset? AppliedAtUtc,
    string Currency,
    IReadOnlyList<PricingFormulaTermView> Terms,
    IReadOnlyList<ContractPriceItem> Prices,
    string? PrintedBy,
    DateTimeOffset PrintedAtUtc);

// ---- price quote (CP-12, BR-06) ----

/// <summary>One service of a quote; <paramref name="Priced"/> is false when the payer/tier does not price it (amounts are then 0).</summary>
public sealed record PriceQuoteLine(Guid ServiceId, decimal ListPrice, decimal PatientShare, decimal PayerShare, string? AlertCondition, bool Priced);

public sealed record PriceQuote(
    Guid PayerId,
    Guid ContractTierId,
    string PayerNameEn,
    string PayerNameAr,
    string TierNameEn,
    string TierNameAr,
    string Currency,
    IReadOnlyList<PriceQuoteLine> Lines,
    decimal TotalListPrice,
    decimal TotalPatientShare,
    decimal TotalPayerShare);

// ---- price list tools (CR 2026-10-19 C21): Excel import and bulk adjust ----

/// <summary>One row of an uploaded sheet: the sheet row number (for the error list), the service code and the new price as parsed by the SPA (null when the cell is empty or not a number).</summary>
public sealed record ImportPriceRowRequest(int Row, string? ServiceCode, decimal? Price);

/// <summary><paramref name="DryRun"/> validates only; otherwise the rows are applied when every row is valid (all-or-nothing).</summary>
public sealed record ImportPriceLinesRequest(IReadOnlyList<ImportPriceRowRequest> Rows, bool DryRun);

/// <summary>A refused row: PRC-041 (no code), PRC-015 (unknown code), PRC-006 (price missing or invalid), PRC-016 (code repeated).</summary>
public sealed record ImportRowError(int Row, string? ServiceCode, string Code, string Message);

/// <summary>
/// <paramref name="Accepted"/> rows passed (of which <paramref name="Added"/> are new lines and <paramref name="Changed"/> change a price);
/// <paramref name="Applied"/> is true only when nothing was refused and the call was not a dry run. With any error nothing is written.
/// </summary>
public sealed record ImportPriceLinesResult(int Accepted, int Added, int Changed, bool Applied, IReadOnlyList<ImportRowError> Errors);

/// <summary><paramref name="Mode"/> Amount|Percent, <paramref name="Rounding"/> None|Whole|Nearest5|Nearest10, <paramref name="ServiceIds"/> null = every line; <paramref name="Preview"/> computes without writing.</summary>
public sealed record AdjustBasePriceListRequest(string Mode, decimal Value, string Rounding, IReadOnlyList<Guid>? ServiceIds, bool Preview);

public sealed record PriceAdjustmentRow(Guid ServiceId, string? Code, string? NameEn, string? NameAr, decimal OldPrice, decimal NewPrice, bool Clamped);

/// <summary>The rows in list order; <paramref name="Changed"/> rows differ from their old price, <paramref name="Clamped"/> rows were held at 0.</summary>
public sealed record AdjustBasePriceListResult(IReadOnlyList<PriceAdjustmentRow> Rows, int Changed, int Clamped, bool Applied);

// ---- service prices (CR 2026-10-19 C22) ----

public sealed record ServiceBasePriceItem(Guid ListId, string ListCode, string ListNameEn, string ListNameAr, int Year, bool ListActive, decimal Price, bool LineActive);

/// <summary>A derived (or negotiated) price of the service in a tier; <paramref name="InEffect"/> compares today with the tier's effective dates.</summary>
public sealed record ServiceContractPriceItem(
    Guid TierId,
    Guid PayerId,
    string PayerNameEn,
    string PayerNameAr,
    string TierNameEn,
    string TierNameAr,
    decimal ListPrice,
    decimal PatientPrice,
    decimal PayerPrice,
    bool IsExcluded,
    string? SourceListCode,
    bool TierActive,
    bool PayerActive,
    bool InEffect);

/// <summary>Every price of one catalogue service: its line in each base list and its derived price in each contract tier (page CP-33b).</summary>
public sealed record ServicePricesView(
    Guid ServiceId,
    string Code,
    string NameEn,
    string NameAr,
    string Type,
    bool IsActive,
    string Currency,
    IReadOnlyList<ServiceBasePriceItem> BasePrices,
    IReadOnlyList<ServiceContractPriceItem> ContractPrices);

/// <summary>A catalogue service as the pricing read ports label it (code and bilingual name).</summary>
public sealed record CatalogueServiceLabel(Guid Id, string Code, string NameEn, string NameAr);
