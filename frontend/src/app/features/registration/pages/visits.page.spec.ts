import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChangeDetectionStrategy, Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { PATIENTS_API, type PatientDetail } from '../../../core/api/patients.contracts';
import { PRICING_API } from '../../../core/api/pricing.contracts';
import { REGISTRATION_API, type CancelLinesRequest, type VisitListItem } from '../../../core/api/registration.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import type { TableExport } from '../../../shared/export/table-export';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import { TABLE_PREFERENCES, type TablePreferences } from '../../../shared/ui/data-table/table-preferences';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { VisitsFacade } from '../state/visits.facade';
import { cashTier, page as pageOf, settle, visitRow } from '../testing/registration.fixtures';
import { VisitsPage } from './visits.page';

/** The parts of the page the scenarios drive (protected in the page). */
interface VisitsPageInternals {
  export(): Promise<void>;
  openCancel(row: VisitListItem): void;
  cancelServices(input: { visitId: string; request: CancelLinesRequest }): Promise<void>;
  results(row: VisitListItem): void;
  details(row: VisitListItem): void;
  payments(row: VisitListItem): void;
  openPatient(row: VisitListItem): void;
  submitPatient(): void;
  readonly cancelOpen: () => boolean;
  readonly patientOpen: () => boolean;
}

const patientDetail: PatientDetail = {
  id: 'p-1',
  number: 'PAT-2026-00001',
  title: 'Mrs',
  fullName: 'Mona Adel',
  nameAr: 'منى عادل',
  gender: 'Female',
  dateOfBirth: '1990-05-01',
  dateOfBirthIsEstimated: false,
  age: { years: 36, months: 5, days: 9 },
  statedAge: null,
  ageRecordedOn: null,
  registeredOn: '2026-10-01',
  patientConditionId: null,
  mobile: '01001234567',
  nationalId: null,
  nationality: 'EG',
  email: null,
  governorateId: null,
  city: 'Cairo',
  address: null,
  comments: null,
  isVip: null,
  status: 'Active',
  merge: null,
  mergedFiles: [],
  createdAtUtc: '2026-10-01T08:00:00Z',
  createdByName: 'Clerk',
  modifiedAtUtc: null,
  modifiedByName: null,
  rowVersion: 7,
};

@Component({ selector: 'app-spec-blank', template: '', changeDetection: ChangeDetectionStrategy.OnPush })
class BlankComponent {}

let granted = new Set<string>();

/** LIS-CR-014 T2: the user's saved column choice, kept here instead of the profile. */
class SpecListPreferences implements TablePreferences {
  readonly lists = signal<Readonly<Record<string, readonly string[]>>>({});
  readonly saved: [string, readonly string[]][] = [];

  hidden(listKey: string): readonly string[] {
    return this.lists()[listKey] ?? [];
  }

  async save(listKey: string, hidden: readonly string[]): Promise<void> {
    this.saved.push([listKey, hidden]);
    this.lists.update((lists) => ({ ...lists, [listKey]: hidden }));
    await Promise.resolve();
  }
}

const listPreferences = new SpecListPreferences();

