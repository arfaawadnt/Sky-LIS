import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { BILLING_API, type PaymentMethodOption, type RecordPaymentRequest } from '../../../core/api/billing.contracts';
import { REGISTRATION_API } from '../../../core/api/registration.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { checkResult, flushReferenceLists, patient, settle } from '../testing/registration.fixtures';
import { RegistrationFacade } from './registration.facade';

/** The laboratory's methods as the options endpoint answers them: CASH needs no reference, CARD does, WALLET is a card without the rule. */
const options: readonly PaymentMethodOption[] = [
  { id: 'pm-cash', code: 'CASH', nameEn: 'Cash', nameAr: 'نقدي', kind: 'Cash', requiresReference: false },
  { id: 'pm-card', code: 'CARD', nameEn: 'Card', nameAr: 'بطاقة', kind: 'Card', requiresReference: true },
  { id: 'pm-wallet', code: 'WALLET', nameEn: 'Wallet', nameAr: 'محفظة', kind: 'Card', requiresReference: false },
];

/**
 * The payment panel of CP-06 with the laboratory's payment methods (CP-30b, change request 2026-10-19 P-C10): the
 * methods come from Billing's options endpoint, the reference is demanded only when the chosen one requires it, and
 * the payment posted after the save names the kind and the code.
 */
describe('RegistrationFacade payment methods (CP-30b)', () => {
  let facade: RegistrationFacade;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthStore, useValue: { currentBranch: signal<string | null>('b-1'), hasPermission: () => true } satisfies Pick<AuthStore, 'currentBranch' | 'hasPermission'> },
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

  /** A new visit with one service due: the methods answer `methods` (or 403 when null), the other lists their fixtures. */
  async function startWithDue(methods: readonly PaymentMethodOption[] | null): Promise<void> {
    await runEffects();
    for (const request of backend.match((candidate) => candidate.url === BILLING_API.paymentMethodOptions)) {
      if (methods === null) {
        request.flush({ type: 'about:blank', title: 'Forbidden', status: 403, code: 'AUTH-403' }, { status: 403, statusText: 'Forbidden' });
      } else {
        request.flush(methods);
      }
    }

    flushReferenceLists(backend);
    await runEffects();
    facade.defaultTier();
    facade.setPatient(patient);
    facade.addServices([{ serviceId: 's-cbc', code: 'CBC', nameEn: '', nameAr: '' }]);
    await runEffects();
    backend.expectOne(REGISTRATION_API.check).flush(checkResult());
    await runEffects();
  }

  it("offers the laboratory's methods, selects the first one and asks for the reference only when the chosen one requires it", async () => {
    await startWithDue(options);

    expect(facade.methodOptions().map((option) => option.code)).toEqual(['CASH', 'CARD', 'WALLET']);
    expect(facade.selectedMethod()?.code).toBe('CASH');
    expect(facade.referenceRequired()).toBe(false);
    expect(facade.blockers()).toEqual([]);

    facade.selectMethod(options[1]!);
    expect(facade.referenceRequired()).toBe(true);
    expect(facade.blockers()).toEqual(['paymentReference']);

    facade.selectMethod(options[2]!);
    expect(facade.referenceRequired()).toBe(false);
    expect(facade.blockers()).toEqual([]);

    // a code the list no longer offers (deactivated since) falls back to the first method
    facade.methodCode.set('GONE');
    expect(facade.selectedMethod()?.code).toBe('CASH');
    backend.verify();
  });

  it('posts the payment with the kind and the code of the chosen method, and the reference only when required (BR-26)', async () => {
    await startWithDue(options);
    facade.selectMethod(options[2]!);
    facade.patchPayment({ reference: 'stale text' });

    const paying = facade.pay.execute('v-1');
    await settle();
    const post = backend.expectOne(BILLING_API.payments);
    expect(post.request.body as RecordPaymentRequest).toEqual({ visitId: 'v-1', amount: 100, method: 'Card', methodCode: 'WALLET', reference: null });
    post.flush({ id: 'pay-1', receiptNumber: 'RCT-2026-00001' }, { status: 201, statusText: 'Created' });
    await paying;

    facade.selectMethod(options[1]!);
    facade.patchPayment({ reference: ' SLIP-7 ' });
    const payingCard = facade.pay.execute('v-1');
    await settle();
    const card = backend.expectOne(BILLING_API.payments);
    expect((card.request.body as RecordPaymentRequest).methodCode).toBe('CARD');
    expect((card.request.body as RecordPaymentRequest).reference).toBe('SLIP-7');
    card.flush({ id: 'pay-2', receiptNumber: 'RCT-2026-00002' }, { status: 201, statusText: 'Created' });
    await payingCard;
    backend.verify();
  });

  it('falls back to the four kinds with the legacy rule when the methods cannot be read (no definitions, or refused)', async () => {
    await startWithDue(null);

    expect(facade.methodOptions()).toEqual([]);
    expect(facade.selectedMethod()).toBeNull();
    expect(facade.referenceRequired()).toBe(false);

    facade.patchPayment({ method: 'Card' });
    expect(facade.referenceRequired()).toBe(true);
    expect(facade.blockers()).toEqual(['paymentReference']);
    facade.patchPayment({ reference: 'SLIP-1' });

    const paying = facade.pay.execute('v-1');
    await settle();
    const post = backend.expectOne(BILLING_API.payments);
    expect(post.request.body as RecordPaymentRequest).toEqual({ visitId: 'v-1', amount: 100, method: 'Card', reference: 'SLIP-1' });
    post.flush({ id: 'pay-1', receiptNumber: 'RCT-2026-00001' }, { status: 201, statusText: 'Created' });
    await paying;
    backend.verify();
  });

  it('re-reads the methods when the page opens again (refresh) and clears the chosen code with the rest of the panel (reset)', async () => {
    await startWithDue(options);
    facade.selectMethod(options[2]!);

    facade.reset();
    expect(facade.methodCode()).toBe('');
    expect(facade.selectedMethod()?.code).toBe('CASH');

    facade.refresh();
    await runEffects();
    backend.expectOne(BILLING_API.paymentMethodOptions).flush([options[1]!]);
    backend.match(() => true).forEach((request) => request.flush(null, { status: 404, statusText: 'Not Found' }));
    await runEffects();
    expect(facade.selectedMethod()?.code).toBe('CARD');
  });
});
