import { Routes } from '@angular/router';

import { WorkAmountFacade } from './state/work-amount.facade';

/** Reporting: the work amount report (change request 2026-10-13 item 3). Claims (CP-31) and the financial reports (CP-35) are Release 2. */
export const REPORTING_ROUTES: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'work-amount' },
  {
    path: 'work-amount',
    providers: [WorkAmountFacade],
    loadComponent: () => import('./pages/work-amount.page').then((m) => m.WorkAmountPage),
  },
];
