import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Router } from '@angular/router';

import { AuthStore } from '../../../../core/auth/auth.store';
import { I18nService } from '../../../../core/i18n/i18n.service';
import { DialogComponent } from '../../../../shared/ui/dialog/dialog.component';
import { SUBSCRIPTION_WALLET_URL } from './subscription-banner.component';
import { SubscriptionNoticeState } from './subscription-notice.state';
import { subscriptionAction, subscriptionMessage, subscriptionNoticeKey, subscriptionTitle } from './subscription-notice.text';

/**
 * LIS-CR-012 (C-16): the sign-in modal — shown once per session and notice when the profile's notice calls for it (fewer than
 * three days left, in grace, or expired; `subscriptionNotice.showModal`), to every member: an administrator is offered the
 * wallet, anyone else is told to ask the administrator. Closing it in any way marks it seen for the session. Mounted in the
 * application root after sign-in, like the idle warning.
 */
@Component({
  selector: 'app-subscription-modal',
  imports: [DialogComponent],
  template: `
    <app-dialog [open]="open()" (openChange)="onOpenChange($event)" [title]="title()" size="sm" [danger]="notice()?.level === 'Expired'">
      <p class="message" data-testid="subscription-modal-message">{{ message() }}</p>
      @if (action(); as action) {
        <p class="action">{{ action }}</p>
      }
      <ng-container dialogFooter>
        @if (notice()?.canManage) {
          <button type="button" class="btn" (click)="close()">{{ i18n.t('account.subscription.later') }}</button>
          <button type="button" class="btn primary" data-autofocus="true" data-testid="subscription-modal-wallet" (click)="openWallet()">{{ i18n.t('account.subscription.openWallet') }}</button>
        } @else {
          <button type="button" class="btn primary" data-autofocus="true" (click)="close()">{{ i18n.t('account.subscription.ok') }}</button>
        }
      </ng-container>
    </app-dialog>
  `,
  styles: `
    .message {
      margin: 0;
      font-weight: 600;
    }

    .action {
      margin-block: var(--sp-2) 0;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SubscriptionModalComponent {
  protected readonly i18n = inject(I18nService);
  private readonly auth = inject(AuthStore);
  private readonly state = inject(SubscriptionNoticeState);
  private readonly router = inject(Router);

  private readonly key = computed(() => {
    const profile = this.auth.profile();
    const notice = profile?.subscriptionNotice;
    return profile && notice?.showModal ? subscriptionNoticeKey(profile.userId, notice) : null;
  });

  protected readonly notice = computed(() => (this.key() === null ? null : (this.auth.profile()?.subscriptionNotice ?? null)));

  protected readonly open = computed(() => {
    const key = this.key();
    return this.auth.isAuthenticated() && key !== null && !this.state.isModalSeen(key);
  });

  protected readonly title = computed(() => {
    const notice = this.notice();
    return notice ? subscriptionTitle(this.i18n, notice) : '';
  });

  protected readonly message = computed(() => {
    const notice = this.notice();
    return notice ? subscriptionMessage(this.i18n, notice) : '';
  });

  protected readonly action = computed(() => {
    const notice = this.notice();
    return notice ? subscriptionAction(this.i18n, notice) : '';
  });

  constructor() {
    void this.i18n.load('account');
  }

  protected onOpenChange(open: boolean): void {
    if (!open) {
      this.close();
    }
  }

  protected close(): void {
    const key = this.key();
    if (key !== null) {
      this.state.markModalSeen(key);
    }
  }

  protected openWallet(): void {
    this.close();
    void this.router.navigateByUrl(SUBSCRIPTION_WALLET_URL);
  }
}
