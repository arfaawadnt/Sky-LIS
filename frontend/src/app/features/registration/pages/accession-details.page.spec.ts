import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChangeDetectionStrategy, Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { BILLING_API, type VisitAccountView } from '../../../core/api/billing.contracts';
import { REGISTRATION_API, type VisitDetail, type VisitLineItem, type VisitListItem } from '../../../core/api/registration.contracts';
import { RESULTS_API } from '../../../core/api/results.contracts';
import { SAMPLES_API, type SampleListItem } from '../../../core/api/samples.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { AccessionFacade } from '../state/accession.facade';
import { RegistrationPrinter } from '../state/registration-printer';
import { page as pageOf, settle, visitDetail, visitRow } from '../testing/registration.fixtures';
import { AccessionDetailsPage } from './accession-details.page';

/** The parts of the page the scenarios drive (protected in the page). */
interface AccessionPageInternals {
  onSearch(text: string): Promise<void>;
  pickMatch(row: VisitListItem): void;
  move(direction: -1 | 1): Promise<void>;
  openCancel(line: VisitLineItem): void;
  collect(sample: SampleListItem): Promise<void>;
  readonly cancelOpen: () => boolean;
  readonly cancelLines: () => readonly string[];
}

@Component({ selector: 'app-spec-blank', template: '', changeDetection: ChangeDetectionStrategy.OnPush })
class BlankComponent {}

const sample: SampleListItem = {
  id: 's-1',
  sampleNumber: 'SMP-2026-00001',
  tubeId: 'tube-1',
  generation: 1,
  visitId: 'v-1',
  accession: 'ACC-2026-00001',
  patientId: 'p-1',
  patientNumber: 'PAT-2026-00001',
  patientName: 'Mona Adel',
  patientNameAr: 'منى عادل',
  branchId: 'b-1',
  clinicalGroupId: 'cg-1',
  unitCode: 'HEM',
  clinicalGroupNameEn: 'Haematology',
  clinicalGroupNameAr: 'أمراض الدم',
  sampleTypeId: 'st-1',
  sampleTypeNameEn: 'EDTA blood',
  sampleTypeNameAr: 'دم EDTA',
  sampleConditionId: null,
  sampleConditionNameEn: null,
  sampleConditionNameAr: null,
  serviceCount: 1,
  status: 'Ordered',
  orderedAtUtc: '2026-10-10T08:00:00Z',
  statusChangedAtUtc: '2026-10-10T08:00:00Z',
  collectedAtUtc: null,
  receivedAtUtc: null,
  rejectionReasonId: null,
  rejectionReasonNameEn: null,
  rejectionReasonNameAr: null,
  rejectionNote: null,
  rejectedAtUtc: null,
  replacedBySampleId: null,
  replacesSampleId: null,
  collectedOutside: true,
};

const account = {
  visitId: 'v-1',
  accession: 'ACC-2026-00001',
  patientShareTotal: 100,
  paid: 40,
  refunded: 0,
  remaining: 60,
  refundable: 40,
  balanceStatus: 'Partial',
  currency: 'EGP',
  payments: [
    { id: 'pay-1', receiptNumber: 'RCP-2026-00001', visitId: 'v-1', accession: 'ACC-2026-00001', amount: 40, method: 'Cash', reference: null, cashierUserId: 'u-1', cashierName: 'Cashier One', branchId: 'b-1', batchId: null, paidAtUtc: '2026-10-10T08:10:00Z', status: 'Posted', isFrozen: false, voidReason: null, voidedByName: null, voidedAtUtc: null },
  ],
  refunds: [],
} as unknown as VisitAccountView;

const resultCase = {
  visit: { visitId: 'v-1', accession: 'ACC-2026-00001' },
  patient: {},
  units: [{ key: 'u-1', groupNameEn: 'Haematology', groupNameAr: 'أمراض الدم', printAloneServiceId: null, status: 'Verified', isPrinted: false, printCount: 0, hasPanic: true, hasDelta: false, results: [{ id: 'r-1', lineId: 'l-1', status: 'Verified' }] }],
  hiddenUnits: 1,
  hiddenResults: 0,
  scannedSampleNumber: null,
  settings: {},
};

let granted = new Set<string>();

