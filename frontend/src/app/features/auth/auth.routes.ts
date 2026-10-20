import { Routes } from '@angular/router';

import type { SignInMode } from './pages/login.page';

const laboratory: { readonly mode: SignInMode } = { mode: 'laboratory' };
const admin: { readonly mode: SignInMode } = { mode: 'admin' };

/**
 * LIS-CR-005, mounted at `login`: **Find your laboratory** (no form but the laboratory address). `mfa-enrollment` stays
 * the placeholder target of `MFA-ENROLL` (`problemDetailsInterceptor`) until MFA enrolment is built.
 */
export const FIND_LABORATORY_ROUTES: Routes = [
  { path: '', loadComponent: () => import('./pages/find-laboratory.page').then((m) => m.FindLaboratoryPage) },
  // an old bookmark of the shared recovery; it returns to the find page
  { path: 'recover', loadComponent: () => import('./pages/recovery.page').then((m) => m.RecoveryPage) },
  { path: 'mfa-enrollment', loadComponent: () => import('./pages/find-laboratory.page').then((m) => m.FindLaboratoryPage) },
];

/** LIS-CR-005, mounted at `lab`: the sign-in of one laboratory (`/lab/<slug>`) and its password recovery (SRS CP-03); `/lab` alone is the find page. */
export const LAB_SIGN_IN_ROUTES: Routes = [
  { path: '', pathMatch: 'full', redirectTo: '/login' },
  { path: ':slug', data: laboratory, loadComponent: () => import('./pages/login.page').then((m) => m.LoginPage) },
  { path: ':slug/recover', data: laboratory, loadComponent: () => import('./pages/recovery.page').then((m) => m.RecoveryPage) },
];

/** LIS-CR-005, mounted at `admin/login`: the Super Admin sign-in and its password recovery. */
export const ADMIN_SIGN_IN_ROUTES: Routes = [
  { path: '', data: admin, loadComponent: () => import('./pages/login.page').then((m) => m.LoginPage) },
  { path: 'recover', data: admin, loadComponent: () => import('./pages/recovery.page').then((m) => m.RecoveryPage) },
];

