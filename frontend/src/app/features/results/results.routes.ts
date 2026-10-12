import { inject } from '@angular/core';
import { type CanActivateFn, Routes } from '@angular/router';

import { I18nService } from '../../core/i18n/i18n.service';

import { ResultsCaseFacade } from './state/results-case.facade';
import { WorklistFacade } from './state/worklist.facade';

/** The module's dictionary is loaded before its first page renders, so no raw key (`results.loading`) shows on the first visit (validation A-06). */
export const resultsDictionary: CanActivateFn = () => inject(I18nService).load('results').then(() => true);

/**
 * Results, Release 1 (SRS CP-18 Worklist, CP-19 Results by accession; WF-06). Children of the lazy `/results` route
 * registered in app.routes.ts by the integrator. `/results/by-accession?accession=<acc>` is the link other pages
 * (registration search, patient file, worklist) use to open a visit's results.
 */
export const RESULTS_ROUTES: Routes = [
  {
    path: '',
    canActivate: [resultsDictionary],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'worklist' },
      {
        path: 'worklist',
        providers: [WorklistFacade],
        loadComponent: () => import('./pages/worklist.page').then((m) => m.WorklistPage),
      },
      {
        path: 'by-accession',
        providers: [ResultsCaseFacade],
        loadComponent: () => import('./pages/results-by-accession.page').then((m) => m.ResultsByAccessionPage),
      },
    ],
  },
];
