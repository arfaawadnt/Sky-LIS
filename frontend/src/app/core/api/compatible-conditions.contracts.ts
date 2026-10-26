/**
 * Compatible sample conditions (change request LIS-CR-015, note M4): a condition whose tests join the tube of another
 * condition of the same sample type when that condition's tube is in the same visit (Serum · Random merges into
 * Serum · Fasting). Mirrors `Platform.Contracts.Catalogue` (SampleTypeContracts.cs).
 */

export const COMPATIBLE_CONDITIONS_API = {
  list: '/api/v1/catalogue/sample-types/compatible-conditions',
  save: (sampleTypeId: string): string => `/api/v1/catalogue/sample-types/${encodeURIComponent(sampleTypeId)}/compatible-conditions`,
} as const;

export interface CompatibleConditionView {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly isActive: boolean;
  /** The condition whose tube this one's tests join; null = its own sample. */
  readonly mergesIntoConditionId: string | null;
}

export interface CompatibleSampleTypeView {
  readonly id: string;
  readonly code: string;
  readonly shortName: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly isActive: boolean;
  readonly conditions: readonly CompatibleConditionView[];
}

/** One entry of the full map a save sends; conditions not listed keep their own sample. */
export interface SampleConditionPairItem {
  readonly conditionId: string;
  readonly mergesIntoConditionId: string | null;
}

export interface SaveCompatibleConditionsRequest {
  readonly pairs: readonly SampleConditionPairItem[];
}
