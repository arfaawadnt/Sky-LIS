import { Routes } from '@angular/router';

import { SubscriptionFacade } from './state/subscription.facade';

/**
 * Public website and subscription wizard (SRS CP-01, CP-02), anonymous, mounted at /welcome by the integrator WITHOUT
 * authentication: '' = the product site, 'subscribe' = the four-step wizard (its state lives and dies with the route).
 */
export const PUBLIC_ROUTES: Routes = [
  {
    path: '',
    pathMatch: 'full',
    title: 'Sky LIS',
    loadComponent: () => import('./pages/landing.page').then((m) => m.LandingPage),
  },
  {
    path: 'subscribe',
    title: 'Sky LIS — Subscribe',
    providers: [SubscriptionFacade],
    loadComponent: () => import('./pages/subscribe.page').then((m) => m.SubscribePage),
  },
];
