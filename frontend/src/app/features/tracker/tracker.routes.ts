import { inject } from '@angular/core';
import { type CanActivateFn, Routes } from '@angular/router';

import { I18nService } from '../../core/i18n/i18n.service';

import { TrackerFacade } from './state/tracker.facade';

/** The module's dictionary is loaded before its first page renders, so no raw key shows on the first visit (validation A-06). */
export const trackerDictionary: CanActivateFn = () => inject(I18nService).load('tracker').then(() => true);

/**
 * Lab Tracker (LIS-CR-006, SRS CP-69): children of the lazy `/tracker` route registered in app.routes.ts by the
 * integrator (`tenantGuard`, nav entry `registration.view`).
 */
export const TRACKER_ROUTES: Routes = [
  {
    path: '',
    canActivate: [trackerDictionary],
    providers: [TrackerFacade],
    loadComponent: () => import('./pages/tracker.page').then((m) => m.TrackerPage),
  },
];
