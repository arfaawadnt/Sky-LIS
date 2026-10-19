import { Routes } from '@angular/router';

import { AccessionFacade } from './state/accession.facade';
import { PriceQueryFacade } from './state/price-query.facade';
import { RegistrationFacade } from './state/registration.facade';
import { VisitsFacade } from './state/visits.facade';

/**
 * Registration (Release 1: CP-06 Registration, CP-10 Search registrations, CP-12 Price query), mounted at
 * `/registration` by the integrator. Each page gets its facade at the route (01 §6.3). The booking, batch,
 * lab-to-lab, money-transfer and cancellations pages (CP-07/08/09/25/26) are Release 2 and have no route here.
 *
 * - `/registration` and `/registration/new` — walk-in registration (`?patientId=` from a patient file, `?services=a,b&tierId=`
 *   from the price query; both paths serve the page so the links of other modules keep their query string)
 * - `/registration/visits` — the search register
 * - `/registration/visits/:visitId` — a saved visit (add or release services, details, discount)
 * - `/registration/visits/:visitId/job-order` — the job order on screen (the link Samples and the search register use)
 * - `/registration/price-query` — CP-12
 * - `/registration/accession` — Accession details (CP-10b, `?accession=` or `?visitId=`; change request 2026-10-19 P-C9)
 */
export const REGISTRATION_ROUTES: Routes = [
  {
    path: '',
    pathMatch: 'full',
    providers: [RegistrationFacade],
    loadComponent: () => import('./pages/registration.page').then((m) => m.RegistrationPage),
  },
  {
    path: 'new',
    providers: [RegistrationFacade],
    loadComponent: () => import('./pages/registration.page').then((m) => m.RegistrationPage),
  },
  {
    path: 'visits',
    pathMatch: 'full',
    providers: [VisitsFacade],
    loadComponent: () => import('./pages/visits.page').then((m) => m.VisitsPage),
  },
  {
    path: 'visits/:visitId/job-order',
    loadComponent: () => import('./pages/job-order.page').then((m) => m.JobOrderPage),
  },
  {
    path: 'visits/:visitId',
    providers: [RegistrationFacade],
    loadComponent: () => import('./pages/registration.page').then((m) => m.RegistrationPage),
  },
  {
    path: 'price-query',
    providers: [PriceQueryFacade],
    loadComponent: () => import('./pages/price-query.page').then((m) => m.PriceQueryPage),
  },
  {
    path: 'accession',
    providers: [AccessionFacade],
    loadComponent: () => import('./pages/accession-details.page').then((m) => m.AccessionDetailsPage),
  },
];
