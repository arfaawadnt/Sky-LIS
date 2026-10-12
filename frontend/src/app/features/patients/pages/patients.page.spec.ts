import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { PATIENTS_API, type PatientListItem } from '../../../core/api/patients.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { PatientsFacade } from '../state/patients.facade';
import { PatientsPage } from './patients.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));
let granted = new Set<string>();

const row: PatientListItem = {
  id: 'p-1',
  number: 'PAT-2026-00001',
  title: 'Mrs',
  fullName: 'Mona Hassan',
  nameAr: 'منى حسن',
  gender: 'Female',
  dateOfBirth: '1990-05-01',
  dateOfBirthIsEstimated: false,
  age: { years: 36, months: 5, days: 8 },
  mobile: '01001234567',
  nationalId: null,
  patientConditionId: null,
  isVip: true,
  status: 'Active',
  mergedIntoPatientId: null,
  mergedIntoNumber: null,
  registeredOn: '2026-10-01',
  visitCount: 3,
  lastVisitAtUtc: '2026-10-10T07:30:00Z',
};

describe('PatientsPage (CP-11 register)', () => {
  let backend: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PatientsPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        PatientsFacade,
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

  async function render(items: readonly PatientListItem[] = [row]): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(PatientsPage);
    await flush();
    backend.expectOne((r) => r.url === PATIENTS_API.patients).flush({ items, page: 1, pageSize: 50, totalCount: items.length, totalPages: 1 });
    backend.match(() => true).forEach((call) => call.flush(call.request.url.endsWith('form-settings') ? { mandatoryMobile: false, mandatoryNationalId: false, similarNameWarning: true } : []));
    await flush();
    return fixture.nativeElement as HTMLElement;
  }

  it('lists the files without the VIP column, filter or create button for a read-only user', async () => {
    granted = new Set(['patients.view']);
    const host = await render();

    expect(host.querySelectorAll('tbody tr[data-row]').length).toBe(1);
    expect(host.querySelector('tbody tr[data-row]')?.textContent).toContain('PAT-2026-00001');
    expect(host.querySelector('tbody tr[data-row]')?.textContent).toContain('منى حسن');
    expect(host.textContent).not.toContain('patients.column.vip');
    expect(host.querySelector('app-page-header .btn.primary')).toBeNull();
  });

  it('shows a masked VIP row as "VIP patient" with no identity (validation A-03)', async () => {
    granted = new Set(['patients.view']);
    const masked: PatientListItem = { ...row, fullName: '', nameAr: null, mobile: null, nationalId: null, isVip: null, identityMasked: true };
    const host = await render([masked]);

    const text = host.querySelector('tbody tr[data-row]')?.textContent ?? '';
    expect(text).toContain('PAT-2026-00001');
    expect(text).toContain('patients.vipMasked');
    expect(text).not.toContain('01001234567');
    expect(text).not.toContain('منى');
  });

  it('shows the visit count and the last visit of each file (CP-11, UAT A-09)', async () => {
    granted = new Set(['patients.view']);
    const host = await render();

    expect(host.textContent).toContain('patients.column.visits');
    expect(host.textContent).toContain('patients.column.lastVisit');
    const cells = [...(host.querySelector('tbody tr[data-row]')?.querySelectorAll('td') ?? [])].map((cell) => cell.textContent?.trim());
    expect(cells).toContain('3');
    expect(cells.some((text) => text?.includes('2026'))).toBe(true);
  });

  it('reads the conditions from the patients API, never the catalogue, and names the condition of the file (round 2 R2-04)', async () => {
    granted = new Set(['patients.view']);
    const fixture = TestBed.createComponent(PatientsPage);
    await flush();
    backend.expectOne((r) => r.url === PATIENTS_API.patients).flush({ items: [{ ...row, patientConditionId: 'c-1' }], page: 1, pageSize: 50, totalCount: 1, totalPages: 1 });
    const conditions = backend.expectOne((r) => r.url === '/api/v1/patients/conditions');
    expect(conditions.request.method).toBe('GET');
    conditions.flush([{ id: 'c-1', nameEn: 'Pregnant', nameAr: 'حامل' }]);
    backend.expectNone((r) => r.url.startsWith('/api/v1/catalogue/'));
    backend.match(() => true).forEach((call) => call.flush(call.request.url.endsWith('form-settings') ? { mandatoryMobile: false, mandatoryNationalId: false, similarNameWarning: true } : []));
    await flush();

    expect((fixture.nativeElement as HTMLElement).querySelector('tbody tr[data-row]')?.textContent).toContain('Pregnant');
  });

  it('shows the visit count in Arabic-Indic digits on an Arabic page (round 2 R2-02)', async () => {
    granted = new Set(['patients.view']);
    TestBed.inject(I18nService).setLanguage('ar');
    try {
      const host = await render([{ ...row, visitCount: 16 }]);
      const cells = [...(host.querySelector('tbody tr[data-row]')?.querySelectorAll('td') ?? [])].map((cell) => cell.textContent?.trim());
      expect(cells).toContain('١٦');
      expect(cells).not.toContain('16');
    } finally {
      TestBed.inject(I18nService).setLanguage('en');
    }
  });

  it('shows VIP to the confidential right and opens the create drawer for a creator', async () => {
    granted = new Set(['patients.view', 'patients.create', 'patients.viewConfidential']);
    const host = await render();

    expect(host.textContent).toContain('patients.column.vip');
    expect(host.querySelector('tbody tr[data-row] app-badge')?.textContent).toContain('patients.vip');

    host.querySelector<HTMLButtonElement>('app-page-header .btn.primary')?.click();
    await flush();
    const dialog = host.querySelector<HTMLElement>('[role="dialog"]');
    expect(dialog).not.toBeNull();
    expect(dialog?.querySelector('app-patient-form')).not.toBeNull();
    expect(dialog?.querySelector('[formcontrolname="isVip"]')).not.toBeNull();

    // Saving an empty form shows the errors and sends nothing.
    dialog?.querySelector<HTMLButtonElement>('.foot .btn.primary')?.click();
    await flush();
    backend.expectNone((r) => r.method === 'POST');
    expect(dialog?.querySelectorAll('app-form-field .error').length).toBeGreaterThan(0);
  });

  it('narrows the register by search and status', async () => {
    granted = new Set(['patients.view']);
    const host = await render();

    const status = host.querySelectorAll<HTMLSelectElement>('app-toolbar select')[1];
    if (!status) {
      throw new Error('status filter missing');
    }

    status.value = 'Merged';
    status.dispatchEvent(new Event('change'));
    await flush();

    const request = backend.expectOne((r) => r.url === PATIENTS_API.patients);
    expect(request.request.params.get('status')).toBe('Merged');
    expect(request.request.params.has('vip')).toBe(false);
    request.flush({ items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 });
  });
});
