import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { FormGroup } from '@angular/forms';
import { provideRouter } from '@angular/router';

import { PLATFORM_API } from '../../../core/api/platform.contracts';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { WalletsFacade } from '../state/wallets.facade';
import { SubscriptionsPage } from './subscriptions.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

interface PlanDrawer {
  openPlan(plan: null): void;
  savePlan(): Promise<void>;
  planForm(): FormGroup | null;
}

/**
 * Validation 2026-10-11 C-05: a plan's monthly fee of 14.145 was saved as 14.15 without a word. The plan form now refuses more
 * than two decimals under the field (like a wallet top-up, C-19) and sends nothing; the server refuses it too (PLN-002).
 */
describe('Plan monthly fee (C-05)', () => {
  let backend: HttpTestingController;
  let page: PlanDrawer;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        WalletsFacade,
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(SubscriptionsPage);
    TestBed.tick();
    await settle();
    backend.match(() => true).forEach((request) => request.flush({ items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 }));
    await settle();
    page = fixture.componentInstance as unknown as PlanDrawer;
  });

  function fill(fee: number): FormGroup {
    page.openPlan(null);
    const form = page.planForm();
    if (form === null) {
      throw new Error('the plan form did not open');
    }

    form.patchValue({ code: 'V14PLAN', name: { en: 'V14 plan', ar: 'باقة' }, monthlyFee: fee, currencyCode: 'EGP', maxBranches: 2, maxUsers: 14, smsQuota: 140, maxAnalysers: 0 });
    return form;
  }

  it('refuses 14.145 under the field and sends nothing', async () => {
    const form = fill(14.145);
    expect(form.get('monthlyFee')?.errors).toEqual({ server: 'platform.plans.validation.twoDecimals' });
    await page.savePlan();
    backend.expectNone(PLATFORM_API.plans);
  });

  it('sends 14.15 as typed', async () => {
    const form = fill(14.15);
    expect(form.valid).toBe(true);
    const saving = page.savePlan();
    await settle();
    const request = backend.expectOne((candidate) => candidate.url === PLATFORM_API.plans && candidate.method === 'POST');
    expect((request.request.body as { monthlyFee: number }).monthlyFee).toBe(14.15);
    request.flush({ id: 'p1' });
    await saving;
  });
});