describe('AccessionDetailsPage (CP-10b)', () => {
  let backend: HttpTestingController;
  let harness: RouterTestingHarness;
  let confirmAnswer = true;
  const printed: string[] = [];

  beforeEach(() => {
    granted = new Set(['registration.view', 'registration.edit', 'registration.cancel', 'patients.edit', 'samples.view', 'samples.create', 'billing.view', 'billing.create', 'billing.approve', 'results.view', 'results.create']);
    confirmAnswer = true;
    printed.length = 0;
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthStore, useValue: { currentBranch: signal<string | null>('b-1'), hasPermission: (key: string) => granted.has(key) } satisfies Pick<AuthStore, 'currentBranch' | 'hasPermission'> },
        { provide: PermissionsService, useValue: { can: (key: string) => granted.has(key) } satisfies Pick<PermissionsService, 'can'> },
        { provide: ConfirmService, useValue: { confirm: () => Promise.resolve(confirmAnswer) } satisfies Pick<ConfirmService, 'confirm'> },
        {
          provide: RegistrationPrinter,
          useValue: {
            labels: (visitId: string, tubeId: string | null = null) => {
              printed.push(`labels:${visitId}:${tubeId ?? '*'}`);
              return Promise.resolve(1);
            },
            jobOrder: (visitId: string) => {
              printed.push(`jobOrder:${visitId}`);
              return Promise.resolve();
            },
            receipt: (paymentId: string) => {
              printed.push(`receipt:${paymentId}`);
              return Promise.resolve();
            },
          } satisfies Pick<RegistrationPrinter, 'labels' | 'jobOrder' | 'receipt'>,
        },
        provideRouter(
          [
            { path: 'registration/accession', providers: [AccessionFacade], component: AccessionDetailsPage },
            { path: 'registration/visits/:visitId', component: BlankComponent },
            { path: 'results/by-accession', component: BlankComponent },
            { path: 'billing/payments', component: BlankComponent },
          ],
          withComponentInputBinding(),
        ),
      ],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  /** Two rounds: a resolved resource makes its dependants (the cards keyed on the visit id) load on the next tick. */
  async function tick(): Promise<void> {
    TestBed.tick();
    await settle();
    TestBed.tick();
    await settle();
  }

  /** Answers the lookup, the detail and the three cards of a visit opened by accession. */
  async function answerVisit(detail: VisitDetail = visitDetail(), samples: readonly SampleListItem[] = [sample]): Promise<void> {
    await tick();
    backend.expectOne((request) => request.url === REGISTRATION_API.visits).flush(pageOf([visitRow({ id: detail.id, accession: detail.accession })]));
    await settle();
    backend.expectOne(`${REGISTRATION_API.visits}/${detail.id}`).flush(detail);
    await tick();
    backend.match((request) => request.url === SAMPLES_API.samples).forEach((call) => call.flush(pageOf(samples)));
    backend.match((request) => request.url === `${BILLING_API.visits}/${detail.id}/account`).forEach((call) => call.flush(account));
    backend.match((request) => request.url === RESULTS_API.results).forEach((call) => call.flush(resultCase));
    await tick();
    harness.detectChanges();
  }

  async function open(url = '/registration/accession?accession=ACC-2026-00001'): Promise<AccessionPageInternals> {
    harness = await RouterTestingHarness.create();
    const page = (await harness.navigateByUrl(url, AccessionDetailsPage)) as unknown as AccessionPageInternals;
    return page;
  }

  it('opens the accession of the URL and renders the header and every card for a full role', async () => {
    await open();
    await answerVisit();
    const host = harness.routeNativeElement!;
    const text = host.textContent ?? '';

    expect(host.querySelector('app-reg-accession-header')?.textContent).toContain('ACC-2026-00001');
    expect(host.querySelector('app-reg-accession-header')?.textContent).toContain('Mona Adel');
    expect(host.querySelector('app-reg-accession-header .btn')).not.toBeNull();   // edit registration
    expect(host.querySelector('app-reg-accession-lines tr[data-line="l-1"]')?.textContent).toContain('CBC');
    expect(host.querySelector('app-reg-accession-lines tr[data-line="l-1"]')?.textContent).toContain('shared.status.Verified');
    expect(host.querySelector('app-reg-accession-lines tr[data-line="l-1"] .btn.danger')).toBeNull();   // a verified result locks the line (REG-106)
    expect(host.querySelector('app-reg-accession-lines tr[data-line="l-1"] [data-locked]')?.textContent).toContain('registration.accession.lines.locked');
    expect(host.querySelector('app-reg-accession-samples tr[data-sample="SMP-2026-00001"] .btn.primary')).not.toBeNull();   // collect
    expect(host.querySelector('app-reg-accession-samples tr[data-sample="SMP-2026-00001"]')?.textContent).toContain('registration.sampleFlags.outside');   // LIS-CR-008 C-02
    expect(text).toContain('Haematology');
    expect(text).toContain('registration.accession.results.hidden');
    expect(host.querySelector('app-reg-accession-payments tr[data-receipt="RCP-2026-00001"]')?.textContent).toContain('Cashier One');
    expect(host.querySelector('app-reg-accession-payments a.btn.primary')?.getAttribute('href')).toBe('/billing/payments?visitId=v-1');
    expect(host.querySelectorAll('app-reg-accession-payments a.btn').length).toBe(3);   // take payment, refund, all payments
  });

  it('hides the gated cards and buttons from a read-only role', async () => {
    granted = new Set(['registration.view']);
    await open();
    await answerVisit();
    const host = harness.routeNativeElement!;

    expect(host.querySelector('app-reg-accession-header')).not.toBeNull();
    expect(host.querySelector('app-reg-accession-header .btn:not([aria-label])')).toBeNull();   // no edit registration
    expect(host.querySelector('app-reg-accession-header button.link')).toBeNull();   // the name opens no patient file
    expect(host.querySelector('app-reg-accession-lines .btn.danger')).toBeNull();
    expect(host.querySelector('app-reg-accession-samples')).toBeNull();
    expect(host.querySelector('app-reg-accession-results')).toBeNull();
    expect(host.querySelector('app-reg-accession-payments')).toBeNull();
    backend.expectNone((request) => request.url === SAMPLES_API.samples);
    backend.expectNone((request) => request.url === RESULTS_API.results);
  });

  it('search: several matches show the list, picking one navigates; a single match navigates at once', async () => {
    const page = await open('/registration/accession');
    await tick();
    harness.detectChanges();
    expect(harness.routeNativeElement!.querySelector('app-empty-state')).not.toBeNull();

    const several = page.onSearch('01001234567');
    await settle();
    backend.expectOne((request) => request.url === REGISTRATION_API.visits && request.params.get('mobile') === '01001234567').flush(pageOf([visitRow(), visitRow({ id: 'v-2', accession: 'ACC-2026-00002' })]));
    await several;
    await tick();
    harness.detectChanges();
    const list = harness.routeNativeElement!.querySelectorAll('app-reg-accession-search .match');
    expect(list.length).toBe(2);

    page.pickMatch(visitRow({ id: 'v-2', accession: 'ACC-2026-00002' }));
    await settle();
    expect(TestBed.inject(Router).url).toBe('/registration/accession?accession=ACC-2026-00002');
    await answerVisit(visitDetail({ id: 'v-2', accession: 'ACC-2026-00002' }));
    expect(harness.routeNativeElement!.querySelector('app-reg-accession-search .match')).toBeNull();

    const single = page.onSearch('Mona');
    await settle();
    backend.expectOne((request) => request.url === REGISTRATION_API.visits && request.params.get('name') === 'Mona').flush(pageOf([visitRow()]));
    await single;
    expect(TestBed.inject(Router).url).toBe('/registration/accession?accession=ACC-2026-00001');
    await answerVisit();
  });

  it('says so when nothing matches and when the URL names an unknown accession', async () => {
    const page = await open('/registration/accession?accession=ACC-2026-09999');
    await tick();
    backend.expectOne((request) => request.url === REGISTRATION_API.visits).flush(pageOf([]));
    await tick();
    harness.detectChanges();
    expect(harness.routeNativeElement!.querySelector('app-empty-state')?.textContent).toContain('registration.accession.notFound');

    const none = page.onSearch('PAT-2026-77777');
    await settle();
    backend.expectOne((request) => request.url === REGISTRATION_API.visits).flush(pageOf([]));
    await none;
    expect(TestBed.inject(ToastService).toasts().some((toast) => toast.kind === 'info')).toBe(true);
  });

  it('moves to the next accession when it exists and only says so when it does not', async () => {
    const page = await open();
    await answerVisit();

    const missing = page.move(1);
    await settle();
    backend.expectOne((request) => request.url === REGISTRATION_API.visits && request.params.get('accession') === 'ACC-2026-00002').flush(pageOf([]));
    await missing;
    expect(TestBed.inject(Router).url).toBe('/registration/accession?accession=ACC-2026-00001');
    expect(TestBed.inject(ToastService).toasts().some((toast) => toast.kind === 'info')).toBe(true);

    const found = page.move(1);
    await settle();
    backend.expectOne((request) => request.url === REGISTRATION_API.visits && request.params.get('accession') === 'ACC-2026-00002').flush(pageOf([visitRow({ id: 'v-2', accession: 'ACC-2026-00002' })]));
    await found;
    expect(TestBed.inject(Router).url).toBe('/registration/accession?accession=ACC-2026-00002');
    await answerVisit(visitDetail({ id: 'v-2', accession: 'ACC-2026-00002' }));
  });

  it('opens the cancel dialog with the row preselected and collects a sample after a confirm', async () => {
    const page = await open();
    await answerVisit();
    const line = visitDetail().lines[0]!;

    page.openCancel(line);
    expect(page.cancelOpen()).toBe(true);
    expect(page.cancelLines()).toEqual(['l-1']);
    await tick();
    harness.detectChanges();
    backend.match((request) => request.url.startsWith(`${REGISTRATION_API.visits}/v-1`)).forEach((call) => call.flush(visitDetail()));
    backend.match((request) => request.url.includes('/catalogue/reasons')).forEach((call) => call.flush(pageOf([])));
    await tick();
    harness.detectChanges();
    expect(harness.routeNativeElement!.querySelector<HTMLInputElement>('app-reg-cancel-services-dialog input[type="checkbox"]')?.checked).toBe(true);

    confirmAnswer = false;
    await page.collect(sample);
    backend.expectNone(SAMPLES_API.scan);

    confirmAnswer = true;
    const collect = page.collect(sample);
    await settle();
    backend.expectOne(SAMPLES_API.scan).flush({ summary: { ...sample, status: 'Collected' } });
    await collect;
    expect(TestBed.inject(ToastService).toasts().some((toast) => toast.kind === 'success')).toBe(true);
    await tick();
    backend.match(() => true).forEach((call) => call.flush(pageOf([])));
  });
});
