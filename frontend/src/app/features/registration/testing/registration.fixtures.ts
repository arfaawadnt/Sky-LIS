/** Fixtures of the Registration specs (not part of the application bundle: only specs import this file). */

import type { HttpTestingController } from '@angular/common/http/testing';

import { BILLING_API, type BillingTreasuryItem } from '../../../core/api/billing.contracts';
import { CATALOGUE_B_API } from '../../../core/api/catalogue-b.contracts';
import type { Paged } from '../../../core/api/paging';
import type { PatientSummary } from '../../../core/api/patients.contracts';
import { PRICING_API, type TierOption } from '../../../core/api/pricing.contracts';
import { REGISTRATION_API, type OrderCheckResult, type OrderLinePreview, type RegistrationPageSettings, type VisitDetail, type VisitListItem } from '../../../core/api/registration.contracts';

export const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

export function page<T>(items: readonly T[]): Paged<T> {
  return { items, page: 1, pageSize: 50, totalCount: items.length, totalPages: 1 };
}

export const cashTier: TierOption = {
  id: 'tier-cash',
  payerId: 'payer-cash',
  payerNameEn: 'Cash',
  payerNameAr: 'نقدي',
  payerType: 'Cash',
  nameEn: 'Standard',
  nameAr: 'قياسي',
  isActive: true,
  payerIsActive: true,
  isDefault: true,
  hasPricing: true,
};

export const insurerTier: TierOption = {
  ...cashTier,
  id: 'tier-ins',
  payerId: 'payer-ins',
  payerNameEn: 'Medi Insure',
  payerNameAr: 'ميدي للتأمين',
  payerType: 'Insurance',
  nameEn: 'Gold',
  nameAr: 'ذهبي',
  isDefault: false,
};

export const treasury: BillingTreasuryItem = {
  id: 't-1',
  code: 'MAIN',
  nameEn: 'Main drawer',
  nameAr: 'الخزينة الرئيسية',
  branchId: 'b-1',
  isActive: true,
};

export function settings(overrides: Partial<RegistrationPageSettings> = {}): RegistrationPageSettings {
  return {
    autoPrintLabels: true,
    autoPrintJobOrder: false,
    autoPrintReceipt: true,
    labelCopies: 1,
    mandatoryMobile: false,
    mandatoryDoctor: false,
    mandatoryNationalId: false,
    discountCapPercent: 20,
    canCancel: true,
    canTransfer: false,
    canPay: true,
    canSeeVip: false,
    currency: 'EGP',
    ...overrides,
  };
}

export const patient: PatientSummary = {
  id: 'p-1',
  number: 'PAT-2026-00001',
  title: 'Mrs',
  fullName: 'Mona Adel',
  nameAr: 'منى عادل',
  gender: 'Female',
  dateOfBirth: '1990-05-01',
  dateOfBirthIsEstimated: false,
  age: { years: 36, months: 5, days: 9 },
  mobile: '01001234567',
  nationalId: null,
  isVip: false,
};

export function line(overrides: Partial<OrderLinePreview> = {}): OrderLinePreview {
  return {
    serviceId: 's-cbc',
    code: 'CBC',
    nameEn: 'Complete blood count',
    nameAr: 'صورة دم كاملة',
    type: 'Test',
    kind: 'Service',
    status: 'Ordered',
    priced: true,
    listPrice: 100,
    patientShare: 100,
    payerShare: 0,
    expectedResultAtUtc: '2026-10-11T08:00:00Z',
    precaution: null,
    alertCondition: null,
    origin: 'Manual',
    addedByRuleId: null,
    ruleNameEn: null,
    ruleNameAr: null,
    tubeKey: 'g-haem:st-blood:',
    members: [],
    ...overrides,
  };
}

export function checkResult(overrides: Partial<OrderCheckResult> = {}): OrderCheckResult {
  const lines = overrides.lines ?? [line()];
  return {
    lines,
    tubes: [
      {
        key: 'g-haem:st-blood:',
        existingTubeId: null,
        sampleNumber: null,
        unitCode: 'H',
        clinicalGroupNameEn: 'Haematology',
        clinicalGroupNameAr: 'أمراض الدم',
        sampleTypeNameEn: 'Whole blood',
        sampleTypeNameAr: 'دم كامل',
        sampleConditionNameEn: null,
        sampleConditionNameAr: null,
        serviceIds: lines.map((item) => item.serviceId),
      },
    ],
    totals: {
      totalListPrice: lines.reduce((sum, item) => sum + item.listPrice, 0),
      patientShareTotal: lines.reduce((sum, item) => sum + item.patientShare, 0),
      payerShareTotal: lines.reduce((sum, item) => sum + item.payerShare, 0),
    },
    issues: [],
    warnings: [],
    preparations: [],
    currency: 'EGP',
    ...overrides,
  };
}

