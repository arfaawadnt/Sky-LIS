import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChangeDetectionStrategy, Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { PATIENTS_API, type PatientDetail, type PatientListItem, type PatientSummary } from '../../core/api/patients.contracts';
import { PermissionsService } from '../../core/permissions/permissions.service';
import { PatientPickerComponent } from './patient-picker.component';

@Component({
  imports: [PatientPickerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<app-patient-picker [selected]="selected()" (picked)="picked.push($event); selected.set($event)" (cleared)="selected.set(null)" />`,
})
class HostComponent {
  readonly selected = signal<PatientSummary | null>(null);
  readonly picked: PatientSummary[] = [];
}

const settle = (ms = 0): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms));

const mona: PatientListItem = {
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
  isVip: null,
  status: 'Active',
  mergedIntoPatientId: null,
  mergedIntoNumber: null,
  registeredOn: '2026-10-01',
};

function type(input: HTMLInputElement | null | undefined, value: string): void {
  if (!input) {
    throw new Error('input not found');
  }

  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
}

describe('PatientPickerComponent (CP-06 patient search / create)', () => {
  let backend: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: PermissionsService, useValue: { can: (key: string) => key === 'patients.create' } satisfies Pick<PermissionsService, 'can'> },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
  });

  async function render(): Promise<{ fixture: ReturnType<typeof TestBed.createComponent<HostComponent>>; host: HTMLElement }> {
    const fixture = TestBed.createComponent(HostComponent);
    await flush();
    return { fixture, host: fixture.nativeElement as HTMLElement };
  }

  /** Change detection and the resource effects without whenStable() (it would wait for the very requests the test flushes). */
  async function flush(): Promise<void> {
    TestBed.tick();
    await settle();
    TestBed.tick();
    await settle();
  }

  it('searches active files and emits the chosen one as a summary', async () => {
    const { fixture, host } = await render();

    const search = host.querySelector<HTMLInputElement>('app-search-box input');
    type(search, 'mona');
    search?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await flush();

    const request = backend.expectOne((r) => r.url === PATIENTS_API.patients);
    expect(request.request.params.get('q')).toBe('mona');
    expect(request.request.params.get('status')).toBe('Active');
    request.flush({ items: [mona], page: 1, pageSize: 10, totalCount: 1, totalPages: 1 });
    await flush();

    host.querySelector<HTMLButtonElement>('button.result')?.click();
    await flush();

    expect(fixture.componentInstance.picked).toEqual([
      {
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
        isVip: null,
      },
    ]);
    expect(host.querySelector('.chosen .number')?.textContent).toContain('PAT-2026-00001');
  });

  it('disables the previous results as soon as the text changes, until the new search answers (UAT A-09)', async () => {
    const { fixture, host } = await render();
    const search = host.querySelector<HTMLInputElement>('app-search-box input');
    type(search, 'mona');
    search?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await flush();
    backend.expectOne((r) => r.url === PATIENTS_API.patients).flush({ items: [mona], page: 1, pageSize: 10, totalCount: 1, totalPages: 1 });
    await flush();
    expect(host.querySelector<HTMLButtonElement>('button.result')?.disabled).toBe(false);

    type(search, 'walk-in two');
    await flush();
    const stale = host.querySelector<HTMLButtonElement>('button.result');
    expect(stale?.disabled).toBe(true);
    stale?.click();
    await flush();
    expect(fixture.componentInstance.picked).toEqual([]);

    search?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await flush();
    backend.expectOne((r) => r.url === PATIENTS_API.patients && r.params.get('q') === 'walk-in two').flush({ items: [mona], page: 1, pageSize: 10, totalCount: 1, totalPages: 1 });
    await flush();
    expect(host.querySelector<HTMLButtonElement>('button.result')?.disabled).toBe(false);
  });

  it('creates a new file inline, warns about similar files and emits the registered file', async () => {
    const { fixture, host } = await render();

    host.querySelector<HTMLButtonElement>('.search-row .btn.primary')?.click();
    await flush();
    backend.expectOne(`${PATIENTS_API.patients}/form-settings`).flush({ mandatoryMobile: false, mandatoryNationalId: false, similarNameWarning: true });
    backend.match(() => true).forEach((call) => call.flush([]));
    await flush();

    const form = host.querySelector<HTMLElement>('app-patient-form');
    type(form?.querySelector<HTMLInputElement>('input[formcontrolname="fullName"]'), 'Omar Said');
    type(form?.querySelector<HTMLInputElement>('input[formcontrolname="ageYears"]'), '40');
    type(form?.querySelector<HTMLInputElement>('input[formcontrolname="mobile"]'), '0100 555 6666');
    await settle(400);   // the similar lookup is debounced
    await flush();

    const similar = backend.expectOne((r) => r.url === `${PATIENTS_API.patients}/similar`);
    expect(similar.request.params.get('mobile')).toBe('01005556666');
    similar.flush({ warningEnabled: true, candidates: [{ ...mona, id: 'p-9', number: 'PAT-2026-00009', fullName: 'Omar Saeed', match: 'Mobile' }] });
    await flush();
    expect(host.querySelectorAll('.similar-item').length).toBe(1);

    host.querySelector<HTMLButtonElement>('.create-actions .btn.primary')?.click();
    await flush();

    const post = backend.expectOne((r) => r.method === 'POST' && r.url === PATIENTS_API.patients);
    const body = post.request.body as Record<string, unknown>;
    expect(body['fullName']).toBe('Omar Said');
    expect(body['dateOfBirth']).toBeNull();
    expect(body['age']).toEqual({ years: 40, months: 0, days: 0 });
    expect(body['mobile']).toBe('01005556666');
    expect(body['isVip']).toBeNull();
    post.flush({ id: 'p-10', number: 'PAT-2026-00010' }, { status: 201, statusText: 'Created' });
    await flush();

    const created: Partial<PatientDetail> = { ...mona, id: 'p-10', number: 'PAT-2026-00010', fullName: 'Omar Said', gender: 'Male', title: 'Mr', isVip: null, dateOfBirthIsEstimated: true };
    backend.expectOne(`${PATIENTS_API.patients}/p-10`).flush(created);
    await flush();

    expect(fixture.componentInstance.picked.map((patient) => patient.number)).toEqual(['PAT-2026-00010']);
    expect(host.querySelector('app-patient-form')).toBeNull();
  });

  it('uses an existing file from the similar warning instead of creating a duplicate', async () => {
    const { fixture, host } = await render();

    host.querySelector<HTMLButtonElement>('.search-row .btn.primary')?.click();
    await flush();
    backend.match(() => true).forEach((call) => call.flush(call.request.url.endsWith('form-settings') ? { mandatoryMobile: false, mandatoryNationalId: false, similarNameWarning: true } : []));
    await flush();

    type(host.querySelector<HTMLInputElement>('input[formcontrolname="fullName"]'), 'Mona Hassan');
    await settle(400);
    await flush();
    backend.expectOne((r) => r.url === `${PATIENTS_API.patients}/similar`).flush({ warningEnabled: true, candidates: [{ ...mona, match: 'Name' }] });
    await flush();

    host.querySelector<HTMLButtonElement>('.similar-item .btn')?.click();
    await flush();

    expect(fixture.componentInstance.picked.map((patient) => patient.id)).toEqual(['p-1']);
    backend.expectNone((r) => r.method === 'POST');
  });
});
