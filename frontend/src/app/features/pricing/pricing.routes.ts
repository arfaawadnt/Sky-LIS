import type { CanDeactivateFn, Routes } from '@angular/router';

import type { PriceListEditorPage } from './pages/price-list-editor.page';
import { ContractPricingFacade } from './state/contract-pricing.facade';
import { PayersFacade } from './state/payers.facade';
import { PriceListEditorFacade } from './state/price-list-editor.facade';
import { ServiceNamesStore } from './state/service-names.store';

/** Unsaved prices ask before the editor is left (the page owns the dialog). */
const leaveEditor: CanDeactivateFn<PriceListEditorPage> = (page) => page.canLeave();

/** Pricing (CP-32, CP-33, CP-34): payers, base price lists, contract pricing. Registered at `/pricing` by the integrator. */
export const PRICING_ROUTES: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'payers' },
  {
    path: 'payers',
    providers: [PayersFacade],
    loadComponent: () => import('./pages/payers.page').then((m) => m.PayersPage),
  },
  {
    path: 'base-price-lists',
    loadComponent: () => import('./pages/base-price-lists.page').then((m) => m.BasePriceListsPage),
  },
  {
    path: 'base-price-lists/:listId',
    providers: [PriceListEditorFacade, ServiceNamesStore],
    canDeactivate: [leaveEditor],
    loadComponent: () => import('./pages/price-list-editor.page').then((m) => m.PriceListEditorPage),
  },
  {
    path: 'contract-pricing',
    providers: [ContractPricingFacade, ServiceNamesStore],
    loadComponent: () => import('./pages/contract-pricing.page').then((m) => m.ContractPricingPage),
  },
  {
    path: 'contract-pricing/:tierId',
    providers: [ContractPricingFacade, ServiceNamesStore],
    loadComponent: () => import('./pages/contract-pricing.page').then((m) => m.ContractPricingPage),
  },
];
