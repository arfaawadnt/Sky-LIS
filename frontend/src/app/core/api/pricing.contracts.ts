/** Pricing module contracts mirrored from `Platform.Contracts.Pricing` (bootstrap until the OpenAPI generator lands, 01 §6.2). */

import type { SortRequest } from './paging';

export type { Paged } from './paging';

export interface CreatedResource {
  readonly id: string;
}

export type PayerType = 'Cash' | 'Credit' | 'Insurance' | 'Lab';

export const PAYER_TYPES: readonly PayerType[] = ['Cash', 'Credit', 'Insurance', 'Lab'];

export type CopyOperation = 'Multiply' | 'Add';

/** Rounding steps a copied list may use (intake LIS-INT-PRC-001 Q13). */
export const ROUNDING_STEPS: readonly number[] = [0.01, 0.25, 0.5, 1];

/** Bounds of the server (PriceMath): amounts 0–99 999 999.99 with two decimals, factors 0–10 with four. */
export const PRICE_LIMITS = {
  maxAmount: 99_999_999.99,
  maxFactor: 10,
  maxFormulaTerms: 3,
  maxAlertLength: 500,
  maxLinesPerUpsert: 2000,
  maxQuoteServices: 200,
} as const;

// ---- payers and tiers (CP-32) ----

export interface ContractTierItem {
  readonly id: string;
  readonly payerId: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly isActive: boolean;
  readonly isDefault: boolean;
}

export interface PayerListItem {
  readonly id: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly searchKey: string;
  readonly type: PayerType;
  readonly webPortalEnabled: boolean;
  readonly webUsername: string | null;
  readonly isActive: boolean;
  readonly isDefault: boolean;
  readonly tiers: readonly ContractTierItem[];
}

export interface PayerRequest {
  readonly nameEn: string;
  readonly nameAr: string;
  readonly searchKey: string;
  readonly type: PayerType;
  readonly webPortalEnabled: boolean;
  readonly webUsername: string | null;
}

export interface ContractTierRequest {
  readonly nameEn: string;
  readonly nameAr: string;
}

/** `sort`: `nameEn`, `nameAr`, `searchKey` or `type` (B-03, server allow-list). */
export interface PayerListFilter extends SortRequest {
  readonly search: string;
  readonly type: PayerType | null;
  readonly isActive: boolean | null;
  readonly page: number;
  readonly pageSize: number;
}

export interface TierOption {
  readonly id: string;
  readonly payerId: string;
  readonly payerNameEn: string;
  readonly payerNameAr: string;
  readonly payerType: PayerType;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly isActive: boolean;
  readonly payerIsActive: boolean;
  readonly isDefault: boolean;
  readonly hasPricing: boolean;
}

export interface DefaultPayerIds {
  readonly payerId: string;
  readonly contractTierId: string;
  readonly created: boolean;
}

// ---- base price lists (CP-33) ----

export interface BasePriceListItem {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly year: number;
  readonly isActive: boolean;
  readonly lineCount: number;
  readonly activeLineCount: number;
  readonly derivedContractCount: number;
  readonly copiedFromListId: string | null;
  readonly copiedFromCode: string | null;
}

export interface DerivedContractItem {
  readonly contractPricingId: string;
  readonly payerId: string;
  readonly contractTierId: string;
  readonly payerNameEn: string;
  readonly payerNameAr: string;
  readonly tierNameEn: string;
  readonly tierNameAr: string;
  readonly appliedAtUtc: string | null;
}

export interface BasePriceListDetail {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly year: number;
  readonly isActive: boolean;
  readonly lineCount: number;
  readonly activeLineCount: number;
  readonly copiedFromListId: string | null;
  readonly copiedFromCode: string | null;
  readonly isCashDefault: boolean;
  readonly derivedContracts: readonly DerivedContractItem[];
}

export interface CreateBasePriceListRequest {
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly year: number;
}

export interface UpdateBasePriceListRequest {
  readonly nameEn: string;
  readonly nameAr: string;
  readonly year: number;
}

export interface CopyBasePriceListRequest {
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly year: number;
  readonly operation: CopyOperation;
  readonly value: number;
  readonly roundingStep: number;
}

/** `sort`: `code`, `nameEn`, `nameAr` or `year` (B-03, server allow-list). */
export interface BasePriceListFilter extends SortRequest {
  readonly search: string;
  readonly year: number | null;
  readonly isActive: boolean | null;
  readonly page: number;
  readonly pageSize: number;
}

/** A line with its service's catalogue code and names (null when the catalogue does not know the id). */
export interface PriceLineItem {
  readonly id: string;
  readonly serviceId: string;
  readonly price: number;
  readonly isActive: boolean;
  readonly serviceCode: string | null;
  readonly serviceNameEn: string | null;
  readonly serviceNameAr: string | null;
}

export interface PriceLineRequest {
  readonly serviceId: string;
  readonly price: number;
  readonly isActive: boolean;
}

export interface UpsertPriceLinesResult {
  readonly added: number;
  readonly changed: number;
}

export interface ApplyBasePriceListResult {
  readonly contractsApplied: number;
  readonly pricesChanged: number;
}

// ---- contract pricing (CP-34) ----

export interface PricingFormulaTermRequest {
  readonly baseListId: string;
  readonly patientFactor: number;
  readonly payerFactor: number;
}

