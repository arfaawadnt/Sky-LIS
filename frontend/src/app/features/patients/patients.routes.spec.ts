import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';

import { I18nService } from '../../core/i18n/i18n.service';
import { PATIENTS_ROUTES, patientsDictionary } from './patients.routes';

describe('Patients routes', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] }));

  it('loads the patients dictionary before the first page renders, so no raw key shows (validation A-06)', async () => {
    const load = vi.spyOn(TestBed.inject(I18nService), 'load').mockResolvedValue();

    const allowed = await TestBed.runInInjectionContext(() => patientsDictionary({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot));

    expect(allowed).toBe(true);
    expect(load).toHaveBeenCalledWith('patients');
    expect(PATIENTS_ROUTES[0]?.canActivate).toContain(patientsDictionary);
    expect(PATIENTS_ROUTES).toHaveLength(1);   // every page sits under the guarded parent
  });
});
