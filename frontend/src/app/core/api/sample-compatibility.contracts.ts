/**
 * Compatibility groups of samples (change request LIS-CR-015, note M4): sample types / conditions the laboratory sets
 * together so that, within one processing unit, their tests share one sample in a visit; the sample takes the name of the
 * first member ordered among those present. Mirrors `Platform.Contracts.Catalogue` (SampleCompatibilityContracts.cs).
 */

export const SAMPLE_COMPATIBILITY_API = '/api/v1/catalogue/sample-compatibility-groups';

/** A member as the editor sends it; its priority is its position in the list. */
export interface CompatibilityMemberItem {
  readonly sampleTypeId: string;
  /** null: the sample type with no condition. */
  readonly sampleConditionId: string | null;
}

export interface CreateSampleCompatibilityGroupRequest {
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly members: readonly CompatibilityMemberItem[];
}

export interface UpdateSampleCompatibilityGroupRequest {
  readonly nameEn: string;
  readonly nameAr: string;
  readonly members: readonly CompatibilityMemberItem[];
}

export interface CompatibilityMemberView {
  readonly sampleTypeId: string;
  readonly sampleTypeCode: string;
  readonly sampleTypeNameEn: string;
  readonly sampleTypeNameAr: string;
  readonly sampleConditionId: string | null;
  readonly sampleConditionCode: string | null;
  readonly sampleConditionNameEn: string | null;
  readonly sampleConditionNameAr: string | null;
  readonly priority: number;
  /** Type and condition active; planning ignores the others. */
  readonly available: boolean;
}

export interface SampleCompatibilityGroupView {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly isActive: boolean;
  readonly members: readonly CompatibilityMemberView[];
}

/** Limits mirrored from the server (SampleCompatibilityGroup). */
export const SAMPLE_COMPATIBILITY_LIMITS = { codeLength: 20, minMembers: 2, maxMembers: 30 } as const;
