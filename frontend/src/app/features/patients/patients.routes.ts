import { Routes } from '@angular/router';

import { PatientFileFacade } from './state/patient-file.facade';
import { PatientsFacade } from './state/patients.facade';

/** Patients (CP-11): the register and one file per page. Children of the lazy `/patients` route registered by the integrator. */
export const PATIENTS_ROUTES: Routes = [
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
];