export function visitDetail(overrides: Partial<VisitDetail> = {}): VisitDetail {
  return {
    id: 'v-1',
    accession: 'ACC-2026-00001',
    status: 'Open',
    locationType: 'Walkin',
    branchId: 'b-1',
    branchNameEn: 'Main branch',
    branchNameAr: 'الفرع الرئيسي',
    patient: {
      id: 'p-1',
      number: 'PAT-2026-00001',
      title: 'Mrs',
      fullName: 'Mona Adel',
      nameAr: 'منى عادل',
      gender: 'Female',
      ageYears: 36,
      ageMonths: 5,
      ageDays: 9,
      mobile: '01001234567',
      nationalId: null,
      isVip: false,
      status: 'Active',
      masked: false,
    },
    originalPatientId: null,
    contract: { payerId: 'payer-cash', payerNameEn: 'Cash', payerNameAr: 'نقدي', contractTierId: 'tier-cash', tierNameEn: 'Standard', tierNameAr: 'قياسي' },
    referringDoctorId: null,
    doctorNameEn: null,
    doctorNameAr: null,
    treatmentNo: null,
    insuranceId: null,
    diagnosisId: null,
    bookingId: null,
    batchId: null,
    batchNumber: null,
    urgent: false,
    biohazard: false,
    collectedOutside: false,
    registeredAtUtc: '2026-10-10T08:00:00Z',
    registeredByName: 'Reception One',
    completedAtUtc: null,
    totalListPrice: 100,
    discountTotal: 0,
    patientShareTotal: 100,
    payerShareTotal: 0,
    discountKind: null,
    discountValue: 0,
    discountReasonId: null,
    discountReason: null,
    utcOffsetMinutes: 180,
    lines: [
      {
        id: 'l-1',
        serviceId: 's-cbc',
        code: 'CBC',
        nameEn: 'Complete blood count',
        nameAr: 'صورة دم كاملة',
        serviceType: 'Test',
        kind: 'Service',
        parentLineId: null,
        status: 'Ordered',
        listPrice: 100,
        patientShare: 100,
        payerShare: 0,
        discount: 0,
        expectedResultAtUtc: '2026-10-11T08:00:00Z',
        tubeId: 'tube-1',
        sampleNumber: 'SMP-2026-00001',
        collectorUserId: null,
        collectorName: null,
        origin: 'Manual',
        orderedAtUtc: '2026-10-10T08:00:00Z',
        orderedByName: 'Reception One',
        releasedAtUtc: null,
        cancellationReason: null,
        cancelledByName: null,
        cancelledAtUtc: null,
      },
    ],
    tubes: [],
    preparationAnswers: [],
    transfers: [],
    balance: { patientShareTotal: 100, paid: 100, refunded: 0, remaining: 0, refundable: 0 },
    rowVersion: 1,
    ...overrides,
  };
}

export function visitRow(overrides: Partial<VisitListItem> = {}): VisitListItem {
  return {
    id: 'v-1',
    accession: 'ACC-2026-00001',
    registeredAtUtc: '2026-10-10T08:00:00Z',
    branchId: 'b-1',
    branchNameEn: 'Main branch',
    branchNameAr: 'الفرع الرئيسي',
    patientId: 'p-1',
    patientNumber: 'PAT-2026-00001',
    patientName: 'Mona Adel',
    patientNameAr: 'منى عادل',
    mobile: '01001234567',
    patientMasked: false,
    payerId: 'payer-cash',
    payerNameEn: 'Cash',
    payerNameAr: 'نقدي',
    tierNameEn: 'Standard',
    tierNameAr: 'قياسي',
    status: 'Open',
    locationType: 'Walkin',
    urgent: false,
    batchNumber: null,
    totalListPrice: 100,
    discountTotal: 0,
    patientShareTotal: 100,
    payerShareTotal: 0,
    paid: 40,
    remaining: 60,
    lines: [
      { lineId: 'l-1', code: 'CBC', nameEn: 'Complete blood count', nameAr: 'صورة دم كاملة', status: 'Ordered', kind: 'Service' },
      { lineId: 'l-2', code: 'GLU', nameEn: 'Glucose', nameAr: 'جلوكوز', status: 'Cancelled', kind: 'Service' },
    ],
    ...overrides,
  };
}

/** Answers the reference lists the registration page reads on start (settings, tiers, doctors, diagnoses, reasons, treasuries). */
export function flushReferenceLists(backend: HttpTestingController, options: { tiers?: readonly TierOption[]; treasuries?: readonly BillingTreasuryItem[]; settings?: RegistrationPageSettings } = {}): void {
  for (const call of backend.match((request) => request.method === 'GET')) {
    const url = call.request.url;
    if (url === REGISTRATION_API.settings) {
      call.flush(options.settings ?? settings());
    } else if (url === PRICING_API.tierOptions) {
      call.flush(options.tiers ?? [cashTier, insurerTier]);
    } else if (url === BILLING_API.myTreasuries) {
      call.flush(options.treasuries ?? [treasury]);
    } else if (url === CATALOGUE_B_API.doctors || url === CATALOGUE_B_API.diagnoses || url === CATALOGUE_B_API.reasons) {
      call.flush(page([]));
    } else {
      call.flush(null, { status: 404, statusText: 'Not Found' });
    }
  }
}
