import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { PATIENTS_API, type PatientListItem } from '../../../core/api/patients.contracts';
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

  async function render(): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(PatientsPage);
    await flush();
    backend.expectOne((r) => r.url === PATIENTS_API.patients).flush({ items: [row], page: 1, pageSize: 50, totalCount: 1, totalPages: 1 });
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

  it('shows the visit count and the last visit of each file (CP-11, UAT A-09)', async () => {
    granted = new Set(['patients.view']);
    const host = await render();

    expect(host.textContent).toContain('patients.column.visits');
    expect(host.textContent).toContain('patients.column.lastVisit');
    const cells = [...(host.querySelector('tbody tr[data-row]')?.querySelectorAll('td') ?? [])].map((cell) => cell.textContent?.trim());
    expect(cells).toContain('3');
    expect(cells.some((text) => text?.includes('2026'))).toBe(true);
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
