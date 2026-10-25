import type { SubscriptionNotice } from '../../../../core/api/auth.contracts';
import type { I18nService } from '../../../../core/i18n/i18n.service';
import { formatMoney } from '../../data/account-format';

/**
 * LIS-CR-012 (C-16): the sentences of a subscription notice in the user's language — what is ending and when, and what to top
 * up. Pure functions over the profile's notice; the banner and the modal share them.
 */
export function subscriptionMessage(i18n: I18nService, notice: SubscriptionNotice): string {
  const days = notice.daysLeft ?? 0;
  switch (notice.level) {
    case 'Expired':
      return i18n.t(notice.canManage ? 'account.subscription.expired' : 'account.subscription.expiredMember');
    case 'Grace':
      return days === 0 ? i18n.t('account.subscription.graceToday') : i18n.tc('account.subscription.grace', days);
    default:
      if (notice.reason === 'Trial') {
        return days === 0 ? i18n.t('account.subscription.trialEndsToday') : i18n.tc('account.subscription.trialEnds', days);
      }

      if (notice.reason === 'WalletAtZero') {
        return i18n.tc('account.subscription.walletZero', days);
      }

      return days === 0 ? i18n.t('account.subscription.walletLowToday') : i18n.tc('account.subscription.walletLow', days);
  }
}

/** "Top up 3,000.00 EGP to cover one month." for an administrator; "Ask your administrator…" for anyone else; empty without an amount. */
export function subscriptionAction(i18n: I18nService, notice: SubscriptionNotice): string {
  if (!notice.canManage) {
    return i18n.t('account.subscription.askAdministrator');
  }

  return notice.amountToTopUp !== null && notice.amountToTopUp > 0
    ? i18n.tf('account.subscription.amount', { amount: formatMoney(notice.amountToTopUp, notice.currencyCode, i18n.lang()) })
    : '';
}

/** The modal's title per level. */
export function subscriptionTitle(i18n: I18nService, notice: SubscriptionNotice): string {
  return i18n.t('account.subscription.title.' + notice.level);
}

/** A key that changes when the notice says something new (level, cause or days), so a dismissal covers only what was read. */
export function subscriptionNoticeKey(userId: string, notice: SubscriptionNotice): string {
  return `${userId}:${notice.level}:${notice.reason}:${notice.daysLeft ?? ''}`;
}
