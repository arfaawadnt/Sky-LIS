import type { LabTestDetail, LabTestGeneral, ServiceDetail, ServiceGeneral } from '../../../core/api/catalogue-a.contracts';

/** The editable data of a loaded service, for an update that changes only part of it. */
export function serviceGeneralOf(detail: ServiceDetail): ServiceGeneral {
  return {
    shortName: detail.shortName,
    shortcutNo: detail.shortcutNo,
    nameEn: detail.nameEn,
    nameAr: detail.nameAr,
    reportName: detail.reportName,
    loinc: detail.loinc,
    cpt: detail.cpt,
    clinicalGroupId: detail.clinicalGroupId,
    sampleTypeId: detail.sampleTypeId,
    sampleConditionId: detail.sampleConditionId,
    header: detail.header,
    aliases: detail.aliases,
    precaution: detail.precaution,
    gender: detail.gender,
    turnaroundHours: detail.turnaroundHours,
    labelCount: detail.labelCount,
    printAlone: detail.printAlone,
    isOrderable: detail.isOrderable,
    showOnWeb: detail.showOnWeb,
    profileSum: detail.profileSum,
  };
}

/** The editable general data of a loaded test. */
export function testGeneralOf(detail: LabTestDetail): LabTestGeneral {
  return {
    nameEn: detail.nameEn,
    nameAr: detail.nameAr,
    reportName: detail.reportName,
    loinc: detail.loinc,
    cpt: detail.cpt,
    clinicalGroupId: detail.clinicalGroupId,
    type: detail.type,
    sampleTypeId: detail.sampleTypeId,
    sampleConditionId: detail.sampleConditionId,
    unitId: detail.unitId,
    gender: detail.gender,
    methodology: detail.methodology,
    decimals: detail.decimals,
    isHiddenHelper: detail.isHiddenHelper,
    printReference: detail.printReference,
    labelCount: detail.labelCount,
    prefix: detail.prefix,
  };
}

/** Empty or whitespace-only text travels as null. */
export function textOrNull(value: string | null | undefined): string | null {
  const trimmed = (value ?? '').trim();
  return trimmed === '' ? null : trimmed;
}

/** A number input's value: `null`, `''` and NaN travel as null. */
export function numberOrNull(value: number | string | null | undefined): number | null {
  if (value === null || value === undefined || value === '') {
    return null;
  }

  const parsed = typeof value === 'number' ? value : Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

/** Weekday mask (bit 0 = Sunday … bit 6 = Saturday) ↔ seven flags in that order. */
export function maskToDays(mask: number): boolean[] {
  return Array.from({ length: 7 }, (_, day) => (mask & (1 << day)) !== 0);
}

export function daysToMask(days: readonly boolean[]): number {
  return days.reduce((mask, on, day) => (on ? mask | (1 << day) : mask), 0);
}
