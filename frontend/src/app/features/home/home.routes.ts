import { inject } from '@angular/core';
import { type CanActivateFn, Router, Routes } from '@angular/router';

import { AuthStore } from '../../core/auth/auth.store';

/** The client portal's dashboard (SRS CP-05) is the account module's page; the home route forwards to it. */
export const DASHBOARD_PATH = '/account/dashboard';

/** A user who may see the dashboard lands on it; anyone else keeps the session summary of this page. */
export const homeRedirect: CanActivateFn = () =>
  inject(AuthStore).hasPermission('dashboard.view') ? inject(Router).createUrlTree([DASHBOARD_PATH]) : true;

export const HOME_ROUTES: Routes = [{ path: '', canActivate: [homeRedirect], loadComponent: () => import('./pages/home.page').then((m) => m.HomePage) }];
