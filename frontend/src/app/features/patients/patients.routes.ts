import { inject } from '@angular/core';
import { type CanActivateFn, Routes } from '@angular/router';

import { I18nService } from '../../core/i18n/i18n.service';

import { PatientFileFacade } from './state/patient-file.facade';
import { PatientsFacade } from './state/patients.facade';

/** The module's dictionary is loaded before its first page renders, so no raw key (`patients.loading`) shows on the first visit (validation A-06). */
export const patientsDictionary: CanActivateFn = () => inject(I18nService).load('patients').then(() => true);

/** Patients (CP-11): the register and one file per page. Children of the lazy `/patients` route registered by the integrator. */
export const PATIENTS_ROUTES: Routes = [
  {
    path: '',
    canActivate: [patientsDictionary],
    children: [
      {
        path: '',
        pathMatch: 'full',
        providers: [PatientsFacade],
        loadComponent: () => import('./pages/patients.page').then((m) => m.PatientsPage),
      },
      {
        path: ':id',
        providers: [PatientFileFacade],
        loadComponent: () => import('./pages/patient-file.page').then((m) => m.PatientFilePage),
      },
    ],
  },
];
