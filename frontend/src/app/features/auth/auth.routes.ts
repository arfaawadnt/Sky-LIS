import { Routes } from '@angular/router';

/** Sign-in (02 §4.1) and password recovery (SRS CP-03), anonymous, under /login. */
export const AUTH_ROUTES: Routes = [
  { path: '', loadComponent: () => import('./pages/login.page').then((m) => m.LoginPage) },
  { path: 'recover', loadComponent: () => import('./pages/recovery.page').then((m) => m.RecoveryPage) },
  { path: 'mfa-enrollment', loadComponent: () => import('./pages/login.page').then((m) => m.LoginPage) },
];
