import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';

import type { BalanceStatus, VisitAccountView } from '../../../core/api/billing.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import type { BadgeTone } from '../../../shared/ui/badge/status-tones';
import { StatCardComponent, type StatTone } from '../../../shared/ui/stat-card/stat-card.component';
import { formatDateTime, formatMoney } from './billing-format';

const BALANCE_TONES: Readonly<Record<BalanceStatus, BadgeTone>> = { Paid: 'ok', Unpaid: 'bad', Partial: 'warn', RefundDue: 'violet' };

/** The account card of a visit (CP-23): who and what, then total, discount, patient share, paid, refunded and remaining (BR-14). */
@Component({
  selector: 'app-billing-account-summary',
  imports: [StatCardComponent, BadgeComponent],
  template: `
    <div class="head">
      <div class="who">
        <span class="mono acc">{{ account().accession }}</span>
        <b>{{ account().patientName ?? i18n.t('billing.account.unknownPatient') }}</b>
        @if (account().patientNumber; as number) {
          <span class="muted mono">{{ number }}</span>
        }
      </div>
      <div class="meta">
        <app-badge [tone]="tone()">{{ i18n.t('billing.balance.' + account().balanceStatus) }}</app-badge>
        <span class="muted">{{ payer() }}</span>
        @if (account().batchNumber; as batch) {
          <span class="muted">{{ i18n.t('billing.field.batch') }} <span class="mono">{{ batch }}</span></span>
        }
        <span class="muted">{{ registered() }}</span>
      </div>
    </div>
    @if (account().billedThroughClaims) {
      <p class="info-strip">{{ i18n.t('billing.account.billedThroughClaims') }}</p>
    }
    <div class="stats">
      <app-stat-card [label]="i18n.t('billing.account.totalListPrice')" [value]="money(account().totalListPrice)" />
      <app-stat-card [label]="i18n.t('billing.account.discount')" [value]="money(account().discountTotal)" />
      <app-stat-card [label]="i18n.t('billing.account.patientShare')" [value]="money(account().patientShareTotal)" tone="brand" [hint]="i18n.t('billing.account.payerShare') + ' ' + money(account().payerShareTotal)" />
      <app-stat-card [label]="i18n.t('billing.account.paid')" [value]="money(account().paid)" tone="ok" />
      <app-stat-card [label]="i18n.t('billing.account.refunded')" [value]="money(account().refunded)" tone="info" />
      <app-stat-card [label]="i18n.t('billing.account.remaining')" [value]="money(account().remaining)" [tone]="remainingTone()" [hint]="account().currency" />
    </div>
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--sp-3);
    }

    .head {
      display: flex;
      flex-wrap: wrap;
      justify-content: space-between;
      gap: var(--sp-2) var(--sp-4);
      align-items: baseline;
    }

    .who,
    .meta {
      display: flex;
      flex-wrap: wrap;
      gap: var(--sp-2);
      align-items: center;
    }

    .acc {
      font-weight: 600;
    }

    .stats {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
      gap: var(--sp-3);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccountSummaryComponent {
  protected readonly i18n = inject(I18nService);

  readonly account = input.required<VisitAccountView>();

  protected readonly tone = computed(() => BALANCE_TONES[this.account().balanceStatus]);

  protected readonly remainingTone = computed<StatTone>(() => {
    const status = this.account().balanceStatus;
    return status === 'Paid' ? 'ok' : status === 'RefundDue' ? 'info' : 'bad';
  });

  protected readonly payer = computed(() => {
    const name = localizedPick({ en: this.account().payerNameEn ?? '', ar: this.account().payerNameAr ?? '' }, this.i18n.lang());
    return name === '' ? this.i18n.t('billing.doc.cashPatient') : name;
  });

  protected readonly registered = computed(() => formatDateTime(this.account().registeredAtUtc, this.i18n.lang()));

  protected money(value: number): string {
    return formatMoney(value, this.i18n.lang());
  }
}

export { BALANCE_TONES };
