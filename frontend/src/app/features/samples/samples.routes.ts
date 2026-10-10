import { Routes } from '@angular/router';

import { CollectionFacade } from './state/collection.facade';
import { RejectionsFacade } from './state/rejections.facade';
import { TrackingFacade } from './state/tracking.facade';
import { VisitSamplesFacade } from './state/visit-samples.facade';

/** Samples (CP-14…CP-17, WF-05), mounted at /samples by the integrator; each page gets its facade at the route. */
export const SAMPLES_ROUTES: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'collection' },
  {
    path: 'collection',
    providers: [CollectionFacade],
    loadComponent: () => import('./pages/collection.page').then((m) => m.CollectionPage),
  },
  {
    path: 'visits',
    providers: [VisitSamplesFacade],
    loadComponent: () => import('./pages/visit-samples.page').then((m) => m.VisitSamplesPage),
  },
  {
    path: 'rejections',
    providers: [RejectionsFacade],
    loadComponent: () => import('./pages/rejections.page').then((m) => m.RejectionsPage),
  },
  {
    path: 'track',
    providers: [TrackingFacade],
    loadComponent: () => import('./pages/tracking.page').then((m) => m.TrackingPage),
  },
  {
    path: 'track/:sampleNumber',
    providers: [TrackingFacade],
    loadComponent: () => import('./pages/tracking.page').then((m) => m.TrackingPage),
  },
];
