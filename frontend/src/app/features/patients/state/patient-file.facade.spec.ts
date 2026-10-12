import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { PATIENTS_API, type PatientDocumentItem } from '../../../core/api/patients.contracts';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { PatientFileFacade } from './patient-file.facade';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const document: PatientDocumentItem = {
  id: 'd-1',
  patientId: 'p-1',
  patientNumber: 'PAT-2026-00001',
  fileName: 'referral.pdf',
  contentType: 'application/pdf',
  sizeBytes: 2048,
  note: null,
  uploadedByUserId: 'u-1',
  uploadedByName: 'Clerk',
  uploadedAtUtc: '2026-10-09T09:00:00Z',
  isActive: true,
  deactivatedByName: null,
  deactivatedAtUtc: null,
  deactivationReason: null,
};

describe('PatientFileFacade (01 §6.3)', () => {
  let facade: PatientFileFacade;
  let backend: HttpTestingController;
  let granted: Set<string>;

  beforeEach(() => {
    granted = new Set(['patients.view', 'files.view', 'registration.view']);
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: PermissionsService, useValue: { can: (key: string) => granted.has(key) } satisfies Pick<PermissionsService, 'can'> },
        PatientFileFacade,
      ],
    });
    facade = TestBed.inject(PatientFileFacade);
    backend = TestBed.inject(HttpTestingController);
  });

  async function runEffects(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  /** Loads file p-1: the detail, its documents and the reference reads (which may fail without harm). */
  async function open(): Promise<void> {
    facade.load('p-1');
    await runEffects();
    backend.expectOne(`${PATIENTS_API.patients}/p-1`).flush({ id: 'p-1', number: 'PAT-2026-00001', status: 'Active', rowVersion: 7 });
    const documents = backend.expectOne((r) => r.url === `${PATIENTS_API.patients}/p-1/documents`);
    expect(documents.request.params.get('includeInactive')).toBe('false');   // active documents first (UAT A-03 note)
    documents.flush([document]);
    backend.match(() => true).forEach((call) => call.flush({}, { status: 404, statusText: 'Not Found' }));
    await runEffects();
  }

  it('never asks for the documents without files.view (UAT A-03: the list answered 403)', async () => {
    granted = new Set(['patients.view']);
    facade.load('p-1');
    await runEffects();
    backend.expectOne(`${PATIENTS_API.patients}/p-1`).flush({ id: 'p-1', number: 'PAT-2026-00001', status: 'Active', rowVersion: 7 });
    backend.expectNone((r) => r.url === `${PATIENTS_API.patients}/p-1/documents`);
    expect(facade.canSeeDocuments()).toBe(false);
  });

  it('never asks for the visits without registration.view, even when the tab is requested (round 2 R2-04)', async () => {
    granted = new Set(['patients.view']);
    facade.load('p-1');
    facade.visitsRequested.set(true);
    await runEffects();
    backend.expectOne(`${PATIENTS_API.patients}/p-1`).flush({ id: 'p-1', number: 'PAT-2026-00001', status: 'Active', rowVersion: 7 });
    backend.expectNone((r) => r.url === PATIENTS_API.visits);
    expect(facade.canSeeVisits()).toBe(false);
  });

  it('re-reads the file and its documents when the page opens again (UAT D-14, A-01)', async () => {
    await open();
    facade.refresh();
    await runEffects();
    backend.expectOne(`${PATIENTS_API.patients}/p-1`).flush({ id: 'p-1', number: 'PAT-2026-00001', status: 'Active', rowVersion: 8 });
    backend.expectOne((r) => r.url === `${PATIENTS_API.patients}/p-1/documents`).flush([]);
    backend.match(() => true).forEach((call) => call.flush({}, { status: 404, statusText: 'Not Found' }));
    await runEffects();
    expect(facade.documents.value()).toEqual([]);
  });

  it('loads the file and its documents but not the visits until their tab is opened', async () => {
    await open();
    expect(facade.detail.value()?.number).toBe('PAT-2026-00001');
    expect(facade.documents.value()).toEqual([document]);
    backend.expectNone((r) => r.url === PATIENTS_API.visits);

    facade.visitsRequested.set(true);
    await runEffects();
    const visits = backend.expectOne((r) => r.url === PATIENTS_API.visits);
    expect(visits.request.params.get('patientId')).toBe('p-1');
    visits.flush('', { status: 404, statusText: 'Not Found' });   // the registration module is not installed yet
    await runEffects();
    expect(facade.visits.error()).toBeTruthy();
  });

  it('uploads a document as multipart and reloads the documents', async () => {
    await open();
    const file = new File(['%PDF-1.7'], 'scan.pdf', { type: 'application/pdf' });

    const pending = facade.upload.execute({ file, note: ' from Dr. Adel ' });
    await settle();
    const post = backend.expectOne(`${PATIENTS_API.patients}/p-1/documents`);
    expect(post.request.method).toBe('POST');
    const body = post.request.body as FormData;
    expect((body.get('file') as File).name).toBe('scan.pdf');
    expect(body.get('note')).toBe('from Dr. Adel');
    post.flush({ id: 'd-2' }, { status: 201, statusText: 'Created' });
    expect(await pending).toEqual({ id: 'd-2' });

    await runEffects();
    backend.expectOne((r) => r.url === `${PATIENTS_API.patients}/p-1/documents`).flush([document]);
    backend.verify();
  });

  it('deactivates with a reason (never DELETE) and merges a duplicate into this file', async () => {
    await open();

    const deactivated = facade.deactivate.execute({ document, reason: 'wrong patient' });
    await settle();
    const deactivate = backend.expectOne(`${PATIENTS_API.documents}/d-1/deactivate`);
    expect(deactivate.request.method).toBe('POST');
    expect(deactivate.request.body).toEqual({ reason: 'wrong patient' });
    deactivate.flush(null, { status: 204, statusText: 'No Content' });
    expect(await deactivated).toBeUndefined();
    await runEffects();
    backend.expectOne((r) => r.url === `${PATIENTS_API.patients}/p-1/documents`).flush([{ ...document, isActive: false }]);

    const merged = facade.merge.execute({ mergedPatientId: 'p-2', reason: 'registered twice', confirmGenderMismatch: false });
    await settle();
    const merge = backend.expectOne(`${PATIENTS_API.patients}/p-1/merge`);
    expect(merge.request.body).toEqual({ mergedPatientId: 'p-2', reason: 'registered twice', confirmGenderMismatch: false });
    merge.flush(null, { status: 204, statusText: 'No Content' });
    expect(await merged).toBeUndefined();   // a 204 is success, not the failure null
    await runEffects();
    backend.expectOne(`${PATIENTS_API.patients}/p-1`).flush({ id: 'p-1', number: 'PAT-2026-00001', status: 'Active', rowVersion: 8, mergedFiles: [] });
    backend.expectOne((r) => r.url === `${PATIENTS_API.patients}/p-1/documents`).flush([]);
    backend.verify();
  });

  it('offers only other active files as merge candidates', async () => {
    await open();

    facade.mergeQuery.set('omar');
    await runEffects();
    const search = backend.expectOne((r) => r.url === PATIENTS_API.patients);
    expect(search.request.params.get('q')).toBe('omar');
    expect(search.request.params.get('status')).toBe('Active');
    search.flush({
      items: [
        { id: 'p-1', status: 'Active' },
        { id: 'p-2', status: 'Active' },
        { id: 'p-3', status: 'Merged' },
      ],
      page: 1,
      pageSize: 10,
      totalCount: 3,
      totalPages: 1,
    });
    await runEffects();

    expect(facade.mergeCandidates().map((candidate) => candidate.id)).toEqual(['p-2']);
  });
});
