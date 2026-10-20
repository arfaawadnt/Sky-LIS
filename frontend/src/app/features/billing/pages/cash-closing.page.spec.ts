import billingAr from '../../../../../public/i18n/billing.ar.json';
import billingEn from '../../../../../public/i18n/billing.en.json';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { FormControl, FormGroup } from '@angular/forms';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { BILLING_API, type CashClosingListItem, type CashClosingPreview, type Paged } from '../../../core/api/billing.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { type ConfirmOptions, ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { BillingPrinter } from '../state/billing-printer';
import { CashClosingFacade } from '../state/cash-closing.facade';
import { preview } from '../testing/billing.fixtures';
import { CashClosingPage } from './cash-closing.page';

interface CashClosingInternals {
  readonly scopeForm: FormGroup<{ cashierUserId: FormControl<string>; from: FormControl<string>; to: FormControl<string> }>;
  readonly countForm: FormGroup<{ countedCash: FormControl<number | null>; note: FormControl<string> }>;
  readonly difference: () => number | null;
  readonly noteMissing: () => boolean;
  preview(): void;
  close(preview: CashClosingPreview): Promise<void>;
}

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const closed: CashClosingListItem = {
  id: 'c-1',
  number: 'CLS-2026-00001',
  cashierUserId: 'u-1',
  cashierName: 'Cashier One',
  periodFromUtc: '2026-10-08T00:00:00Z',
  periodToUtc: '2026-10-08T16:00:00Z',
  totalPayments: 100,
  totalRefunds: 0,
  expectedCash: 100,
  countedCash: 100,
  difference: 0,
  closedByUserId: 'u-9',
  closedByName: 'Manager',
  closedAtUtc: '2026-10-08T16:05:00Z',
};

function page<T>(items: T[]): Paged<T> {
  return { items, page: 1, pageSize: 20, totalCount: items.length, totalPages: 1 };
}

