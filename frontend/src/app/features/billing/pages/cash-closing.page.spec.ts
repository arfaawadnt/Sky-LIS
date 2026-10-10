import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { FormControl, FormGroup } from '@angular/forms';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { BILLING_API, type CashClosingListItem, type CashClosingPreview, type Paged } from '../../../core/api/billing.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { BillingPrinter } from '../state/billing-printer';
import { CashClosingFacade } from '../state/cash-closing.facade';
import { preview } from '../testing/billing.fixtures';
import { CashClosingPage } from './cash-closing.page';

interface CashClosingInternals {
  readonly scopeForm: FormGroup<{ treasuryId: FormControl<string>; cashierUserId: FormControl<string>; from: FormControl<string>; to: FormControl<string> }>;
  readonly countForm: FormGroup<{ countedCash: FormControl<number | null>; note: FormControl<string> }>;
  readonly difference: () => number | null;
  readonly noteMissing: () => boolean;
  preview(): void;
  close(preview: CashClosingPreview): Promise<void>;
}

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function page<T>(items: T[]): Paged<T> {
  return { items, page: 1, pageSize: 20, totalCount: items.length, totalPages: 1 };
}

describe('CashClosingPage (CP-30)', () => {
  let backend: HttpTestingController;
  const printed: string[] = [];
  let confirmations = 0;

  beforeEach(() => {
    printed.length = 0;
    confirmations = 0;
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
            confirm: () => {
              confirmations++;
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
    backend.expectOne(BILLING_API.treasuries).flush([{ id: 't-1', code: 'MAIN', nameEn: 'Main drawer', nameAr: 'الخزينة', branchId: 'b-1', isActive: true }]);
    backend.expectOne((request) => request.url === BILLING_API.cashClosings).flush(page<CashClosingListItem>([]));

    component.scopeForm.setValue({ treasuryId: 't-1', cashierUserId: '', from: '2026-10-09T00:00', to: '2026-10-09T16:00' });
    component.preview();
    TestBed.tick();
    await settle();
    backend.expectOne((request) => request.url === `${BILLING_API.cashClosings}/cashiers`).flush([]);
    const request = backend.expectOne((candidate) => candidate.url === `${BILLING_API.cashClosings}/preview`);
    expect(request.request.params.has('cashierUserId')).toBe(false);
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
          treasuryId: 't-1',
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
});
