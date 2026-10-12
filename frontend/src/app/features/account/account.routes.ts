import { inject } from '@angular/core';
import { type CanActivateFn, Routes } from '@angular/router';

import { I18nService } from '../../core/i18n/i18n.service';

import { AuditLogFacade } from './state/audit-log.facade';
import { BranchSelectionFacade } from './state/branch-selection.facade';
import { DashboardFacade } from './state/dashboard.facade';
import { ProfileFacade } from './state/profile.facade';
import { SupportDeskFacade, SupportFacade } from './state/support.facade';
import { WalletFacade } from './state/wallet.facade';

export { ACCOUNT_PATHS, branchGuard, changeBranchUrl, passwordChangeGuard } from './account.guards';

/** The module's dictionary is loaded before its first page renders, so no raw key (`account.loading`) shows on the first visit (validation A-06). */
export const accountDictionary: CanActivateFn = () => inject(I18nService).load('account').then(() => true);

/**
 * Account (SRS CP-04, CP-05, CP-65…CP-68; SA-21 for platform staff), mounted at /account inside the signed-in shell
 * for both portals. Facades are provided per page, so each page's state lives and dies with it.
 */
export const ACCOUNT_ROUTES: Routes = [
  {
    path: '',
    canActivate: [accountDictionary],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'select-branch',
        providers: [BranchSelectionFacade],
        loadComponent: () => import('./pages/select-branch.page').then((m) => m.SelectBranchPage),
      },
      {
        path: 'dashboard',
        providers: [DashboardFacade],
        loadComponent: () => import('./pages/dashboard.page').then((m) => m.DashboardPage),
      },
      {
        path: 'profile',
        providers: [ProfileFacade],
        loadComponent: () => import('./pages/profile.page').then((m) => m.ProfilePage),
      },
      {
        path: 'wallet',
        providers: [WalletFacade],
        loadComponent: () => import('./pages/wallet.page').then((m) => m.WalletPage),
      },
      {
        path: 'support',
        providers: [SupportFacade],
        loadComponent: () => import('./pages/support.page').then((m) => m.SupportPage),
      },
      {
        path: 'audit',
        providers: [AuditLogFacade],
        loadComponent: () => import('./pages/audit-log.page').then((m) => m.AuditLogPage),
      },
    ],
  },
];

/**
 * The Super Admin portal's pages of this module, mounted by the integrator under /admin (platformGuard):
 * 'support' = the platform support desk, 'audit' = SA-09 (the platform tenant's own audit log), 'profile' = SA-21.
 */
export const ACCOUNT_ADMIN_ROUTES: Routes = [
  {
    path: '',
    canActivate: [accountDictionary],
    children: [
      {
        path: 'support',
        providers: [SupportDeskFacade],
        loadComponent: () => import('./pages/support-desk.page').then((m) => m.SupportDeskPage),
      },
      {
        path: 'audit',
        providers: [AuditLogFacade],
        loadComponent: () => import('./pages/audit-log.page').then((m) => m.AuditLogPage),
      },
      {
        path: 'profile',
        providers: [ProfileFacade],
        loadComponent: () => import('./pages/profile.page').then((m) => m.ProfilePage),
      },
    ],
  },
];