describe('VisitsPage (CP-10)', () => {
  let backend: HttpTestingController;
  let harness: RouterTestingHarness;

  beforeEach(() => {
    granted = new Set(['registration.view', 'registration.create', 'registration.edit', 'registration.cancel', 'registration.export', 'samples.view', 'billing.view', 'results.view']);
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthStore, useValue: { currentBranch: signal<string | null>('b-1'), hasPermission: (key: string) => granted.has(key) } satisfies Pick<AuthStore, 'currentBranch' | 'hasPermission'> },
        { provide: PermissionsService, useValue: { can: (key: string) => granted.has(key) } satisfies Pick<PermissionsService, 'can'> },
        { provide: TABLE_PREFERENCES, useValue: listPreferences },
        provideRouter([
          { path: 'registration/visits', providers: [VisitsFacade], component: VisitsPage },
          { path: 'results/by-accession', component: BlankComponent },
          { path: 'billing/payments', component: BlankComponent },
          { path: 'registration/accession', component: BlankComponent },
        ]),
      ],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  async function tick(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  async function open(rows: readonly VisitListItem[] = [visitRow()]): Promise<VisitsPageInternals> {
    harness = await RouterTestingHarness.create();
    const page = (await harness.navigateByUrl('/registration/visits', VisitsPage)) as unknown as VisitsPageInternals;
    await tick();
    backend.expectOne((request) => request.url === REGISTRATION_API.visits).flush(pageOf(rows));
    backend.expectOne((request) => request.url === PRICING_API.tierOptions).flush([cashTier]);
    await tick();
    harness.detectChanges();
    return page;
  }

  /** The row's inline actions by their text, or by their accessible name for an icon button. */
  function inlineActions(row: Element | null | undefined): string[] {
    return [...(row?.querySelectorAll<HTMLButtonElement>('.row-actions > button') ?? [])].map((button) => button.textContent?.trim() || (button.getAttribute('aria-label') ?? ''));
  }

  function menuItems(row: Element | null | undefined): string[] {
    return [...(row?.querySelectorAll('.row-menu-panel button') ?? [])].map((button) => button.textContent?.trim() ?? '');
  }

  it('exports the date as the local date and time shown in the grid, not raw UTC (UAT A-15)', async () => {
    const page = await open();
    const blobs: Blob[] = [];
    const original = URL.createObjectURL;
    URL.createObjectURL = (blob: Blob | MediaSource): string => {
      blobs.push(blob as Blob);
      return 'blob:spec';
    };
    try {
      const done = page.export();
      await tick();
      backend.expectOne((request) => request.url === REGISTRATION_API.exportVisits).flush([visitRow({ registeredAtUtc: '2026-10-11T07:28:26.129944+00:00' })]);
      await done;
    } finally {
      URL.createObjectURL = original;
    }

    const text = await blobs[0]!.text();
    expect(text).not.toContain('2026-10-11T07:28:26');
    expect(text).toContain(new Intl.DateTimeFormat('en-GB', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date('2026-10-11T07:28:26.129944+00:00')));
  });

  it('offers Branch, Payer, Patient share and Remaining to the column chooser, saved under "visits", and prints the filters (LIS-CR-014)', async () => {
    listPreferences.lists.set({});
    listPreferences.saved.length = 0;
    await open();
    const host = harness.routeNativeElement!;
    const headers = (): string[] => [...host.querySelectorAll('thead th:not(.sel):not(.actions)')].map((cell) => cell.textContent?.trim() ?? '');
    expect(headers()).toContain('registration.column.payer');

    host.querySelector<HTMLButtonElement>('app-column-chooser button')!.click();
    await tick();
    harness.detectChanges();
    const items = [...host.querySelectorAll('.column-chooser-panel .cc-item')];
    expect(items.map((item) => item.textContent?.trim())).toEqual(['registration.column.branch', 'registration.column.payer', 'registration.column.patientShare', 'registration.column.remaining']);
    items[1]!.querySelector('input')!.click();
    await tick();
    harness.detectChanges();
    expect(listPreferences.saved).toEqual([['visits', ['payer']]]);
    expect(headers()).not.toContain('registration.column.payer');

    // Print / Excel: the visible columns, the active filters as the subtitle, typed money cells
    expect([...host.querySelectorAll('.table-tools button')].map((button) => button.textContent?.trim())).toEqual(['shared.table.print', 'shared.table.excel']);
    const table = harness.fixture.debugElement.query((element) => element.componentInstance instanceof DataTableComponent).componentInstance as { exportDocument(): TableExport };
    const document = table.exportDocument();
    expect(document.title).toBe('registration.visits.title');
    expect(document.subtitle).toContain('registration.visits.branchOnly');
    expect(document.columns.map((column) => column.key)).not.toContain('payer');
    const share = document.rows[0]![document.columns.findIndex((column) => column.key === 'patientShareTotal')];
    expect(share).toEqual(expect.objectContaining({ numFmt: '#,##0.00' }));
    expect(document.rows[0]![document.columns.findIndex((column) => column.key === 'services')]).toBe('CBC, GLU');
    listPreferences.lists.set({});
  });

  it('lists the visits with the services styled by status, the balance and every row action for a full role', async () => {
    await open();
    const host = harness.routeNativeElement!;
    const row = host.querySelector('tbody tr[data-row]');
    expect(row).not.toBeNull();
    expect(row?.textContent).toContain('ACC-2026-00001');
    expect(row?.textContent).toContain('Mona Adel');
    expect(row?.querySelector('.chip.cancelled')?.textContent).toBe('GLU');
    expect(row?.querySelector('.chip:not(.cancelled)')?.textContent).toBe('CBC');
    expect(row?.textContent).toContain('60.00');
    // LIS-CR-007 C-06: Details, Edit, Payments and Documents inline, the rest (Patient needs patients.edit) in the More menu
    expect(inlineActions(row)).toEqual(['registration.accession.details', 'registration.visits.edit', 'registration.visits.payments ACC-2026-00001', 'registration.visits.documents ACC-2026-00001']);
    expect(row?.querySelector('app-row-menu')?.hasAttribute('hidden')).toBe(false);
    expect(menuItems(row)).toEqual(['registration.visits.samples', 'registration.visits.results', 'registration.cancel.action']);
    expect(host.querySelector('app-page-header .btn.primary')).not.toBeNull();
  });

  it('offers only the details and the documents to a read-only role and masks VIP patients', async () => {
    granted = new Set(['registration.view']);
    await open([visitRow({ patientMasked: true, patientName: null, mobile: null, remaining: 0 })]);
    const host = harness.routeNativeElement!;
    const row = host.querySelector('tbody tr[data-row]');
    expect(inlineActions(row)).toEqual(['registration.accession.details', 'registration.visits.documents ACC-2026-00001']);
    expect(menuItems(row)).toEqual([]);
    expect(row?.querySelector('app-row-menu')?.hasAttribute('hidden')).toBe(true);   // no empty More menu
    expect(row?.textContent).toContain('registration.vipMasked');
    expect(row?.textContent).not.toContain('Mona');
    expect(row?.textContent).toContain('registration.visits.settled');
    expect(host.querySelector('app-page-header .btn.primary')).toBeNull();
  });

  it('cancels services from the dialog, closes it and reloads the register', async () => {
    const page = await open();
    const row = visitRow();
    page.openCancel(row);
    expect(page.cancelOpen()).toBe(true);

    await Promise.all([
      page.cancelServices({ visitId: row.id, request: { lineIds: ['l-1'], reasonId: 'r-cancel', reason: null } }),
      (async () => {
        await settle();
        const post = backend.expectOne(`${REGISTRATION_API.visits}/v-1/lines/cancel`);
        expect(post.request.body).toEqual({ lineIds: ['l-1'], reasonId: 'r-cancel', reason: null });
        post.flush(null, { status: 204, statusText: 'No Content' });
      })(),
    ]);

    expect(page.cancelOpen()).toBe(false);
    expect(TestBed.inject(ToastService).toasts().some((toast) => toast.kind === 'success')).toBe(true);
    await tick();
    backend.match((request) => request.url === REGISTRATION_API.visits || request.url.startsWith(`${REGISTRATION_API.visits}/v-1`) || request.url.includes('/catalogue/reasons')).forEach((call) => call.flush(pageOf([])));
  });

  it('keeps the cancel dialog open with the refusal code', async () => {
    const page = await open();
    page.openCancel(visitRow());

    await Promise.all([
      page.cancelServices({ visitId: 'v-1', request: { lineIds: ['l-1'], reasonId: null, reason: 'typed' } }),
      (async () => {
        await settle();
        backend.expectOne(`${REGISTRATION_API.visits}/v-1/lines/cancel`).flush({ type: 'about:blank', title: 'Verified', status: 409, code: 'REG-106' }, { status: 409, statusText: 'Conflict' });
      })(),
    ]);

    expect(page.cancelOpen()).toBe(true);
    expect(harness.routeDebugElement!.injector.get(VisitsFacade).cancel.problem()?.code).toBe('REG-106');
  });

  /** Opens the patient drawer of the first row and answers the file, the lock of the visit and the form's lookups. */
  async function openPatient(page: VisitsPageInternals, canEditIdentity: boolean): Promise<HTMLElement> {
    page.openPatient(visitRow());
    await tick();
    backend.expectOne(`${PATIENTS_API.patients}/p-1`).flush(patientDetail);
    const lock = backend.expectOne((request) => request.url === `${PATIENTS_API.patients}/p-1/demographics-lock`);
    expect(lock.request.params.get('visitId')).toBe('v-1');
    lock.flush({ highestStatus: 'Verified', canEditIdentity, editableUpTo: 'Complete' });
    backend.match((request) => request.url.startsWith(PATIENTS_API.patients) || request.url.includes('/public/')).forEach((call) => call.flush([]));
    await tick();
    harness.detectChanges();
    return harness.routeNativeElement!;
  }

  it('offers the patient file to patients.edit, loads the lock of the visit and disables the identity fields', async () => {
    granted.add('patients.edit');
    const page = await open();
    const host = harness.routeNativeElement!;
    expect(menuItems(host.querySelector('tbody tr[data-row]'))[0]).toBe('registration.visits.patient');

    await openPatient(page, false);
    expect(page.patientOpen()).toBe(true);
    expect(host.querySelector('app-drawer app-patient-form')).not.toBeNull();
    expect(host.querySelector('.lock-hint')?.textContent).toContain('registration.patient.locked');
    for (const field of ['title', 'fullName', 'nameAr', 'dateOfBirth', 'ageYears', 'nationalId']) {
      expect(host.querySelector<HTMLInputElement>(`[formcontrolname="${field}"]`)?.disabled).toBe(true);
    }

    expect(host.querySelector<HTMLInputElement>('input[type="radio"][formcontrolname="gender"]')?.disabled).toBe(true);
    for (const field of ['mobile', 'email', 'city', 'address', 'comments']) {
      expect(host.querySelector<HTMLInputElement>(`[formcontrolname="${field}"]`)?.disabled).toBe(false);
    }
  });

  it('saves the file with the visit id and the row version, closes the drawer and reloads the register', async () => {
    granted.add('patients.edit');
    const page = await open();
    const host = await openPatient(page, true);
    expect(host.querySelector('.lock-hint')).toBeNull();
    expect(host.querySelector<HTMLInputElement>('[formcontrolname="fullName"]')?.disabled).toBe(false);

    const mobile = host.querySelector<HTMLInputElement>('[formcontrolname="mobile"]')!;
    mobile.value = '01112223333';
    mobile.dispatchEvent(new Event('input', { bubbles: true }));
    await tick();

    page.submitPatient();
    await settle();
    const put = backend.expectOne((request) => request.method === 'PUT' && request.url === `${PATIENTS_API.patients}/p-1`);
    expect(put.request.body).toEqual(expect.objectContaining({ visitId: 'v-1', rowVersion: 7, fullName: 'Mona Adel', mobile: '01112223333' }));
    put.flush(null, { status: 204, statusText: 'No Content' });
    await tick();
    await tick();   // the reload is asked in the mutation's continuation; the resource issues it on the next effect flush

    expect(page.patientOpen()).toBe(false);
    expect(TestBed.inject(ToastService).toasts().some((toast) => toast.kind === 'success')).toBe(true);
    backend.expectOne((request) => request.url === REGISTRATION_API.visits).flush(pageOf([visitRow({ mobile: '01112223333' })]));
    await tick();
    harness.detectChanges();
    expect(harness.routeNativeElement!.querySelector('tbody tr[data-row]')?.textContent).toContain('01112223333');
  });

  it('links a row to the results by accession and to the cash desk by visit', async () => {
    const page = await open();
    const router = TestBed.inject(Router);

    page.results(visitRow());
    await settle();
    expect(router.url).toBe('/results/by-accession?accession=ACC-2026-00001');

    page.payments(visitRow());
    await settle();
    expect(router.url).toBe('/billing/payments?visitId=v-1');

    page.details(visitRow());
    await settle();
    expect(router.url).toBe('/registration/accession?accession=ACC-2026-00001');
  });

  it('runs an action of the More menu and closes the menu (LIS-CR-007 C-06)', async () => {
    await open();
    const router = TestBed.inject(Router);
    const row = harness.routeNativeElement!.querySelector('tbody tr[data-row]')!;
    const trigger = row.querySelector<HTMLButtonElement>('app-row-menu > button')!;
    const panel = row.querySelector<HTMLElement>('.row-menu-panel')!;
    expect(panel.hidden).toBe(true);

    trigger.click();
    await tick();
    expect(trigger.getAttribute('aria-expanded')).toBe('true');
    expect(panel.hidden).toBe(false);

    [...panel.querySelectorAll<HTMLButtonElement>('button')].find((button) => button.textContent?.includes('registration.visits.results'))!.click();
    await tick();
    expect(router.url).toBe('/results/by-accession?accession=ACC-2026-00001');
  });
});
