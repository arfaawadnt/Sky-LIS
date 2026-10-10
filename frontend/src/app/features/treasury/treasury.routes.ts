import { Routes } from '@angular/router';

import { TreasuryAccessFacade } from './state/treasury-access.facade';

export const TREASURY_ROUTES: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'access' },
  {
    path: 'access',
    providers: [TreasuryAccessFacade],
    loadComponent: () => import('./pages/treasury-access.page').then((m) => m.TreasuryAccessPage),
  },
  {
    path: 'treasuries',
    loadComponent: () => import('./pages/treasuries.page').then((m) => m.TreasuriesPage),
  },
];
