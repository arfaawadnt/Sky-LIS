import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { AuthStore } from '../../../../core/auth/auth.store';
import { I18nService } from '../../../../core/i18n/i18n.service';
import { IconComponent } from '../../../../shared/ui/icon/icon.component';
import { SubscriptionNoticeState } from './subscription-notice.state';
import { subscriptionAction, subscriptionMessage, subscriptionNoticeKey } from './subscription-notice.text';

/** The wallet page of the client portal (CP-66), where an administrator requests a top-up. */
export const SUBSCRIPTION_WALLET_URL = '/account/wallet';

/**
 * LIS-CR-012 (C-16): the subscription banner under the top bar of the client portal — for the laboratory's administrators only
 * (`subscriptionNotice.canManage`), whenever the session's profile carries a notice (trial ending within a week, wallet covering
 * less than a week or at zero, grace, expired). It names what ends and when, the amount that covers one month, and links to the
 * wallet. Dismissable for the session (an expired subscription is not: the wallet is all that is left). Mounted by the shell.
 */
@Component({
  selector: 'app-subscription-banner',
  imports: [RouterLink, IconComponent],
  template: `
    @if (visible(); as notice) {
      <div class="sub-banner" [class]="'sub-banner level-' + notice.level" role="status" [attr.aria-label]="i18n.t('account.subscription.label')" data-testid="subscription-banner">
        <app-icon name="alert" [size]="16" />
        <p class="text">
          <strong>{{ message() }}</strong>
          @if (action(); as action) {
            <span> {{ action }}</span>
          }
        </p>
        <a class="btn sm primary" [routerLink]="walletUrl">{{ i18n.t('account.subscription.openWallet') }}</a>
        @if (notice.level !== 'Expired') {
          <button type="button" class="btn sm ghost" data-testid="subscription-banner-dismiss" (click)="dismiss()" [attr.aria-label]="i18n.t('account.subscription.dismiss')" [title]="i18n.t('account.subscription.dismiss')">×</button>
        }
      </div>
    }
  `,
  styles: `
    .sub-banner {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--sp-2) var(--sp-3);
      padding-block: var(--sp-2);
      padding-inline: var(--sp-4);
      border-block-end: 1px solid var(--line);
      background: var(--warn-soft);
      color: var(--ink);
    }

    .level-Grace,
    .level-Expired {
      background: var(--bad-soft);
    }

    .text {
      flex: 1 1 18rem;
      margin: 0;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SubscriptionBannerComponent {
  protected readonly i18n = inject(I18nService);
  private readonly auth = inject(AuthStore);
  private readonly state = inject(SubscriptionNoticeState);

  protected readonly walletUrl = SUBSCRIPTION_WALLET_URL;

  private readonly key = computed(() => {
    const profile = this.auth.profile();
    const notice = profile?.subscriptionNotice;
    return profile && notice ? subscriptionNoticeKey(profile.userId, notice) : null;
  });

  /** The notice to show: an administrator's, not dismissed in this session (an expired one cannot be dismissed). */
  protected readonly visible = computed(() => {
    const notice = this.auth.profile()?.subscriptionNotice;
    const key = this.key();
    if (!notice?.canManage || key === null) {
      return null;
    }

    return notice.level === 'Expired' || !this.state.isBannerDismissed(key) ? notice : null;
  });

  protected readonly message = computed(() => {
    const notice = this.visible();
    return notice ? subscriptionMessage(this.i18n, notice) : '';
  });

  protected readonly action = computed(() => {
    const notice = this.visible();
    return notice ? subscriptionAction(this.i18n, notice) : '';
  });

  constructor() {
    void this.i18n.load('account');
  }

  protected dismiss(): void {
    const key = this.key();
    if (key !== null) {
      this.state.dismissBanner(key);
    }
  }
}
