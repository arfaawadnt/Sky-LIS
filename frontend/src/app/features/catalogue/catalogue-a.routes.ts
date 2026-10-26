import type { CanDeactivateFn, Routes } from '@angular/router';

import type { ServiceSequencePage } from './pages-a/service-sequence.page';
import { CatalogueAOptions } from './state-a/catalogue-a-options';
import { ServicesListFacade, TestsListFacade } from './state-a/catalogue-lists.facade';
import { CompatibleConditionsFacade } from './state-a/compatible-conditions.facade';
import { ServiceEditorFacade } from './state-a/service-editor.facade';
import { ServiceSequenceFacade } from './state-a/service-sequence.facade';
import { TestEditorFacade } from './state-a/test-editor.facade';

/** An unsaved service order asks before the sequence page is left (the page owns the dialog, CP-42b). */
const leaveSequence: CanDeactivateFn<ServiceSequencePage> = (page) => page.canLeave();

/**
 * Catalogue A (CP-39…CP-45, CP-55): clinical groups, sample types, units, patient conditions, services, tests,
 * service sequence, additional services. Relative paths: mounted at `/catalogue` (client) and `/admin/catalogue`
 * (SA-12/SA-13 under the platform tenant). Editors are children of their list so `..` always returns to it.
 */
export const CATALOGUE_A_ROUTES: Routes = [
  { path: 'clinical-groups', loadComponent: () => import('./pages-a/clinical-groups.page').then((m) => m.ClinicalGroupsPage) },
  { path: 'sample-types', loadComponent: () => import('./pages-a/sample-types.page').then((m) => m.SampleTypesPage) },
  // LIS-CR-015 M4: compatible sample conditions (a condition whose tests join another condition's tube in the same visit)
  { path: 'compatible-conditions', providers: [CompatibleConditionsFacade], loadComponent: () => import('./pages-a/compatible-conditions.page').then((m) => m.CompatibleConditionsPage) },
  { path: 'units', loadComponent: () => import('./pages-a/units.page').then((m) => m.UnitsPage) },
  { path: 'patient-conditions', loadComponent: () => import('./pages-a/patient-conditions.page').then((m) => m.PatientConditionsPage) },
  {
    path: 'services',
    children: [
      { path: '', pathMatch: 'full', providers: [ServicesListFacade, CatalogueAOptions], loadComponent: () => import('./pages-a/services.page').then((m) => m.ServicesPage) },
      { path: 'new', providers: [ServiceEditorFacade, CatalogueAOptions], loadComponent: () => import('./pages-a/service-editor.page').then((m) => m.ServiceEditorPage) },
      { path: ':id', providers: [ServiceEditorFacade, CatalogueAOptions], loadComponent: () => import('./pages-a/service-editor.page').then((m) => m.ServiceEditorPage) },
    ],
  },
  {
    path: 'tests',
    children: [
      { path: '', pathMatch: 'full', providers: [TestsListFacade, CatalogueAOptions], loadComponent: () => import('./pages-a/tests.page').then((m) => m.TestsPage) },
      { path: 'new', providers: [TestEditorFacade, CatalogueAOptions], loadComponent: () => import('./pages-a/test-editor.page').then((m) => m.TestEditorPage) },
      { path: ':id', providers: [TestEditorFacade, CatalogueAOptions], loadComponent: () => import('./pages-a/test-editor.page').then((m) => m.TestEditorPage) },
    ],
  },
  {
    path: 'service-sequence',
    providers: [ServiceSequenceFacade, CatalogueAOptions],
    canDeactivate: [leaveSequence],
    loadComponent: () => import('./pages-a/service-sequence.page').then((m) => m.ServiceSequencePage),
  },
  { path: 'additional-services', loadComponent: () => import('./pages-a/additional-services.page').then((m) => m.AdditionalServicesPage) },
];
