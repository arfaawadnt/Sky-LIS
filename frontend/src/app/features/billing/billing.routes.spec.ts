import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';

import { I18nService } from '../../core/i18n/i18n.service';
import { BILLING_ROUTES, billingDictionary } from './billing.routes';

describe('Billing routes', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] }));

  it('loads the billing dictionary before the first page renders, so no raw key shows (UAT A-07)', async () => {
    const load = vi.spyOn(TestBed.inject(I18nService), 'load').mockResolvedValue();

    const allowed = await TestBed.runInInjectionContext(() => billingDictionary({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot));

    expect(allowed).toBe(true);
    expect(load).toHaveBeenCalledWith('billing');
    expect(BILLING_ROUTES[0]?.canActivate).toContain(billingDictionary);
  });
});
