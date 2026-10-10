import type { Paged, SampleDetail, SampleListItem, SampleStatus, SampleTimeline } from '../../../core/api/samples.contracts';

/** Test data of the Samples specs (not part of the application bundle: only specs import it). */
export function sampleItem(overrides: Partial<SampleListItem> = {}): SampleListItem {
  return {
    id: 'smp-1',
    sampleNumber: 'SMP-2026-00001',
    tubeId: 'tube-1',
    generation: 0,
    visitId: 'visit-1',
    accession: 'ACC-2026-00001',
    patientId: 'pat-1',
    patientNumber: 'PAT-2026-00001',
    patientName: 'Mona Adel',
    patientNameAr: 'منى عادل',
    branchId: 'branch-1',
    clinicalGroupId: 'grp-1',
    unitCode: 'C',
    clinicalGroupNameEn: 'Chemistry',
    clinicalGroupNameAr: 'الكيمياء',
    sampleTypeId: 'type-1',
    sampleTypeNameEn: 'Serum',
    sampleTypeNameAr: 'مصل',
    sampleConditionId: null,
    sampleConditionNameEn: null,
    sampleConditionNameAr: null,
    serviceCount: 2,
    status: 'Ordered',
    orderedAtUtc: '2026-10-09T08:00:00Z',
    statusChangedAtUtc: '2026-10-09T08:00:00Z',
    collectedAtUtc: null,
    receivedAtUtc: null,
    rejectionReasonId: null,
    rejectionReasonNameEn: null,
    rejectionReasonNameAr: null,
    rejectionNote: null,
    rejectedAtUtc: null,
    replacedBySampleId: null,
    replacesSampleId: null,
    ...overrides,
  };
}

export function sampleDetail(status: SampleStatus = 'Ordered', overrides: Partial<SampleListItem> = {}): SampleDetail {
  return {
    summary: sampleItem({ status, ...overrides }),
    services: [{ id: 'svc-1', code: 'GLU', nameEn: 'Glucose', nameAr: 'جلوكوز' }],
    collected: null,
    received: null,
    rejection: null,
    replacedBy: null,
    replaces: null,
    cancelledAtUtc: null,
    cancellationReason: null,
    isResultEntryAllowed: status === 'Received',
  };
}

export function page<T>(items: readonly T[]): Paged<T> {
  return { items, page: 1, pageSize: 50, totalCount: items.length, totalPages: items.length === 0 ? 0 : 1 };
}

export function timeline(): SampleTimeline {
  return {
    sample: sampleDetail('Received', { collectedAtUtc: '2026-10-09T08:10:00Z', receivedAtUtc: '2026-10-09T09:10:00Z' }),
    steps: [
      { id: 'e1', sequence: 1, action: 'Ordered', status: 'Ordered', atUtc: '2026-10-09T08:00:00Z', userId: 'u1', userName: 'Reception Clerk', branchId: 'branch-1', branchNameEn: 'Main', branchNameAr: 'الرئيسي', reasonId: null, reasonNameEn: null, reasonNameAr: null, note: null, sincePreviousSeconds: null },
      { id: 'e2', sequence: 2, action: 'Collected', status: 'Collected', atUtc: '2026-10-09T08:10:00Z', userId: 'u2', userName: 'Phlebotomist', branchId: 'branch-1', branchNameEn: 'Main', branchNameAr: 'الرئيسي', reasonId: null, reasonNameEn: null, reasonNameAr: null, note: null, sincePreviousSeconds: 600 },
      { id: 'e3', sequence: 3, action: 'Received', status: 'Received', atUtc: '2026-10-09T09:10:00Z', userId: 'u3', userName: 'Technician', branchId: 'branch-2', branchNameEn: 'Lab', branchNameAr: 'المعمل', reasonId: null, reasonNameEn: null, reasonNameAr: null, note: null, sincePreviousSeconds: 3600 },
    ],
    durations: { orderedToCollectedSeconds: 600, collectedToReceivedSeconds: 3600, orderedToReceivedSeconds: 4200 },
  };
}

/** A problem+json body as the API sends it. */
export function problemBody(code: string, status: number): Record<string, unknown> {
  return { type: 'about:blank', title: code, status, code, traceId: 't', correlationId: 'c' };
}

/** Runs the resource effects and lets the promises settle; `whenStable()` would wait for the very requests a test flushes. */
export const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));
