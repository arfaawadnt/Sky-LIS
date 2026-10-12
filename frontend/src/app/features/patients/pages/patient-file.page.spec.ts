import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { PATIENTS_API, type PatientDetail, type PatientDocumentItem } from '../../../core/api/patients.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { PatientFileFacade } from '../state/patient-file.facade';
import { PatientFilePage } from './patient-file.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));
let granted = new Set<string>();

const detail: PatientDetail = {
  id: 'p-1',
  number: 'PAT-2026-00001',
  title: 'Mrs',
  fullName: 'Mona Hassan',
  nameAr: 'منى حسن',
  gender: 'Female',
  dateOfBirth: '1990-05-01',
  dateOfBirthIsEstimated: false,
  age: { years: 36, months: 5, days: 8 },
  statedAge: null,
  ageRecordedOn: null,
  registeredOn: '2026-10-01',
  patientConditionId: null,
  mobile: '01001234567',
  nationalId: '29005010101234',
  nationality: 'EG',
  email: null,
  governorateId: null,
  city: 'Cairo',
  address: null,
  comments: null,
  isVip: null,
  status: 'Active',
  merge: null,
  mergedFiles: [{ id: 'p-0', number: 'PAT-2026-00000', mergedAtUtc: '2026-10-05T10:00:00Z' }],
  createdAtUtc: '2026-10-01T08:00:00Z',
  createdByName: 'Clerk',
  modifiedAtUtc: null,
  modifiedByName: null,
  rowVersion: 7,
};

const document: PatientDocumentItem = {
  id: 'd-1',
  patientId: 'p-0',
  patientNumber: 'PAT-2026-00000',
  fileName: 'referral.pdf',
  contentType: 'application/pdf',
  sizeBytes: 2048,
  note: 'from Dr. Adel',
  uploadedByUserId: 'u-1',
  uploadedByName: 'Clerk',
  uploadedAtUtc: '2026-10-02T09:00:00Z',
  isActive: true,
  deactivatedByName: null,
  deactivatedAtUtc: null,
  deactivationReason: null,
};

