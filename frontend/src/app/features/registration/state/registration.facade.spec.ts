import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { BILLING_API } from '../../../core/api/billing.contracts';
import { REGISTRATION_API, type RegisterVisitRequest } from '../../../core/api/registration.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { DeviceIdentityService } from '../../../core/device/device-identity.service';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { checkResult, flushReferenceLists, insurerTier, line, patient, settings, settle, visitDetail } from '../testing/registration.fixtures';
import { RegistrationFacade } from './registration.facade';

describe('RegistrationFacade (CP-06, WF-02)', () => {
  let facade: RegistrationFacade;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthStore, useValue: { currentBranch: signal<string | null>('b-1'), hasPermission: () => true } satisfies Pick<AuthStore, 'currentBranch' | 'hasPermission'> },
        { provide: DeviceIdentityService, useValue: { deviceId: () => 'device-test' } satisfies Pick<DeviceIdentityService, 'deviceId'> },
        RegistrationFacade,
      ],
    });
    facade = TestBed.inject(RegistrationFacade);
    backend = TestBed.inject(HttpTestingController);
  });

  async function runEffects(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  /** Starts a new visit: the reference lists answer, the cash default tier is chosen, the patient is set. */
  async function startNewVisit(): Promise<void> {
    await runEffects();
    flushReferenceLists(backend);
    await runEffects();
    facade.defaultTier();
    facade.defaultTreasury();
    facade.setPatient(patient);
    await runEffects();
  }

  it('reads the parameters and reference lists on start and chooses the cash default tier and the first treasury', async () => {
    await startNewVisit();

    expect(facade.settings.value()?.discountCapPercent).toBe(20);
    expect(facade.header().tierId).toBe('tier-cash');
    expect(facade.contract()).toEqual({ payerId: 'payer-cash', tierId: 'tier-cash' });
    expect(facade.payment().treasuryId).toBe('t-1');
    expect(facade.canPay()).toBe(true);
    backend.expectNone(REGISTRATION_API.check);
    backend.verify();
  });

  it('runs the server dry run whenever services are typed, with the contract, patient and desk offset', async () => {
    await startNewVisit();
    expect(facade.blockers()).toEqual(['lines']);

    expect(facade.addServices([{ serviceId: 's-cbc', code: 'CBC', nameEn: 'Complete blood count', nameAr: 'صورة دم كاملة' }])).toEqual([]);
    await runEffects();

    const check = backend.expectOne(REGISTRATION_API.check);
    expect(check.request.method).toBe('POST');
    expect(check.request.body).toEqual({
      visitId: null,
      patientId: 'p-1',
      payerId: 'payer-cash',
      contractTierId: 'tier-cash',
      diagnosisId: null,
      lines: [{ serviceId: 's-cbc', reserved: false }],
      utcOffsetMinutes: -new Date().getTimezoneOffset(),
    });
    expect(facade.blockers()).toEqual(['checking']);
    check.flush(checkResult());
    await runEffects();

    expect(facade.plannedLines().map((item) => item.code)).toEqual(['CBC']);
    expect(facade.totals()).toEqual({ list: 100, payer: 0, patient: 100, discount: 0, due: 100, paid: 0, remaining: 100 });
    expect(facade.paymentAmount()).toBe(100);
    expect(facade.blockers()).toEqual([]);
    backend.verify();
  });

  it('refuses a duplicate typed service and re-runs the dry run when a line is reserved (BR-05)', async () => {
    await startNewVisit();
    facade.addServices([{ serviceId: 's-cbc', code: 'CBC', nameEn: 'Complete blood count', nameAr: '' }]);
    await runEffects();
    backend.expectOne(REGISTRATION_API.check).flush(checkResult());
    await runEffects();

    expect(facade.addServices([{ serviceId: 's-cbc', code: 'CBC', nameEn: 'Complete blood count', nameAr: '' }])).toEqual(['CBC']);
    facade.setReserved('s-cbc', true);
    await runEffects();
    const check = backend.expectOne(REGISTRATION_API.check);
    expect(check.request.body).toEqual(expect.objectContaining({ lines: [{ serviceId: 's-cbc', reserved: true }] }));
    check.flush(checkResult({ lines: [line({ status: 'Reserved', tubeKey: null })], tubes: [] }));
    await runEffects();
    expect(facade.plannedLines()[0]?.status).toBe('Reserved');
    backend.verify();
  });

  it('allocates the discount to the patient share only, needs a reason and stays inside the cap (REG-017, REG-060)', async () => {
    await startNewVisit();
    facade.addServices([{ serviceId: 's-cbc', code: 'CBC', nameEn: '', nameAr: '' }]);
    await runEffects();
    backend.expectOne(REGISTRATION_API.check).flush(checkResult({ lines: [line({ listPrice: 300, patientShare: 200, payerShare: 100 })] }));
    await runEffects();

    facade.patchDiscount({ kind: 'Percent', value: 10 });
    expect(facade.discountTotal()).toBe(20);
    expect(facade.totals().due).toBe(180);
    expect(facade.totals().payer).toBe(100);
    expect(facade.blockers()).toEqual(['discountReason']);

    facade.patchDiscount({ reasonId: 'r-disc', value: 30 });
    expect(facade.blockers()).toEqual(['discountCap']);

    facade.patchDiscount({ kind: 'Amount', value: 25 });
    expect(facade.discountTotal()).toBe(25);
    expect(facade.blockers()).toEqual([]);
    expect(facade.registerRequest(false).discount).toEqual({ kind: 'Amount', value: 25, reasonId: 'r-disc' });
  });

  it('blocks the save when the payment needs a reference, a treasury or exceeds the due amount', async () => {
    await startNewVisit();
    facade.addServices([{ serviceId: 's-cbc', code: 'CBC', nameEn: '', nameAr: '' }]);
    await runEffects();
    backend.expectOne(REGISTRATION_API.check).flush(checkResult());
    await runEffects();

    facade.patchPayment({ method: 'Card' });
    expect(facade.blockers()).toEqual(['paymentReference']);
    facade.patchPayment({ reference: 'SLIP-1', amount: 150 });
    expect(facade.blockers()).toEqual(['paymentAmount']);
    facade.patchPayment({ amount: 40, treasuryId: '' });
    expect(facade.blockers()).toEqual(['paymentTreasury']);
    facade.patchPayment({ treasuryId: 't-1' });
    expect(facade.blockers()).toEqual([]);
    expect(facade.paymentAmount()).toBe(40);
  });

  it('requires the doctor when the tenant parameter says so and the patient before anything else', async () => {
    await runEffects();
    flushReferenceLists(backend, { settings: settings({ mandatoryDoctor: true }) });
    await runEffects();
    facade.defaultTier();

    expect(facade.blockers()).toEqual(['patient', 'lines', 'doctor']);
    facade.patchHeader({ doctorId: 'd-1' });
    facade.setPatient(patient);
    expect(facade.blockers()).toEqual(['lines']);
  });

  it('registers the visit with the typed data and the frequency confirmation, then posts the payment through Billing (BR-26)', async () => {
    await startNewVisit();
    facade.addServices([{ serviceId: 's-cbc', code: 'CBC', nameEn: '', nameAr: '' }]);
    facade.patchHeader({ urgent: true, treatmentNo: ' T-9 ' });
    await runEffects();
    backend.expectOne(REGISTRATION_API.check).flush(checkResult({ preparations: [{ preparationId: 'prep-fast', code: 'FAST', questionEn: 'Fasting?', questionAr: 'صائم؟', answerType: 'YesNo', options: [], gender: 'Any', serviceIds: ['s-cbc'] }] }));
    await runEffects();
    facade.setAnswer('prep-fast', 'Yes');
    facade.setAnswer('prep-other', 'ignored');

    const pending = facade.register.execute(true);
    await settle();
    const post = backend.expectOne(REGISTRATION_API.visits);
    const body = post.request.body as RegisterVisitRequest;
    expect(body).toEqual(
      expect.objectContaining({
        patientId: 'p-1',
        payerId: 'payer-cash',
        contractTierId: 'tier-cash',
        treatmentNo: 'T-9',
        referringDoctorId: null,
        bookingId: null,
        urgent: true,
        lines: [{ serviceId: 's-cbc', reserved: false }],
        preparationAnswers: [{ preparationId: 'prep-fast', answer: 'Yes' }],
        discount: null,
        confirmFrequencyWarnings: true,
      }),
    );
    post.flush({ id: 'v-9', accession: 'ACC-2026-00009', tubeCount: 1, warnings: [] }, { status: 201, statusText: 'Created' });
    expect(await pending).toEqual(expect.objectContaining({ accession: 'ACC-2026-00009' }));

    const paying = facade.pay.execute('v-9');
    await settle();
    const pay = backend.expectOne(BILLING_API.payments);
    expect(pay.request.body).toEqual({ visitId: 'v-9', amount: 100, method: 'Cash', reference: null, treasuryId: 't-1', deviceId: 'device-test' });
    pay.flush({ id: 'pay-1', receiptNumber: 'RCT-2026-00001' }, { status: 201, statusText: 'Created' });
    expect(await paying).toEqual({ id: 'pay-1', receiptNumber: 'RCT-2026-00001' });
    backend.verify();
  });

  it('keeps the refusal code of a registration as its problem (REG-102 asks for confirmation)', async () => {
    await startNewVisit();
    facade.addServices([{ serviceId: 's-cbc', code: 'CBC', nameEn: '', nameAr: '' }]);
    await runEffects();
    backend.expectOne(REGISTRATION_API.check).flush(checkResult({ warnings: [{ code: 'REG-201', serviceId: 's-cbc', serviceCode: 'CBC', serviceNameEn: null, serviceNameAr: null, detail: 'HB', detailAr: null, lastOrderedAtUtc: '2026-10-01T08:00:00Z', lastAccession: 'ACC-2026-00001', windowDays: 30 }] }));
    await runEffects();
    expect(facade.frequencyAlerts().length).toBe(1);

    const pending = facade.register.execute(false);
    await settle();
    backend.expectOne(REGISTRATION_API.visits).flush({ type: 'about:blank', title: 'Confirm', status: 409, code: 'REG-102' }, { status: 409, statusText: 'Conflict' });
    expect(await pending).toBeNull();
    expect(facade.register.problem()?.code).toBe('REG-102');
  });

  it('clears the visit but keeps the contract and the treasury for the next patient', async () => {
    await startNewVisit();
    facade.addServices([{ serviceId: 's-cbc', code: 'CBC', nameEn: '', nameAr: '' }]);
    facade.patchDiscount({ value: 5, reasonId: 'r' });
    facade.patchPayment({ method: 'Card', reference: 'X' });
    await runEffects();
    backend.match(() => true).forEach((call) => call.flush(checkResult()));

    facade.reset(true);
    expect(facade.patient()).toBeNull();
    expect(facade.lines()).toEqual([]);
    expect(facade.discount().value).toBeNull();
    expect(facade.header().tierId).toBe('tier-cash');
    expect(facade.payment()).toEqual({ method: 'Cash', amount: null, reference: '', treasuryId: 't-1' });
  });

  it('opens a saved visit in edit mode: the visit loads, the contract is the saved one, and only added services, details and discount are sent', async () => {
    facade.open('v-1');
    await runEffects();
    backend.expectOne(`${REGISTRATION_API.visits}/v-1`).flush(visitDetail({ treatmentNo: 'T-1', discountKind: 'Percent', discountValue: 10, discountReasonId: 'r-disc', discountTotal: 10, patientShareTotal: 90, lines: [{ ...visitDetail().lines[0]!, patientShare: 90, discount: 10 }] }));
    flushReferenceLists(backend, { tiers: [insurerTier] });
    await runEffects();

    expect(facade.isEdit()).toBe(true);
    expect(facade.saved()?.accession).toBe('ACC-2026-00001');
    facade.adopt(facade.saved()!);
    expect(facade.contract()).toEqual({ payerId: 'payer-cash', tierId: 'tier-cash' });
    expect(facade.canPay()).toBe(false);
    expect(facade.discountBase()).toBe(100);
    expect(facade.discountTotal()).toBe(10);
    expect(facade.totals()).toEqual(expect.objectContaining({ patient: 100, discount: 10, due: 90, paid: 100, remaining: -10 }));
    expect(facade.detailsChanged()).toBe(false);
    expect(facade.discountChanged()).toBe(false);
    backend.expectNone((request) => request.url === BILLING_API.myTreasuries);

    expect(facade.addServices([{ serviceId: 's-cbc', code: 'CBC', nameEn: '', nameAr: '' }])).toEqual(['CBC']);
    facade.addServices([{ serviceId: 's-glu', code: 'GLU', nameEn: 'Glucose', nameAr: '' }]);
    await runEffects();
    const check = backend.expectOne(REGISTRATION_API.check);
    expect(check.request.body).toEqual(expect.objectContaining({ visitId: 'v-1', patientId: 'p-1', diagnosisId: null, lines: [{ serviceId: 's-glu', reserved: false }] }));
    check.flush(checkResult({ lines: [line({ serviceId: 's-glu', code: 'GLU', listPrice: 50, patientShare: 50 })] }));
    await runEffects();
    expect(facade.discountBase()).toBe(150);

    facade.patchHeader({ treatmentNo: 'T-2' });
    expect(facade.detailsChanged()).toBe(true);
    facade.patchDiscount({ value: 15 });
    expect(facade.discountChanged()).toBe(true);

    const adding = facade.addLines.execute({ visitId: 'v-1', confirm: false });
    await settle();
    const post = backend.expectOne(`${REGISTRATION_API.visits}/v-1/lines`);
    expect(post.request.body).toEqual({ lines: [{ serviceId: 's-glu', reserved: false }], preparationAnswers: null, confirmFrequencyWarnings: false });
    post.flush({ addedLines: 1, newTubes: 1, warnings: [] });
    expect(await adding).toEqual({ addedLines: 1, newTubes: 1, warnings: [] });

    const updating = facade.updateDetails.execute('v-1');
    await settle();
    const put = backend.expectOne(`${REGISTRATION_API.visits}/v-1/details`);
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ treatmentNo: 'T-2', referringDoctorId: null, insuranceId: null, urgent: false, biohazard: false, collectedOutside: false });
    put.flush(null, { status: 204, statusText: 'No Content' });
    expect(await updating).toBeUndefined();

    const discounting = facade.setDiscount.execute('v-1');
    await settle();
    const discount = backend.expectOne(`${REGISTRATION_API.visits}/v-1/discount`);
    expect(discount.request.body).toEqual({ discount: { kind: 'Percent', value: 15, reasonId: 'r-disc' }, reason: null });
    discount.flush(null, { status: 204, statusText: 'No Content' });
    expect(await discounting).toBeUndefined();
  });

  it('releases a reserved line and reloads the visit', async () => {
    facade.open('v-1');
    await runEffects();
    backend.expectOne(`${REGISTRATION_API.visits}/v-1`).flush(visitDetail());
    flushReferenceLists(backend);
    await runEffects();

    const releasing = facade.release.execute({ visitId: 'v-1', lineIds: ['l-1'] });
    await settle();
    const post = backend.expectOne(`${REGISTRATION_API.visits}/v-1/lines/release`);
    expect(post.request.body).toEqual({ lineIds: ['l-1'] });
    post.flush(null, { status: 204, statusText: 'No Content' });
    await releasing;
    await runEffects();
    backend.expectOne(`${REGISTRATION_API.visits}/v-1`).flush(visitDetail());
    backend.verify();
  });
});
