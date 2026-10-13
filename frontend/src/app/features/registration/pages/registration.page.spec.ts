import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { BILLING_API } from '../../../core/api/billing.contracts';
import { PATIENTS_API } from '../../../core/api/patients.contracts';
import { REGISTRATION_API, type RegisterVisitRequest } from '../../../core/api/registration.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { I18nService } from '../../../core/i18n/i18n.service';
import { DeviceIdentityService } from '../../../core/device/device-identity.service';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { RegistrationFacade } from '../state/registration.facade';
import { RegistrationPrinter } from '../state/registration-printer';
import { checkResult, flushReferenceLists, patient, settle, visitDetail } from '../testing/registration.fixtures';
import type { PickedService } from '../ui/service-entry.component';
import { RegistrationPage } from './registration.page';

/** The parts of the page the scenarios drive (protected in the page). */
interface RegistrationPageInternals {
  save(): Promise<void>;
  onAdded(services: readonly PickedService[]): void;
  release(line: { id: string; code: string }): Promise<void>;
  readonly successOpen: () => boolean;
  readonly success: () => { readonly visit: { readonly accession: string }; readonly payment: unknown; readonly paymentProblem: { readonly code: string } | null } | null;
}

class PrinterStub {
  readonly printed: string[] = [];

  labels(visitId: string): Promise<number> {
    this.printed.push(`labels:${visitId}`);
    return Promise.resolve(1);
  }

  jobOrder(visitId: string): Promise<void> {
    this.printed.push(`jobOrder:${visitId}`);
    return Promise.resolve();
  }

  receipt(paymentId: string): Promise<void> {
    this.printed.push(`receipt:${paymentId}`);
    return Promise.resolve();
  }
}

const cbc: PickedService = { serviceId: 's-cbc', code: 'CBC', nameEn: 'Complete blood count', nameAr: 'صورة دم كاملة' };

