import { inject } from '@angular/core';
import { type CanActivateFn, Router, Routes } from '@angular/router';

import { AuthStore } from '../../core/auth/auth.store';
import { landingEntry } from '../../shell/nav.filter';

/** The client portal's dashboard (SRS CP-05) is the account module's page; the home route forwards to it. */
export const DASHBOARD_PATH = '/account/dashboard';

/**
 * R1-04: the client home is never a page of its own. A user who may see the dashboard lands on it; anyone else (a unit
 * head without `dashboard.view`, for instance) lands on the first menu entry they may open. Only a user whose menu is
 * empty stays on the home page, which then says so.
 */
export const homeRedirect: CanActivateFn = () => {
  const auth = inject(AuthStore);
  const can = (permission: string): boolean => auth.hasPermission(permission);
  const target = can('dashboard.view') ? DASHBOARD_PATH : (landingEntry('client', can)?.path ?? null);
  return target === null || target === '/' ? true : inject(Router).parseUrl(target);
};

export const HOME_ROUTES: Routes = [{ path: '', canActivate: [homeRedirect], loadComponent: () => import('./pages/home.page').then((m) => m.HomePage) }];
