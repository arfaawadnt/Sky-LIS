import { Routes } from '@angular/router';

import { AnalyserTestsFacade } from './state/analyser-tests.facade';
import { AnalysersFacade } from './state/analysers.facade';
import { PcrKitsFacade } from './state/pcr-kits.facade';
import { SiteAgentsFacade } from './state/site-agents.facade';

/** Integration (CP-58…CP-62): analysers, mappings, sample codes, site agents, PCR kits. Children of the lazy /integration route. */
export const INTEGRATION_ROUTES: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'analysers' },
  {
    path: 'analysers',
    providers: [AnalysersFacade],
    loadComponent: () => import('./pages/analysers.page').then((m) => m.AnalysersPage),
  },
  {
    path: 'analyser-tests',
    providers: [AnalyserTestsFacade],
    loadComponent: () => import('./pages/analyser-tests.page').then((m) => m.AnalyserTestsPage),
  },
  {
    path: 'analyser-samples',
    providers: [AnalyserTestsFacade],
    loadComponent: () => import('./pages/analyser-samples.page').then((m) => m.AnalyserSamplesPage),
  },
  {
    path: 'site-agents',
    providers: [SiteAgentsFacade],
    loadComponent: () => import('./pages/site-agents.page').then((m) => m.SiteAgentsPage),
  },
  {
    path: 'pcr-kits',
    providers: [PcrKitsFacade],
    loadComponent: () => import('./pages/pcr-kits.page').then((m) => m.PcrKitsPage),
  },
];
