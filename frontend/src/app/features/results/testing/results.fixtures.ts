import type {
  LaboratoryReportDocument,
  Paged,
  ReportUnitView,
  ResultCaseView,
  ResultRowView,
  ResultVisitListItem,
  WorklistItem,
  WorklistPage,
} from '../../../core/api/results.contracts';

/** Test data of the Results specs (only specs import it). */
export function resultRow(overrides: Partial<ResultRowView> = {}): ResultRowView {
  return {
    id: 'res-1',
    lineId: 'line-1',
    serviceId: 'svc-1',
    testId: 'test-1',
    testCode: 'GLU',
    testNameEn: 'Glucose',
    testNameAr: 'جلوكوز',
    reportName: null,
    testType: 'Numeric',
    isHiddenHelper: false,
    isFormula: false,
    tubeId: 'tube-1',
    sampleNumber: 'SMP-2026-00001',
    sampleStatus: 'Received',
    sampleReceived: true,
    status: 'Ordered',
    numericValue: null,
    textValue: null,
    paragraphs: null,
    culture: null,
    unit: 'mg/dL',
    decimals: 1,
    range: { low: 70, high: 110, panicLow: 40, panicHigh: 400, reportableLow: null, reportableHigh: null, text: null, display: '70 – 110' },
    flag: null,
    isPanic: false,
    delta: null,
    source: 'Manual',
    analyserCode: null,
    analyserFlags: null,
    entered: null,
    reviewed: null,
    verified: null,
    verifiedByName: null,
    printCount: 0,
    lastPrintedAtUtc: null,
    amendmentCount: 0,
    reportComment: null,
    internalNote: null,
    listOptions: [],
    paragraphTemplates: [],
    expectedAtUtc: '2026-10-11T08:00:00Z',
    ...overrides,
  };
}

export function reportUnit(rows: readonly ResultRowView[] = [resultRow()], overrides: Partial<ReportUnitView> = {}): ReportUnitView {
  return {
    key: 'unit-1',
    clinicalGroupId: 'grp-1',
    groupCode: 'CHEM',
    groupNameEn: 'Chemistry',
    groupNameAr: 'الكيمياء',
    printAloneServiceId: null,
    serviceNameEn: null,
    serviceNameAr: null,
    status: rows[0]?.status ?? 'Ordered',
    isPrinted: false,
    printCount: 0,
    hasMissingValues: rows.some((row) => row.numericValue === null && row.textValue === null),
    hasPanic: rows.some((row) => row.isPanic),
    hasDelta: rows.some((row) => row.delta?.flag === true),
    samples: [{ tubeId: 'tube-1', sampleNumber: 'SMP-2026-00001', status: 'Received', collectedAtUtc: '2026-10-10T08:00:00Z', receivedAtUtc: '2026-10-10T08:10:00Z' }],
    rights: { canEdit: true, canReview: true, canVerify: true, canPrint: true, canModifyVerified: false, canPreviewWithoutVerify: false },
    services: [{ serviceId: 'svc-1', code: 'GLU', nameEn: 'Glucose', nameAr: 'جلوكوز', reportName: null, status: rows[0]?.status ?? 'Ordered', resultCount: rows.length }],
    results: rows,
    hiddenResults: 0,
    hiddenGroups: [],
    ...overrides,
  };
}

export function caseView(units: readonly ReportUnitView[] = [reportUnit()], overrides: Partial<ResultCaseView> = {}): ResultCaseView {
  return {
    visit: {
      visitId: 'visit-1',
      accession: 'ACC-2026-00001',
      registeredAtUtc: '2026-10-10T07:30:00Z',
      branchId: 'branch-1',
      branchNameEn: 'Main',
      branchNameAr: 'الرئيسي',
      payerId: null,
      payerNameEn: 'Cash',
      payerNameAr: 'نقدي',
      doctorId: null,
      doctorNameEn: null,
      doctorNameAr: null,
      urgent: false,
    },
    patient: {
      patientId: 'pat-1',
      number: 'PAT-2026-00001',
      fullName: 'Mona Adel',
      nameAr: 'منى عادل',
      gender: 'Female',
      dateOfBirth: '1990-05-01',
      ageYears: 36,
      ageMonths: 5,
      ageDays: 9,
      patientConditionId: null,
      conditionNameEn: null,
      conditionNameAr: null,
      isVip: false,
    },
    units,
    hiddenUnits: 0,
    hiddenResults: 0,
    scannedSampleNumber: null,
    settings: { reviewBeforeVerify: false, printUnverified: false, resultLanguage: 'en' },
    ...overrides,
  };
}