describe('CashClosingPage (CP-30, LIS-CR-004)', () => {
  let backend: HttpTestingController;
  const printed: string[] = [];
  let confirmations = 0;
  const messages: string[] = [];

  beforeEach(() => {
    printed.length = 0;
    confirmations = 0;
    messages.length = 0;
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
        { provide: BillingPrinter, useValue: { closingSheet: (id: string) => Promise.resolve(void printed.push(id)) } satisfies Pick<BillingPrinter, 'closingSheet'> },
        {
          provide: ConfirmService,
          useValue: {
            confirm: (options: ConfirmOptions) => {
              confirmations++;
              messages.push(options.message);
              return Promise.resolve(true);
            },
          } satisfies Pick<ConfirmService, 'confirm'>,
        },
        provideRouter([{ path: 'billing/cash-closing', providers: [CashClosingFacade], component: CashClosingPage }]),
      ],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  it('previews the drawer, needs a note for a difference (BIL-034), closes after confirmation and prints the sheet', async () => {
    const harness = await RouterTestingHarness.create();
    const component = (await harness.navigateByUrl('/billing/cash-closing', CashClosingPage)) as unknown as CashClosingInternals;
    TestBed.tick();
    await settle();
    backend.expectOne((request) => request.url === `${BILLING_API.cashClosings}/cashiers`).flush([{ userId: 'u-1', name: 'Cashier One' }]);
    backend.expectOne((request) => request.url === BILLING_API.cashClosings).flush(page<CashClosingListItem>([]));

    component.scopeForm.setValue({ cashierUserId: '', from: '2026-10-09T00:00', to: '2026-10-09T16:00' });
    component.preview();
    TestBed.tick();
    await settle();
    backend.expectOne((request) => request.url === `${BILLING_API.cashClosings}/cashiers`).flush([]);
    const request = backend.expectOne((candidate) => candidate.url === `${BILLING_API.cashClosings}/preview`);
    expect(request.request.params.has('cashierUserId')).toBe(false);
    expect(request.request.params.has('treasuryId')).toBe(false);
    backend.expectNone('/api/v1/billing/treasuries');
    request.flush(preview());
    TestBed.tick();
    await settle();

    component.countForm.controls.countedCash.setValue(440);
    expect(component.difference()).toBe(-10);
    expect(component.noteMissing()).toBe(true);
    await component.close(preview());
    expect(confirmations).toBe(0);
    backend.expectNone((candidate) => candidate.method === 'POST');

    component.countForm.controls.note.setValue('10 short, cashier informed');
    TestBed.tick();
    expect(component.noteMissing()).toBe(false);
    await Promise.all([
      component.close(preview()),
      (async () => {
        await settle();
        const post = backend.expectOne((candidate) => candidate.method === 'POST' && candidate.url === BILLING_API.cashClosings);
        expect(post.request.body).toEqual({
          cashierUserId: null,
          periodFromUtc: preview().periodFromUtc,
          periodToUtc: preview().periodToUtc,
          countedCash: 440,
          note: '10 short, cashier informed',
        });
        post.flush({ id: 'c-1', number: 'CLS-2026-00001' }, { status: 201, statusText: 'Created' });
      })(),
    ]);

    expect(confirmations).toBe(1);
    expect(printed).toEqual(['c-1']);
    TestBed.tick();
    await settle();
    backend.expectOne((candidate) => candidate.url === `${BILLING_API.cashClosings}/preview`).flush(preview({ overlapsClosedPeriod: true }));
    backend.expectOne((candidate) => candidate.url === BILLING_API.cashClosings && candidate.method === 'GET').flush(page<CashClosingListItem>([]));
  });

  it('closes the drawer of one cashier of the session branch: the cashier is the scope, the confirmation names them, no treasury anywhere (LIS-CR-004)', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input: string | URL | Request) => new Response(JSON.stringify(String(input) === '/i18n/billing.en.json' ? billingEn : {}), { status: 200 }));
    await TestBed.inject(I18nService).load('billing');
    vi.restoreAllMocks();
    const harness = await RouterTestingHarness.create();
    const component = (await harness.navigateByUrl('/billing/cash-closing', CashClosingPage)) as unknown as CashClosingInternals;
    TestBed.tick();
    await settle();
    const cashiers = backend.expectOne((request) => request.url === `${BILLING_API.cashClosings}/cashiers`);
    expect(cashiers.request.params.has('from')).toBe(true);
    expect(cashiers.request.params.has('treasuryId')).toBe(false);
    cashiers.flush([{ userId: 'u-1', name: 'Cashier One' }]);
    backend.expectOne((request) => request.url === BILLING_API.cashClosings).flush(page<CashClosingListItem>([closed]));
    TestBed.tick();
    await settle();
    harness.detectChanges();
    const root = harness.routeNativeElement as HTMLElement;
    expect(root.querySelector('select[formcontrolname=treasuryId]')).toBeNull();
    expect(root.textContent).not.toContain('billing.field.treasury');
    expect(root.textContent).not.toContain('billing.column.treasury');
    expect(root.textContent).toContain('Cashier One');

    component.scopeForm.setValue({ cashierUserId: 'u-1', from: '2026-10-09T00:00', to: '2026-10-09T16:00' });
    component.preview();
    TestBed.tick();
    await settle();
    backend.expectOne((request) => request.url === `${BILLING_API.cashClosings}/cashiers`).flush([{ userId: 'u-1', name: 'Cashier One' }]);
    const request = backend.expectOne((candidate) => candidate.url === `${BILLING_API.cashClosings}/preview`);
    expect(request.request.params.get('cashierUserId')).toBe('u-1');
    const mine = preview({ cashierUserId: 'u-1', cashierName: 'Cashier One' });
    request.flush(mine);
    TestBed.tick();
    await settle();

    component.countForm.controls.countedCash.setValue(450);
    await Promise.all([
      component.close(mine),
      (async () => {
        await settle();
        const post = backend.expectOne((candidate) => candidate.method === 'POST' && candidate.url === BILLING_API.cashClosings);
        expect(post.request.body).toEqual({ cashierUserId: 'u-1', periodFromUtc: mine.periodFromUtc, periodToUtc: mine.periodToUtc, countedCash: 450, note: null });
        post.flush({ id: 'c-2', number: 'CLS-2026-00002' }, { status: 201, statusText: 'Created' });
      })(),
    ]);
    expect(messages[0]).toBe('Close the drawer of Cashier One with expected cash 450.00 and counted cash 450.00? The period is frozen afterwards.');
    expect(printed).toEqual(['c-2']);
    TestBed.tick();
    await settle();
    backend.match(() => true).forEach((pending) => pending.flush(pending.request.url.endsWith('/preview') ? mine : page<CashClosingListItem>([])));
  });

  it('F-09 / F-11: on an Arabic screen the counts read Arabic-Indic digits in their counted form, the amounts too', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input: string | URL | Request) => new Response(JSON.stringify(String(input) === '/i18n/billing.ar.json' ? billingAr : {}), { status: 200 }));
    const i18n = TestBed.inject(I18nService);
    i18n.setLanguage('ar');
    await i18n.load('billing');
    try {
      const harness = await RouterTestingHarness.create();
      const component = (await harness.navigateByUrl('/billing/cash-closing', CashClosingPage)) as unknown as CashClosingInternals;
      TestBed.tick();
      await settle();
      backend.expectOne((request) => request.url === `${BILLING_API.cashClosings}/cashiers`).flush([{ userId: 'u-1', name: 'Cashier One' }]);
      backend.expectOne((request) => request.url === BILLING_API.cashClosings).flush(page<CashClosingListItem>([]));
      component.scopeForm.setValue({ cashierUserId: '', from: '2026-10-09T00:00', to: '2026-10-09T16:00' });
      component.preview();
      TestBed.tick();
      await settle();
      backend.expectOne((request) => request.url === `${BILLING_API.cashClosings}/cashiers`).flush([]);
      const base = preview();
      backend.expectOne((candidate) => candidate.url === `${BILLING_API.cashClosings}/preview`).flush({ ...base, totals: { ...base.totals, paymentCount: 7, visitCount: 2 } });
      TestBed.tick();
      await settle();
      harness.detectChanges();

      const text = harness.routeNativeElement?.textContent ?? '';
      expect(text).toContain('٧ إيصالات');
      expect(text).toContain('زيارتان');
      expect(text).not.toMatch(/\d+\.\d{2}/);   // no Latin amount on the Arabic screen
    } finally {
      i18n.setLanguage('en');
      vi.restoreAllMocks();
    }
  });
});
