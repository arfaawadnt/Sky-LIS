import { inject } from '@angular/core';
import { type CanActivateFn, Routes } from '@angular/router';

import { I18nService } from '../../core/i18n/i18n.service';

import { BatchPaymentFacade } from './state/batch-payment.facade';
import { CashClosingFacade } from './state/cash-closing.facade';
import { CashDeskFacade } from './state/cash-desk.facade';
import { PaymentsFacade } from './state/payments.facade';
import { RefundsFacade } from './state/refunds.facade';

/**
 * Billing (SRS CP-23 Payments, CP-24 Refunds, CP-27 Batch payment, CP-30 Cash closing), mounted at /billing by the
 * integrator. The cash desk (device, approved treasuries, auto-print) is shared by the pages; each page has its facade.
 * `/billing/payments?visitId=…` (or `?accession=…`) opens a visit directly (BR-26: the registration page links here).
 */
/** The module's dictionary is loaded before its first page renders, so no raw key (`billing.loading`) shows on the first visit (UAT A-07). */
export const billingDictionary: CanActivateFn = () => inject(I18nService).load('billing').then(() => true);

export const BILLING_ROUTES: Routes = [
  {
    path: '',
    canActivate: [billingDictionary],
    providers: [CashDeskFacade],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'payments' },
      {
        path: 'payments',
        providers: [PaymentsFacade],
        loadComponent: () => import('./pages/payments.page').then((m) => m.PaymentsPage),
      },
      {
        path: 'refunds',
        providers: [RefundsFacade],
        loadComponent: () => import('./pages/refunds.page').then((m) => m.RefundsPage),
      },
      {
        path: 'batch-payment',
        providers: [BatchPaymentFacade],
        loadComponent: () => import('./pages/batch-payment.page').then((m) => m.BatchPaymentPage),
      },
      {
        path: 'cash-closing',
        providers: [CashClosingFacade],
        loadComponent: () => import('./pages/cash-closing.page').then((m) => m.CashClosingPage),
      },
      {
        // CP-30b set-up list (change request 2026-10-19): nav entry under Setup, billing.view; writes need billing.manage
        path: 'payment-methods',
        loadComponent: () => import('./pages/payment-methods.page').then((m) => m.PaymentMethodsPage),
      },
    ],
  },
];