describe('PatientFilePage (CP-11 file, documents, visits, merge)', () => {
  let backend: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PatientFilePage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        PatientFileFacade,
        { provide: PermissionsService, useValue: { can: (key: string) => granted.has(key) } satisfies Pick<PermissionsService, 'can'> },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
  });

  async function flush(): Promise<void> {
    TestBed.tick();
    await settle();
    TestBed.tick();
    await settle();
  }

  async function render(patient: PatientDetail = detail): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(PatientFilePage);
    fixture.componentRef.setInput('id', patient.id);
    await flush();
    backend.expectOne(`${PATIENTS_API.patients}/${patient.id}`).flush(patient);
    if (granted.has('files.view')) {
      backend.expectOne((r) => r.url === `${PATIENTS_API.patients}/${patient.id}/documents`).flush([document]);
    } else {
      backend.expectNone((r) => r.url === `${PATIENTS_API.patients}/${patient.id}/documents`);
    }

    backend.match(() => true).forEach((call) => call.flush([]));
    await flush();
    return fixture.nativeElement as HTMLElement;
  }

  function tab(host: HTMLElement, index: number): HTMLElement {
    const button = host.querySelectorAll<HTMLElement>('app-tabs [role="tab"]')[index];
    if (!button) {
      throw new Error(`tab ${index} missing`);
    }

    return button;
  }

  it('shows the file with the files merged into it and the actions the user holds', async () => {
    granted = new Set(['patients.view', 'patients.edit']);
    const host = await render();

    expect(host.querySelector('app-page-header')?.textContent).toContain('PAT-2026-00001 · Mona Hassan');
    expect(host.textContent).toContain('29005010101234');
    expect(host.textContent).toContain('PAT-2026-00000');
    expect(host.textContent).toContain('patients.edit');
    expect(host.textContent).not.toContain('patients.merge.open');
  });

  it('offers no Documents tab without files.view (UAT A-03)', async () => {
    granted = new Set(['patients.view', 'registration.view']);
    const host = await render();
    const tabs = [...host.querySelectorAll<HTMLElement>('app-tabs [role="tab"]')].map((button) => button.textContent?.trim() ?? '');
    expect(tabs.some((label) => label.includes('patients.tab.documents'))).toBe(false);
    expect(tabs.some((label) => label.includes('patients.tab.visits'))).toBe(true);
  });

  it('lists documents of merged files with their own number and offers deactivation to files managers only', async () => {
    granted = new Set(['patients.view', 'files.view', 'files.create']);
    const host = await render();

    tab(host, 1).click();
    await flush();
    expect(host.querySelector('tbody tr[data-row]')?.textContent).toContain('referral.pdf');
    expect(host.querySelector('tbody tr[data-row]')?.textContent).toContain('PAT-2026-00000');
    expect(host.textContent).toContain('patients.document.attachTitle');
    expect(host.textContent).not.toContain('patients.document.deactivate');
  });

  it('shows the visits tab as unavailable until the registration module answers', async () => {
    granted = new Set(['patients.view', 'registration.view']);
    const host = await render();

    tab(host, 1).click();   // without files.view there is no Documents tab: Visits is the second (UAT A-03)
    await flush();
    backend.expectOne((r) => r.url === PATIENTS_API.visits).flush('', { status: 404, statusText: 'Not Found' });
    await flush();

    expect(host.textContent).toContain('patients.visit.unavailable');
  });

  it('offers no Visits tab and reads no visits or catalogue list for a patients reader without registration.view (round 2 R2-04)', async () => {
    granted = new Set(['patients.view']);
    const host = await render();
    const tabs = [...host.querySelectorAll<HTMLElement>('app-tabs [role="tab"]')].map((button) => button.textContent?.trim() ?? '');
    expect(tabs.some((label) => label.includes('patients.tab.visits'))).toBe(false);
    backend.expectNone((r) => r.url === PATIENTS_API.visits);
    backend.expectNone((r) => r.url.startsWith('/api/v1/catalogue/'));
  });

  it('shows a visit row in Arabic: Arabic branch name, translated status and Arabic-Indic count (round 3 R3-01)', async () => {
    granted = new Set(['patients.view', 'registration.view']);
    TestBed.inject(I18nService).setLanguage('ar');
    try {
      const host = await render();
      tab(host, 1).click();
      await flush();
      backend.expectOne((r) => r.url === PATIENTS_API.visits).flush({
        items: [{ id: 'v-1', accession: 'ACC-2026-00001', registeredAtUtc: '2026-10-01T09:00:00Z', status: 'Open', branchNameEn: 'Main Branch', branchNameAr: 'الفرع الرئيسي', servicesCount: 12 }],
      });
      await flush();
      const row = host.querySelector('app-data-table tbody tr')?.textContent ?? '';
      expect(row).toContain('الفرع الرئيسي');
      expect(row).not.toContain('Main Branch');
      expect(row).toContain('١٢');
      expect(row).toContain('shared.status.Open');
      expect(row).not.toMatch(/(^|[^.])Open/);
    } finally {
      TestBed.inject(I18nService).setLanguage('en');
    }
  });

  it('shows a merged file read-only with a link to its survivor', async () => {
    granted = new Set(['patients.view', 'patients.edit', 'patients.manage']);
    const host = await render({
      ...detail,
      status: 'Merged',
      merge: { survivorId: 'p-9', survivorNumber: 'PAT-2026-00009', mergedAtUtc: '2026-10-08T10:00:00Z', mergedByUserId: 'u-2', mergedByName: 'Admin', reason: 'registered twice' },
      mergedFiles: [],
    });

    expect(host.querySelector('.merged')?.textContent).toContain('registered twice');
    expect(host.querySelector('.merged a')?.getAttribute('href')).toBe('/patients/p-9');
    expect(host.textContent).not.toContain('patients.merge.open');
    expect(host.querySelector('app-page-header .btn.primary')).toBeNull();
  });

  it('shows the dates and the Documents count in Arabic-Indic digits on an Arabic page (round 2 R2-02)', async () => {
    granted = new Set(['patients.view', 'files.view']);
    TestBed.inject(I18nService).setLanguage('ar');
    try {
      const host = await render({ ...detail, dateOfBirthIsEstimated: true });
      const text = host.textContent ?? '';
      expect(text).not.toContain('1990-05-01');
      expect(text).not.toContain('2026-10-01');
      expect(text).toContain('١٩٩٠');
      expect(text).toContain('٢٠٢٦');
      expect(host.querySelector('app-tabs .count')?.textContent?.trim()).toBe('١');
    } finally {
      TestBed.inject(I18nService).setLanguage('en');
    }
  });

  it('shows the dates as days of the calendar in English, never shifted by the time zone (round 2 R2-02)', async () => {
    granted = new Set(['patients.view', 'files.view']);
    const host = await render();
    expect(host.textContent).toContain('1 May 1990');
    expect(host.textContent).toContain('1 Oct 2026');
    expect(host.querySelector('app-tabs .count')?.textContent?.trim()).toBe('1');
  });
});
