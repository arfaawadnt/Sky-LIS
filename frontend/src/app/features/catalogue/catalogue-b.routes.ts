import { Routes } from '@angular/router';

import { DiagnosesFacade, DoctorsFacade, PreparationsFacade, ShortcutSetsFacade, TestRulesFacade } from './state-b/catalogue-b-list.facade';

/**
 * Catalogue B (CP-46…CP-54, CP-56, SA-14): preparations, comment library, doctors, diagnoses, sending labs, shortcut
 * sets, test rules, reasons, report templates and the microbiology masters. Mounted with Catalogue A at
 * `/catalogue` (client portal) and `/admin/catalogue` (Super Admin portal, platform tenant = global templates).
 */
export const CATALOGUE_B_ROUTES: Routes = [
  {
    path: 'preparations',
    providers: [PreparationsFacade],
    loadComponent: () => import('./pages-b/preparations.page').then((m) => m.PreparationsPage),
  },
  {
    path: 'comments',
    loadComponent: () => import('./pages-b/comment-templates.page').then((m) => m.CommentTemplatesPage),
  },
  {
    path: 'doctors',
    providers: [DoctorsFacade],
    loadComponent: () => import('./pages-b/doctors.page').then((m) => m.DoctorsPage),
  },
  {
    path: 'diagnoses',
    providers: [DiagnosesFacade],
    loadComponent: () => import('./pages-b/diagnoses.page').then((m) => m.DiagnosesPage),
  },
  {
    path: 'sending-labs',
    loadComponent: () => import('./pages-b/sending-labs.page').then((m) => m.SendingLabsPage),
  },
  {
    path: 'shortcut-sets',
    providers: [ShortcutSetsFacade],
    loadComponent: () => import('./pages-b/shortcut-sets.page').then((m) => m.ShortcutSetsPage),
  },
  {
    path: 'test-rules',
    providers: [TestRulesFacade],
    loadComponent: () => import('./pages-b/test-rules.page').then((m) => m.TestRulesPage),
  },
  {
    path: 'reasons',
    loadComponent: () => import('./pages-b/reasons.page').then((m) => m.ReasonsPage),
  },
  {
    path: 'report-templates',
    loadComponent: () => import('./pages-b/report-templates.page').then((m) => m.ReportTemplatesPage),
  },
  {
    path: 'microbiology',
    loadComponent: () => import('./pages-b/microbiology.page').then((m) => m.MicrobiologyPage),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'organisms' },
      { path: 'organisms', loadComponent: () => import('./pages-b/microbiology.pages').then((m) => m.OrganismsPage) },
      { path: 'antibiotics', loadComponent: () => import('./pages-b/microbiology.pages').then((m) => m.AntibioticsPage) },
      { path: 'drug-types', loadComponent: () => import('./pages-b/microbiology.pages').then((m) => m.DrugTypesPage) },
      { path: 'commercial-names', loadComponent: () => import('./pages-b/microbiology.pages').then((m) => m.CommercialNamesPage) },
      { path: 'sensitivity-values', loadComponent: () => import('./pages-b/microbiology.pages').then((m) => m.SensitivityValuesPage) },
      { path: 'culture-specimens', loadComponent: () => import('./pages-b/culture-specimens.page').then((m) => m.CultureSpecimensPage) },
    ],
  },
];
