import { Routes } from '@angular/router';

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
export const BILLING_ROUTES: Routes = [
  {
    path: '',
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
    ],
  },
];
