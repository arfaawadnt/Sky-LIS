import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';

import { BILLING_PERMISSIONS, type PaymentListItem, type RefundListItem, type VisitAccountView } from '../../../core/api/billing.contracts';
import type { VisitBalanceView } from '../../../core/api/registration.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { dateTime, money } from '../data/registration-format';

/**
 * The *Payments* card of Accession details (CP-10b; C11 menu change): the balance figures (Billing's account when it
 * answered, else the visit's own balance), the posted payments and refunds with receipt numbers, method, amount, cashier
 * and time; *Take payment* (`billing.create`) and *Refund* (`billing.approve`) open the cash desk on this visit,
 * *Receipt* prints a payment's receipt, *All payments* opens the payments register (`/billing/payments?visitId=`).
 */
@Component({
  selector: 'app-reg-accession-payments',
  imports: [RouterLink, BadgeComponent, CanDirective, IconComponent, SectionCardComponent],
  template: `
    <app-section-card [title]="i18n.t('registration.accession.payments.title')" [padded]="false">
      <a cardActions class="btn sm primary" *appCan="permissions.pay" routerLink="/billing/payments" [queryParams]="{ visitId: visitId() }"><app-icon name="coins" [size]="14" />{{ i18n.t('registration.accession.payments.take') }}</a>
      <a cardActions class="btn sm" *appCan="permissions.refund" routerLink="/billing/refunds" [queryParams]="{ visitId: visitId() }"><app-icon name="arrow-down" [size]="14" />{{ i18n.t('registration.accession.payments.refund') }}</a>
      <a cardActions class="btn sm ghost" routerLink="/billing/payments" [queryParams]="{ visitId: visitId() }">{{ i18n.t('registration.accession.payments.all') }}</a>
      @if (figures(); as f) {
        <dl class="figures">
          <div><dt>{{ i18n.t('registration.column.patientShare') }}</dt><dd>{{ amount(f.patientShareTotal) }}</dd></div>
          <div><dt>{{ i18n.t('registration.column.paid') }}</dt><dd>{{ amount(f.paid) }}</dd></div>
          <div><dt>{{ i18n.t('registration.accession.payments.refunded') }}</dt><dd>{{ amount(f.refunded) }}</dd></div>
          <div><dt>{{ i18n.t('registration.column.remaining') }}</dt><dd class="strong">{{ amount(f.remaining) }}</dd></div>
          <div><dt>{{ i18n.t('registration.accession.payments.refundable') }}</dt><dd>{{ amount(f.refundable) }}</dd></div>
          @if (account(); as a) {
            <div><dt>{{ i18n.t('registration.column.status') }}</dt><dd><app-badge [status]="a.balanceStatus">{{ i18n.t('shared.status.' + a.balanceStatus) }}</app-badge></dd></div>
          }
        </dl>
      }
      @if (loading()) {
        <p class="muted small pad" role="status">{{ i18n.t('registration.loading') }}</p>
      } @else if (account() === null) {
        <p class="muted small pad">{{ i18n.t('registration.accession.payments.unavailable') }}</p>
      } @else if (rows().length === 0) {
        <p class="muted small pad">{{ i18n.t('registration.accession.payments.empty') }}</p>
      } @else {
        <div class="scroll">
          <table class="grid">
            <thead>
              <tr>
                <th>{{ i18n.t('registration.accession.column.receipt') }}</th>
                <th>{{ i18n.t('registration.accession.column.method') }}</th>
                <th class="num">{{ i18n.t('registration.accession.column.amount') }}</th>
                <th>{{ i18n.t('registration.accession.column.cashier') }}</th>
                <th>{{ i18n.t('registration.accession.column.time') }}</th>
                <th><span class="visually-hidden">{{ i18n.t('registration.column.actions') }}</span></th>
              </tr>
            </thead>
            <tbody>
              @for (row of rows(); track row.id) {
                <tr [class.voided]="row.voided" [attr.data-receipt]="row.number">
                  <td><span class="mono">{{ row.number }}</span>@if (row.kind === 'refund') { <span class="small muted"> · {{ i18n.t('registration.accession.payments.refundRow') }}</span> }</td>
                  <td>{{ i18n.t('registration.payment.methods.' + row.method) }}@if (row.reference) { <span class="small muted mono"> {{ row.reference }}</span> }</td>
                  <td class="num" [class.minus]="row.kind === 'refund'">{{ row.kind === 'refund' ? '−' : '' }}{{ amount(row.amount) }}</td>
                  <td class="small">{{ row.cashier }}</td>
                  <td class="small">{{ when(row.atUtc) }}</td>
                  <td class="actions">
                    @if (row.voided) {
                      <app-badge tone="bad">{{ i18n.t('registration.accession.payments.voided') }}</app-badge>
                    } @else if (row.kind === 'payment') {
                      <button type="button" class="btn sm ghost" (click)="receipt.emit(row.id)"><app-icon name="printer" [size]="14" />{{ i18n.t('registration.accession.payments.receipt') }}</button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </app-section-card>
  `,
  styles: `
    .pad { padding: var(--sp-3) var(--sp-4); }
    .figures { display: flex; flex-wrap: wrap; gap: var(--sp-2) var(--sp-5); margin: 0; padding: var(--sp-3) var(--sp-4); border-block-end: 1px solid var(--line-soft); font-size: var(--fs-sm); }
    .figures div { display: grid; gap: 2px; }
    .figures dt { color: var(--muted); }
    .figures dd { margin: 0; font-variant-numeric: tabular-nums; }
    .strong { font-weight: 700; font-size: var(--fs-md); }
    .scroll { overflow-x: auto; }
    .grid { width: 100%; border-collapse: collapse; font-size: var(--fs-sm); }
    .grid th, .grid td { padding: var(--sp-2) var(--sp-3); border-block-end: 1px solid var(--line-soft); text-align: start; vertical-align: top; }
    .grid th { color: var(--ink-2); font-weight: 600; white-space: nowrap; }
    .num { text-align: end !important; font-variant-numeric: tabular-nums; white-space: nowrap; }
    .minus { color: var(--bad); }
    .voided td { color: var(--muted); text-decoration: line-through; }
    .voided td.actions { text-decoration: none; }
    .actions { white-space: nowrap; text-align: end; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccessionPaymentsComponent {
  protected readonly i18n = inject(I18nService);

  readonly visitId = input('');
  readonly account = input<VisitAccountView | null>(null);
  /** The visit's own balance (`VisitDetail.balance`): the figures when Billing's account is not readable. */
  readonly balance = input<VisitBalanceView | null>(null);
  readonly loading = input(false);
  readonly receipt = output<string>();

  protected readonly permissions = BILLING_PERMISSIONS;

  protected readonly figures = computed<VisitBalanceView | null>(() => {
    const account = this.account();
    return account === null ? this.balance() : { patientShareTotal: account.patientShareTotal, paid: account.paid, refunded: account.refunded, remaining: account.remaining, refundable: account.refundable };
  });

  /** Payments and refunds in one list, newest first. */
  protected readonly rows = computed<readonly MoneyRow[]>(() => {
    const account = this.account();
    if (account === null) {
      return [];
    }

    const rows: MoneyRow[] = [
      ...account.payments.map((p: PaymentListItem): MoneyRow => ({ kind: 'payment', id: p.id, number: p.receiptNumber, method: p.method, reference: p.reference, amount: p.amount, cashier: p.cashierName, atUtc: p.paidAtUtc, voided: p.status === 'Voided' })),
      ...account.refunds.map((r: RefundListItem): MoneyRow => ({ kind: 'refund', id: r.id, number: r.number, method: r.method, reference: r.reference, amount: r.amount, cashier: r.cashierName, atUtc: r.refundedAtUtc, voided: false })),
    ];
    return rows.sort((a, b) => b.atUtc.localeCompare(a.atUtc));
  });

  protected amount(value: number): string {
    return money(value, this.i18n.lang());
  }

  protected when(utc: string | null): string {
    return dateTime(utc, this.i18n.lang());
  }
}

interface MoneyRow {
  readonly kind: 'payment' | 'refund';
  readonly id: string;
  readonly number: string;
  readonly method: string;
  readonly reference: string | null;
  readonly amount: number;
  readonly cashier: string;
  readonly atUtc: string;
  readonly voided: boolean;
}