export interface SetContractFormulaRequest {
  /** `yyyy-MM-dd`. */
  readonly effectiveFrom: string;
  readonly effectiveTo: string | null;
  readonly terms: readonly PricingFormulaTermRequest[];
}

export interface PricingFormulaTermView {
  readonly priority: number;
  readonly baseListId: string;
  readonly listCode: string;
  readonly listNameEn: string;
  readonly listNameAr: string;
  readonly listYear: number;
  readonly listIsActive: boolean;
  readonly patientFactor: number;
  readonly payerFactor: number;
}

export interface ContractPriceItem {
  readonly serviceId: string;
  readonly listPrice: number;
  readonly patientShare: number;
  readonly payerShare: number;
  readonly isExcluded: boolean;
  readonly alertCondition: string | null;
  readonly sourceListId: string | null;
  readonly sourceListCode: string | null;
  readonly serviceCode: string | null;
  readonly serviceNameEn: string | null;
  readonly serviceNameAr: string | null;
}

export interface ContractPricingDetail {
  readonly exists: boolean;
  readonly contractPricingId: string | null;
  readonly payerId: string;
  readonly payerNameEn: string;
  readonly payerNameAr: string;
  readonly payerType: PayerType;
  readonly payerIsActive: boolean;
  readonly contractTierId: string;
  readonly tierNameEn: string;
  readonly tierNameAr: string;
  readonly tierIsActive: boolean;
  readonly tierIsDefault: boolean;
  readonly effectiveFrom: string | null;
  readonly effectiveTo: string | null;
  readonly inEffect: boolean;
  readonly appliedAtUtc: string | null;
  readonly currency: string;
  readonly terms: readonly PricingFormulaTermView[];
  readonly prices: readonly ContractPriceItem[];
}

export interface ContractPricingListItem {
  readonly id: string;
  readonly payerId: string;
  readonly contractTierId: string;
  readonly payerNameEn: string;
  readonly payerNameAr: string;
  readonly tierNameEn: string;
  readonly tierNameAr: string;
  readonly effectiveFrom: string;
  readonly effectiveTo: string | null;
  readonly appliedAtUtc: string | null;
  readonly pricedCount: number;
  readonly excludedCount: number;
  readonly alertCount: number;
}

export interface ContractApplyResult {
  readonly priced: number;
  readonly added: number;
  readonly updated: number;
  readonly removed: number;
  readonly excludedKept: number;
}

export interface NegotiatePriceRequest {
  readonly patientShare: number;
  readonly payerShare: number;
}

export interface ContractPricingReviewDocument {
  readonly payerNameEn: string;
  readonly payerNameAr: string;
  readonly payerSearchKey: string;
  readonly tierNameEn: string;
  readonly tierNameAr: string;
  readonly effectiveFrom: string;
  readonly effectiveTo: string | null;
  readonly appliedAtUtc: string | null;
  readonly currency: string;
  readonly terms: readonly PricingFormulaTermView[];
  readonly prices: readonly ContractPriceItem[];
  readonly printedBy: string | null;
  readonly printedAtUtc: string;
}

// ---- price quote (CP-12, BR-06; the page is Registration's) ----

export interface PriceQuoteLine {
  readonly serviceId: string;
  readonly listPrice: number;
  readonly patientShare: number;
  readonly payerShare: number;
  readonly alertCondition: string | null;
  readonly priced: boolean;
}

export interface PriceQuote {
  readonly payerId: string;
  readonly contractTierId: string;
  readonly payerNameEn: string;
  readonly payerNameAr: string;
  readonly tierNameEn: string;
  readonly tierNameAr: string;
  readonly currency: string;
  readonly lines: readonly PriceQuoteLine[];
  readonly totalListPrice: number;
  readonly totalPatientShare: number;
  readonly totalPayerShare: number;
}

export interface PriceQuoteRequest {
  readonly payerId: string | null;
  readonly tierId: string | null;
  readonly serviceIds: readonly string[];
}

// ---- catalogue services as Pricing reads them (module Catalogue A, same wave) ----

/**
 * One item of `GET /api/v1/catalogue/lookups/services` (Catalogue A's picker: active services, at most 50 per page,
 * `catalogue.view`) as the pricing typeahead consumes it; a `Paged` envelope (an array is accepted too) with `code`
 * (or `shortName`). The grids do not use it: their DTOs carry the service code and names.
 */
export interface CatalogueServiceLookupItem {
  readonly id: string;
  readonly code?: string | null;
  readonly shortName?: string | null;
  readonly nameEn?: string | null;
  readonly nameAr?: string | null;
  readonly isActive?: boolean | null;
}

/** A service as the pricing grids show it. */
export interface ServiceLabel {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
}

export const PRICING_API = {
  payers: '/api/v1/pricing/payers',
  defaultPayer: '/api/v1/pricing/payers/default',
  tierOptions: '/api/v1/pricing/payers/tiers',
  basePriceLists: '/api/v1/pricing/base-price-lists',
  contractPricing: '/api/v1/pricing/contract-pricing',
  quote: '/api/v1/pricing/quote',
  catalogueServices: '/api/v1/catalogue/lookups/services',
} as const;

/** Permission keys of the module, as the catalogue defines them (approve = Apply). */
export const PRICING_PERMISSIONS = {
  view: 'pricing.view',
  create: 'pricing.create',
  edit: 'pricing.edit',
  approve: 'pricing.approve',
  manage: 'pricing.manage',
  export: 'pricing.export',
} as const;
