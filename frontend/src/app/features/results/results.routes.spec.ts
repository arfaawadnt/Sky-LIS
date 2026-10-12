import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';

import { I18nService } from '../../core/i18n/i18n.service';
import { RESULTS_ROUTES, resultsDictionary } from './results.routes';

describe('Results routes', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] }));

  it('loads the results dictionary before the first page renders, so no raw key shows (validation A-06)', async () => {
    const load = vi.spyOn(TestBed.inject(I18nService), 'load').mockResolvedValue();

    const allowed = await TestBed.runInInjectionContext(() => resultsDictionary({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot));

    expect(allowed).toBe(true);
    expect(load).toHaveBeenCalledWith('results');
    expect(RESULTS_ROUTES[0]?.canActivate).toContain(resultsDictionary);
    expect(RESULTS_ROUTES).toHaveLength(1);   // every page sits under the guarded parent
  });
});