export function visitItem(overrides: Partial<ResultVisitListItem> = {}): ResultVisitListItem {
  return {
    visitId: 'visit-1',
    accession: 'ACC-2026-00001',
    registeredAtUtc: '2026-10-10T07:30:00Z',
    patientId: 'pat-1',
    patientNumber: 'PAT-2026-00001',
    patientName: 'Mona Adel',
    patientNameAr: 'منى عادل',
    branchId: 'branch-1',
    status: 'Ordered',
    results: 2,
    units: 1,
    hasPanic: false,
    hasDelta: false,
    anyPrinted: false,
    ...overrides,
  };
}

export function worklistItem(overrides: Partial<WorklistItem> = {}): WorklistItem {
  return {
    resultId: 'res-1',
    visitId: 'visit-1',
    accession: 'ACC-2026-00001',
    patientId: 'pat-1',
    patientNumber: 'PAT-2026-00001',
    patientName: 'Mona Adel',
    patientNameAr: 'منى عادل',
    gender: 'Female',
    ageYears: 36,
    testId: 'test-1',
    testCode: 'GLU',
    testNameEn: 'Glucose',
    testNameAr: 'جلوكوز',
    clinicalGroupId: 'grp-1',
    groupNameEn: 'Chemistry',
    groupNameAr: 'الكيمياء',
    status: 'Ordered',
    tubeId: 'tube-1',
    sampleNumber: 'SMP-2026-00001',
    sampleStatus: 'Received',
    collectedAtUtc: '2026-10-10T08:00:00Z',
    expectedAtUtc: '2026-10-11T08:00:00Z',
    delaySeconds: 0,
    consumedPercent: 40,
    delayState: 'OnTime',
    urgent: false,
    textValue: null,
    flag: null,
    isPrinted: false,
    source: 'Manual',
    ...overrides,
  };
}

export function worklistPage(items: readonly WorklistItem[], overrides: Partial<WorklistPage> = {}): WorklistPage {
  return {
    items,
    page: 1,
    pageSize: 50,
    totalCount: items.length,
    counters: { requests: new Set(items.map((item) => item.visitId)).size, tests: items.length, samples: new Set(items.map((item) => item.tubeId)).size, delayed: items.filter((item) => item.delayState === 'Late').length },
    warnPercent: 80,
    latePercent: 100,
    ...overrides,
  };
}

export function page<T>(items: readonly T[]): Paged<T> {
  return { items, page: 1, pageSize: 40, totalCount: items.length, totalPages: 1 };
}

export function reportDocument(overrides: Partial<LaboratoryReportDocument> = {}): LaboratoryReportDocument {
  return {
    letterhead: { labNameEn: 'Sky Lab', labNameAr: 'مختبر سكاي', branchNameEn: 'Main', branchNameAr: 'الرئيسي' },
    template: null,
    language: 'en',
    reportUnitKey: 'unit-1',
    titleEn: 'Laboratory report',
    titleAr: 'تقرير المختبر',
    patient: { number: 'PAT-2026-00001', fullName: 'Mona Adel', nameAr: 'منى عادل', gender: 'Female', ageYears: 36, ageMonths: 5, ageDays: 9, dateOfBirth: '1990-05-01' },
    visit: { accession: 'ACC-2026-00001', registeredAtUtc: '2026-10-10T07:30:00Z', payerNameEn: 'Cash', payerNameAr: 'نقدي', doctorNameEn: null, doctorNameAr: null },
    samples: [{ sampleNumber: 'SMP-2026-00001', collectedAtUtc: '2026-10-10T08:00:00Z', receivedAtUtc: '2026-10-10T08:10:00Z' }],
    sections: [
      {
        serviceId: 'svc-1',
        headingEn: 'Chemistry',
        headingAr: 'الكيمياء',
        isProfile: false,
        lines: [{ resultId: 'res-1', nameEn: 'Glucose', nameAr: 'جلوكوز', testType: 'Numeric', value: '95.0', unit: 'mg/dL', range: '70 – 110', flag: 'Normal', isPanic: false, status: 'Verified', comment: null, paragraphs: null, culture: null, methodology: null }],
      },
    ],
    signature: { verifiedByName: 'Dr. Hassan', verifiedAtUtc: '2026-10-10T10:00:00Z', reviewedByName: null, reviewedAtUtc: null },
    isPreliminary: false,
    isPreview: false,
    printCount: 1,
    amendmentCount: 0,
    printListNumber: null,
    qrValue: 'ACC-2026-00001',
    generatedAtUtc: '2026-10-10T10:05:00Z',
    generatedBy: 'Reception',
    ...overrides,
  };
}

export function problemBody(code: string, status: number): Record<string, unknown> {
  return { type: 'about:blank', title: code, status, code, traceId: 't', correlationId: 'c' };
}

/** Runs the resource effects and lets the promises settle; `whenStable()` would wait for the very requests a test flushes. */
export const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));
