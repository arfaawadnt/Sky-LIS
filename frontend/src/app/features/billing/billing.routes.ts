import { inject } from '@angular/core';
import { type CanActivateFn, Routes } from '@angular/router';

import { I18nService } from '../../core/i18n/i18n.service';

import { BatchPaymentFacade } from './state/batch-payment.facade';
import { CashClosingFacade } from './state/cash-closing.facade';
import { CashDeskFacade } from './state/cash-desk.facade';
import { CashLedgerFacade } from './state/cash-ledger.facade';
import { PaymentsFacade } from './state/payments.facade';
import { RefundsFacade } from './state/refunds.facade';

/**
 * Billing (SRS CP-23 Payments, CP-24 Refunds, CP-27 Batch payment, CP-30 Cash closing, CP-70 Cash ledger), mounted at /billing by the
 * integrator. The cash desk (auto-print, payment methods, cashier) is shared by the pages; each page has its facade.
 * Money moves for the signed-in cashier on the session branch (LIS-CR-004: no treasury).
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
        // CP-70 Cash ledger (change request LIS-CR-011, C-15): nav entry under Finance, billing.view; entries billing.create,
        // their void billing.approve, the day close billing.manage
        path: 'cash-ledger',
        providers: [CashLedgerFacade],
        loadComponent: () => import('./pages/cash-ledger.page').then((m) => m.CashLedgerPage),
      },
      {
        // CP-30b set-up list (change request 2026-10-19): nav entry under Setup, billing.view; writes need billing.manage
        path: 'payment-methods',
        loadComponent: () => import('./pages/payment-methods.page').then((m) => m.PaymentMethodsPage),
      },
    ],
  },
];
