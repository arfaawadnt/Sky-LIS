import { Routes } from '@angular/router';

import { ClientsFacade } from './state/clients.facade';
import { DashboardFacade } from './state/dashboard.facade';
import { LoginLogsFacade } from './state/login-logs.facade';
import { ParametersFacade, PcrKitsFacade, TemplatesFacade } from './state/reference.facades';
import { SessionsFacade } from './state/sessions.facade';
import { WalletsFacade } from './state/wallets.facade';

/**
 * Platform administration (SA-03…SA-05, SA-08, SA-10, SA-11, SA-15…SA-20), relative to /admin (the platform tenant's
 * portal; `platformGuard` is on the parent route). Facades are provided per page. The server refuses every one of
 * these APIs to a client tenant (PLT-403) whatever the role holds.
 */
export const PLATFORM_ROUTES: Routes = [
  {
    path: 'dashboard',
    providers: [DashboardFacade],
    loadComponent: () => import('./pages/dashboard.page').then((m) => m.DashboardPage),
  },
  {
    path: 'clients',
    providers: [ClientsFacade],
    loadComponent: () => import('./pages/clients.page').then((m) => m.ClientsPage),
  },
  {
    path: 'subscriptions',
    providers: [WalletsFacade],
    loadComponent: () => import('./pages/subscriptions.page').then((m) => m.SubscriptionsPage),
  },
  {
    path: 'sessions',
    providers: [SessionsFacade],
    loadComponent: () => import('./pages/sessions.page').then((m) => m.SessionsPage),
  },
  {
    path: 'login-logs',
    providers: [LoginLogsFacade],
    loadComponent: () => import('./pages/login-logs.page').then((m) => m.LoginLogsPage),
  },
  {
    path: 'countries',
    loadComponent: () => import('./pages/countries.page').then((m) => m.CountriesPage),
  },
  {
    path: 'governorates',
    loadComponent: () => import('./pages/governorates.page').then((m) => m.GovernoratesPage),
  },
  {
    path: 'notification-templates',
    providers: [TemplatesFacade],
    loadComponent: () => import('./pages/notification-templates.page').then((m) => m.NotificationTemplatesPage),
  },
  {
    path: 'parameters',
    providers: [ParametersFacade],
    loadComponent: () => import('./pages/parameters.page').then((m) => m.ParametersPage),
  },
  {
    path: 'library/analyser-models',
    loadComponent: () => import('./pages/analyser-models.page').then((m) => m.AnalyserModelsPage),
  },
  {
    path: 'library/pcr-kits',
    providers: [PcrKitsFacade],
    loadComponent: () => import('./pages/pcr-kits.page').then((m) => m.PcrKitsPage),
  },
  {
    path: 'library/code-systems',
    loadComponent: () => import('./pages/code-systems.page').then((m) => m.CodeSystemsPage),
  },
  {
    path: 'library/rule-templates',
    loadComponent: () => import('./pages/rule-templates.page').then((m) => m.RuleTemplatesPage),
  },
];
