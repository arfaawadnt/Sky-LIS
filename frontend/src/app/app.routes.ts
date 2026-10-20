import { Routes } from '@angular/router';

import { anonymousGuard, authGuard } from './core/auth/auth.guard';
import { platformGuard, tenantGuard } from './core/auth/portal.guards';
import { branchGuard, passwordChangeGuard } from './features/account/account.guards';
import { navRegistryGuard } from './shell/nav.guard';

/**
 * Lazy routes per feature (01 §6.5). Guards are affordance; the server decides on every request. The signed-in
 * pages are children of the shell route (sidebar + top bar); the integrator registers each module's routes as a
 * sibling of `billing` (client portal) or under `admin` (Super Admin portal, platform tenant only).
 */
export const routes: Routes = [
  {
    path: 'welcome',
    loadChildren: () => import('./features/public/public.routes').then((m) => m.PUBLIC_ROUTES),
  },
  {
    path: 'login',
    canActivate: [anonymousGuard],
    loadChildren: () => import('./features/auth/auth.routes').then((m) => m.AUTH_ROUTES),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./shell/shell.component').then((m) => m.ShellComponent),
    // A-08 / C-15: a typed URL obeys the navigation registry (permission and release); CP-04: a client session picks its
    // branch first; an administrator reset forces a password change (both let the account pages through)
    canActivateChild: [navRegistryGuard, branchGuard, passwordChangeGuard],
    children: [
      {
        path: '',
        pathMatch: 'full',
        canActivate: [tenantGuard],   // every client feature below is guarded the same way: a platform session is sent to /admin (UAT D-17)
        loadChildren: () => import('./features/home/home.routes').then((m) => m.HOME_ROUTES),
      },
      {
        path: 'catalogue',
        canActivate: [tenantGuard],
        loadChildren: () => import('./features/catalogue/catalogue.routes').then((m) => m.CATALOGUE_ROUTES),
      },
      {
        path: 'organization',
        canActivate: [tenantGuard],
        loadChildren: () => import('./features/organization/organization.routes').then((m) => m.ORGANIZATION_ROUTES),
      },
      {
        path: 'pricing',
        canActivate: [tenantGuard],
        loadChildren: () => import('./features/pricing/pricing.routes').then((m) => m.PRICING_ROUTES),
      },
      {
        path: 'patients',
        canActivate: [tenantGuard],
        loadChildren: () => import('./features/patients/patients.routes').then((m) => m.PATIENTS_ROUTES),
      },
      {
        path: 'registration',
        canActivate: [tenantGuard],
        loadChildren: () => import('./features/registration/registration.routes').then((m) => m.REGISTRATION_ROUTES),
      },
      {
        path: 'samples',
        canActivate: [tenantGuard],
        loadChildren: () => import('./features/samples/samples.routes').then((m) => m.SAMPLES_ROUTES),
      },
      {
        path: 'billing',
        canActivate: [tenantGuard],
        loadChildren: () => import('./features/billing/billing.routes').then((m) => m.BILLING_ROUTES),
      },
      {
        path: 'account',
        canActivate: [tenantGuard], // C-14: the platform portal has its own account pages under /admin
        loadChildren: () => import('./features/account/account.routes').then((m) => m.ACCOUNT_ROUTES),
      },
      {
        path: 'integration',
        canActivate: [tenantGuard],
        loadChildren: () => import('./features/integration/integration.routes').then((m) => m.INTEGRATION_ROUTES),
      },
      {
        path: 'results',
        canActivate: [tenantGuard],
        loadChildren: () => import('./features/results/results.routes').then((m) => m.RESULTS_ROUTES),
      },
      {
        path: 'reports',
        loadChildren: () => import('./features/reporting/reporting.routes').then((m) => m.REPORTING_ROUTES),
      },
      {
        path: 'admin',
        canActivate: [platformGuard],
        children: [
          {
            path: '',
            pathMatch: 'full',
            loadChildren: () => import('./features/admin-home/admin-home.routes').then((m) => m.ADMIN_HOME_ROUTES),
          },
          {
            path: 'catalogue',
            loadChildren: () => import('./features/catalogue/catalogue.routes').then((m) => m.CATALOGUE_ROUTES),
          },
          {
            path: 'organization',
            loadChildren: () => import('./features/organization/organization.routes').then((m) => m.ORGANIZATION_ROUTES),
          },
          {
            path: '',
            loadChildren: () => import('./features/platform/platform.routes').then((m) => m.PLATFORM_ROUTES),
          },
          {
            path: '',
            loadChildren: () => import('./features/account/account.routes').then((m) => m.ACCOUNT_ADMIN_ROUTES),
          },
          // admin modules are registered here by the integrator: { path: 'tenants', loadChildren: … }
        ],
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