describe('RegistrationPage (CP-06, WF-02)', () => {
  let backend: HttpTestingController;
  let printer: PrinterStub;
  let harness: RouterTestingHarness;
  let confirmAnswer = true;
  /** Permission keys the signed-in user lacks (UAT R3-02); everything else is granted. */
  let denied = new Set<string>();

  beforeEach(() => {
    printer = new PrinterStub();
    confirmAnswer = true;
    denied = new Set<string>();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthStore, useValue: { currentBranch: signal<string | null>('b-1'), hasPermission: (key: string) => !denied.has(key) } satisfies Pick<AuthStore, 'currentBranch' | 'hasPermission'> },
        { provide: DeviceIdentityService, useValue: { deviceId: () => 'device-test' } satisfies Pick<DeviceIdentityService, 'deviceId'> },
        { provide: PermissionsService, useValue: { can: (key: string) => !denied.has(key) } satisfies Pick<PermissionsService, 'can'> },
        { provide: RegistrationPrinter, useValue: printer },
        { provide: ConfirmService, useValue: { confirm: () => Promise.resolve(confirmAnswer), promptReason: () => Promise.resolve(null) } satisfies Pick<ConfirmService, 'confirm' | 'promptReason'> },
        provideRouter(
          [
            {
              path: 'registration',
              children: [
                { path: '', pathMatch: 'full', providers: [RegistrationFacade], component: RegistrationPage },
                { path: 'new', providers: [RegistrationFacade], component: RegistrationPage },
                { path: 'visits/:visitId', providers: [RegistrationFacade], component: RegistrationPage },
              ],
            },
          ],
          withComponentInputBinding(),
        ),
      ],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  /** Two rounds: a resource settles in a microtask after its flush, and the effects that read it run on the next tick. */
  async function tick(): Promise<void> {
    TestBed.tick();
    await settle();
    TestBed.tick();
    await settle();
  }

  function facade(): RegistrationFacade {
    return harness.routeDebugElement!.injector.get(RegistrationFacade);
  }

  /** Opens the page; the caller answers any request that is not a reference list before `flushReferenceLists`. */
  async function open(url: string, beforeLists: () => void = () => undefined): Promise<RegistrationPageInternals> {
    harness = await RouterTestingHarness.create();
    const page = (await harness.navigateByUrl(url, RegistrationPage)) as unknown as RegistrationPageInternals;
    await tick();
    beforeLists();
    flushReferenceLists(backend);
    await tick();
    return page;
  }

  /** A new visit with the patient chosen and one priced service. */
  async function typedVisit(page: RegistrationPageInternals, warnings: ReturnType<typeof checkResult>['warnings'] = []): Promise<void> {
    facade().setPatient(patient);
    page.onAdded([cbc]);
    await tick();
    backend.expectOne(REGISTRATION_API.check).flush(checkResult({ warnings }));
    await tick();
    expect(facade().blockers()).toEqual([]);
  }

  it('refuses to save while something is missing and sends nothing', async () => {
    const page = await open('/registration/new');
    expect(facade().blockers()).toEqual(['patient', 'lines']);

    await page.save();
    backend.expectNone(REGISTRATION_API.visits);
    expect(TestBed.inject(ToastService).toasts().some((toast) => toast.kind === 'error')).toBe(true);
    expect(page.successOpen()).toBe(false);
  });

  it('lists every open item in the checklist and the action bar, and marks the fields after a blocked save', async () => {
    const page = await open('/registration/new');
    harness.detectChanges();
    const host = harness.routeNativeElement!;

    // the cash contract is preselected: one of three done, patient and services open, nothing marked yet
    expect(host.querySelectorAll('app-reg-readiness .item.done').length).toBe(1);
    expect(host.querySelectorAll('app-reg-readiness .item.open').length).toBe(2);
    expect(host.textContent).toContain('registration.ready.count');
    expect(Array.from(host.querySelectorAll('.action-bar .chip.bad')).map((chip) => chip.textContent?.trim())).toEqual(['registration.validation.patient', 'registration.validation.lines']);
    expect(host.querySelector('#reg-patient.invalid')).toBeNull();

    await page.save();
    await tick();
    harness.detectChanges();
    expect(host.querySelector('#reg-patient.invalid')).not.toBeNull();
    // UAT A-08: the empty services list is marked too
    expect(host.querySelector('.services-card.invalid')).not.toBeNull();
    expect(host.querySelector('#reg-service-entry')?.getAttribute('aria-invalid')).toBe('true');
    expect(TestBed.inject(ToastService).toasts().some((toast) => toast.kind === 'error' && toast.text.includes('registration.validation.many'))).toBe(true);

    await typedVisit(page);
    harness.detectChanges();
    expect(host.querySelectorAll('app-reg-readiness .item.open').length).toBe(0);
    expect(host.querySelector('.action-bar .chip.bad')).toBeNull();
    expect(host.querySelector('#reg-patient.invalid')).toBeNull();
    expect(host.querySelector('.services-card.invalid')).toBeNull();
  });

  it('keeps the discount folded until it is opened or set', async () => {
    await open('/registration/new');
    harness.detectChanges();
    const host = harness.routeNativeElement!;
    expect(host.querySelector('#reg-discount')).toBeNull();

    host.querySelector<HTMLButtonElement>('.fold-head')?.click();
    await tick();
    harness.detectChanges();
    expect(host.querySelector('#reg-discount')).not.toBeNull();
    expect(host.querySelector('.fold-head')?.getAttribute('aria-expanded')).toBe('true');
  });

  it('preselects the patient of a patient-file link (/registration?patientId=) and the services of a price-query link', async () => {
    await open('/registration?patientId=p-1&services=s-cbc,s-glu&tierId=tier-ins', () => {
      backend.expectOne(`${PATIENTS_API.patients}/p-1`).flush({ ...patient, statedAge: null, ageRecordedOn: null, registeredOn: '2026-10-01', patientConditionId: null, nationality: null, email: null, governorateId: null, city: null, address: null, comments: null });
    });

    expect(facade().patient()?.number).toBe('PAT-2026-00001');
    expect(facade().lines().map((line) => line.serviceId)).toEqual(['s-cbc', 's-glu']);
    expect(facade().header().tierId).toBe('tier-ins');
    const check = backend.expectOne(REGISTRATION_API.check);
    expect(check.request.body).toEqual(expect.objectContaining({ patientId: 'p-1', contractTierId: 'tier-ins', lines: [{ serviceId: 's-cbc', reserved: false }, { serviceId: 's-glu', reserved: false }] }));
    check.flush(checkResult());
  });

  it('registers the visit, posts the captured payment (BR-26), prints labels and receipt per the parameters and shows the accession', async () => {
    const page = await open('/registration/new');
    await typedVisit(page);

    await Promise.all([
      page.save(),
      (async () => {
        await settle();
        const post = backend.expectOne(REGISTRATION_API.visits);
        const body = post.request.body as RegisterVisitRequest;
        expect(body).toEqual(expect.objectContaining({ patientId: 'p-1', payerId: 'payer-cash', contractTierId: 'tier-cash', lines: [{ serviceId: 's-cbc', reserved: false }], confirmFrequencyWarnings: false }));
        post.flush({ id: 'v-9', accession: 'ACC-2026-00009', tubeCount: 1, warnings: [] }, { status: 201, statusText: 'Created' });
        await settle();
        const pay = backend.expectOne(BILLING_API.payments);
        expect(pay.request.body).toEqual({ visitId: 'v-9', amount: 100, method: 'Cash', reference: null, treasuryId: 't-1', deviceId: 'device-test' });
        pay.flush({ id: 'pay-1', receiptNumber: 'RCT-2026-00001' }, { status: 201, statusText: 'Created' });
      })(),
    ]);

    expect(page.successOpen()).toBe(true);
    expect(page.success()?.visit.accession).toBe('ACC-2026-00009');
    expect(page.success()?.paymentProblem).toBeNull();
    expect(printer.printed).toEqual(['labels:v-9', 'receipt:pay-1']);
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('ACC-2026-00009');

    // UAT A-01: the spent draft is cleared at once, so "Open the visit" / "Payments" / closing the dialog cannot bring it
    // back as ready to save (the route-provided facade outlives the page); the dialog keeps its own copy
    expect(facade().patient()).toBeNull();
    expect(facade().lines()).toEqual([]);
    expect(facade().payment().reference).toBe('');
    expect(facade().blockers()).toContain('patient');
    expect(page.success()?.visit.accession).toBe('ACC-2026-00009');
    backend.verify();
  });

  it('asks to confirm a frequency alert (REG-201) and retries a REG-102 refusal with the confirmation', async () => {
    const page = await open('/registration/new');
    await typedVisit(page, [{ code: 'REG-201', serviceId: 's-cbc', serviceCode: 'CBC', serviceNameEn: null, serviceNameAr: null, detail: 'HB', detailAr: null, lastOrderedAtUtc: '2026-10-01T08:00:00Z', lastAccession: 'ACC-2026-00001', windowDays: 30 }]);

    await Promise.all([
      page.save(),
      (async () => {
        await settle();
        const first = backend.expectOne(REGISTRATION_API.visits);
        expect((first.request.body as RegisterVisitRequest).confirmFrequencyWarnings).toBe(true);
        first.flush({ type: 'about:blank', title: 'Confirm', status: 409, code: 'REG-102' }, { status: 409, statusText: 'Conflict' });
        await settle();
        const second = backend.expectOne(REGISTRATION_API.visits);
        expect((second.request.body as RegisterVisitRequest).confirmFrequencyWarnings).toBe(true);
        second.flush({ id: 'v-9', accession: 'ACC-2026-00009', tubeCount: 1, warnings: [] }, { status: 201, statusText: 'Created' });
        await settle();
        backend.expectOne(BILLING_API.payments).flush({ id: 'pay-1', receiptNumber: 'RCT-2026-00001' }, { status: 201, statusText: 'Created' });
      })(),
    ]);

    expect(page.successOpen()).toBe(true);
  });

  it('does not save when the clerk declines the frequency alert', async () => {
    confirmAnswer = false;
    const page = await open('/registration/new');
    await typedVisit(page, [{ code: 'REG-201', serviceId: 's-cbc', serviceCode: 'CBC', serviceNameEn: null, serviceNameAr: null, detail: 'HB', detailAr: null, lastOrderedAtUtc: null, lastAccession: null, windowDays: 30 }]);

    await page.save();
    backend.expectNone(REGISTRATION_API.visits);
    expect(page.successOpen()).toBe(false);
  });

  it('keeps the saved visit and reports a refused payment with its code (Q19: never retried silently)', async () => {
    const page = await open('/registration/new');
    await typedVisit(page);

    await Promise.all([
      page.save(),
      (async () => {
        await settle();
        backend.expectOne(REGISTRATION_API.visits).flush({ id: 'v-9', accession: 'ACC-2026-00009', tubeCount: 1, warnings: [] }, { status: 201, statusText: 'Created' });
        await settle();
        backend.expectOne(BILLING_API.payments).flush({ type: 'about:blank', title: 'Treasury', status: 403, code: 'BIL-020' }, { status: 403, statusText: 'Forbidden' });
      })(),
    ]);

    expect(page.successOpen()).toBe(true);
    expect(page.success()?.payment).toBeNull();
    expect(page.success()?.paymentProblem?.code).toBe('BIL-020');
    expect(printer.printed).toEqual(['labels:v-9']);
    harness.detectChanges();
    expect(harness.routeNativeElement?.querySelector('app-dialog [role="alert"]')).not.toBeNull();
  });

  it('shows a refusal of the registration itself and stays on the page', async () => {
    const page = await open('/registration/new');
    await typedVisit(page);

    await Promise.all([
      page.save(),
      (async () => {
        await settle();
        backend.expectOne(REGISTRATION_API.visits).flush({ type: 'about:blank', title: 'Merged', status: 422, code: 'REG-002' }, { status: 422, statusText: 'Unprocessable' });
      })(),
    ]);

    expect(page.successOpen()).toBe(false);
    expect(facade().register.problem()?.code).toBe('REG-002');
    backend.expectNone(BILLING_API.payments);
    expect(printer.printed).toEqual([]);
  });

  it('shows a saved visit read-only to a user without registration.edit: no Save, no New registration, every control disabled (UAT R3-02)', async () => {
    denied = new Set(['registration.edit', 'registration.create']);
    const page = await open('/registration/visits/v-1', () => backend.expectOne(`${REGISTRATION_API.visits}/v-1`).flush(visitDetail()));
    harness.detectChanges();

    const host = harness.routeNativeElement!;
    expect(host.textContent).toContain('registration.readOnly');
    const buttons = Array.from(host.querySelectorAll<HTMLButtonElement>('button')).filter((button) => !button.hidden);
    expect(buttons.some((button) => /saveChanges|newRegistration/.test(button.textContent ?? ''))).toBe(false);
    expect(host.querySelector<HTMLFieldSetElement>('fieldset.layout-fieldset')?.disabled).toBe(true);

    await page.save();
    await tick();
    backend.expectNone((request) => request.method !== 'GET');
  });

  it('opens a saved visit for editing: the saved lines are listed, a reserved line can be released, and no payment panel is shown', async () => {
    const reserved = { ...visitDetail().lines[0]!, id: 'l-2', serviceId: 's-glu', code: 'GLU', status: 'Reserved' as const, tubeId: null, sampleNumber: null };
    const page = await open('/registration/visits/v-1', () => backend.expectOne(`${REGISTRATION_API.visits}/v-1`).flush(visitDetail({ lines: [visitDetail().lines[0]!, reserved] })));
    harness.detectChanges();

    const host = harness.routeNativeElement!;
    expect(facade().saved()?.accession).toBe('ACC-2026-00001');
    expect(host.textContent).toContain('PAT-2026-00001');
    expect(host.querySelectorAll('tr.saved').length).toBe(2);
    expect(host.querySelectorAll('select option').length).toBeGreaterThan(0);
    expect(host.querySelector<HTMLSelectElement>('select')?.disabled).toBe(true);
    expect(host.querySelector('app-patient-picker')).toBeNull();
    expect(host.textContent).not.toContain('registration.payment.cardTitle');
    const releaseButton = host.querySelector<HTMLButtonElement>('tr.saved button.btn.sm');
    expect(releaseButton).not.toBeNull();
    expect(facade().canPay()).toBe(false);

    await Promise.all([
      page.release(reserved),
      (async () => {
        await settle();
        const post = backend.expectOne(`${REGISTRATION_API.visits}/v-1/lines/release`);
        expect(post.request.body).toEqual({ lineIds: ['l-2'] });
        post.flush(null, { status: 204, statusText: 'No Content' });
      })(),
    ]);
    await tick();
    backend.expectOne(`${REGISTRATION_API.visits}/v-1`).flush(visitDetail());
    expect(TestBed.inject(ToastService).toasts().some((toast) => toast.kind === 'success')).toBe(true);
  });

  it('names the session branch in the header of a new registration (validation 2026-10-11)', async () => {
    await open('/registration/new');
    harness.detectChanges();

    expect(harness.routeNativeElement?.querySelector('app-page-header')?.textContent).toContain('Main Branch');
  });

  it('shows a REG-109 refusal with the repeated tests, a REG-206 warning as a caution, and the tube count in the singular (validation O-3)', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input: string | URL | Request) =>
      new Response(
        JSON.stringify(
          String(input) === '/i18n/registration.en.json'
            ? {
                'error.REG-109': 'Every test of this service is already on the visit: {detail}.',
                'warning.REG-206': '{service} repeats tests already on this visit: {detail}.',
                'success.tubesOne': '1 tube was issued.',
                'success.tubes': '{count} tubes were issued.',
              }
            : {},
        ),
        { status: 200 },
      ),
    );
    await TestBed.inject(I18nService).load('registration');
    const page = await open('/registration/new');
    facade().setPatient(patient);
    page.onAdded([cbc, { serviceId: 's-crea', code: 'CREA', nameEn: 'Creatinine', nameAr: 'كرياتينين' }]);
    await tick();
    backend.expectOne(REGISTRATION_API.check).flush(
      checkResult({
        issues: [{ code: 'REG-109', serviceId: 's-crea', serviceCode: 'CREA', message: "Every test of 'CREA' is already on this visit: CREA (PKG › RFT).", detail: 'CREA (PKG › RFT)' }],
        warnings: [{ code: 'REG-206', serviceId: 's-cbc', serviceCode: 'CBC', serviceNameEn: null, serviceNameAr: null, detail: 'HB (FBC)', detailAr: null, lastOrderedAtUtc: null, lastAccession: null, windowDays: null }],
      }),
    );
    await tick();
    harness.detectChanges();

    const text = harness.routeNativeElement?.textContent ?? '';
    expect(text).toContain('CREA: Every test of this service is already on the visit: CREA (PKG › RFT).');
    expect(text).toContain('CBC repeats tests already on this visit: HB (FBC).');
    expect(harness.routeNativeElement?.querySelector('li.warning.warn')?.textContent).toContain('HB (FBC)');

    const internals = page as unknown as { tubesText(count: number): string };
    expect(internals.tubesText(1)).toBe('1 tube was issued.');
    expect(internals.tubesText(3)).toBe('3 tubes were issued.');
    vi.restoreAllMocks();
  });
});
