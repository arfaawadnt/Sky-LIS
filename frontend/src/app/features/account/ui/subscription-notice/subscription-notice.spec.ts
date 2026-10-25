import { Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import type { AuthProfile, SubscriptionNotice } from '../../../../core/api/auth.contracts';
import { AuthStore } from '../../../../core/auth/auth.store';
import { I18nService } from '../../../../core/i18n/i18n.service';
import { SUBSCRIPTION_WALLET_URL, SubscriptionBannerComponent, SubscriptionModalComponent, SubscriptionNoticeState } from './index';
import { subscriptionAction, subscriptionMessage, subscriptionNoticeKey } from './subscription-notice.text';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function notice(overrides: Partial<SubscriptionNotice> = {}): SubscriptionNotice {
  return {
    level: 'Warning',
    reason: 'Trial',
    daysLeft: 2,
    endsAtUtc: '2026-10-26T09:00:00Z',
    amountToTopUp: 3000,
    currencyCode: 'EGP',
    trialEndsAtUtc: '2026-10-26T09:00:00Z',
    canManage: true,
    showModal: true,
    ...overrides,
  };
}

function profile(subscriptionNotice: SubscriptionNotice | null): AuthProfile {
  return {
    userId: 'u-1',
    displayName: 'Mona',
    tenantId: 't-1',
    roleName: 'Administrator',
    permissions: [],
    language: 'en',
    scope: 'full',
    subscriptionNotice,
  };
}

@Component({
  selector: 'app-subscription-host',
  imports: [SubscriptionBannerComponent, SubscriptionModalComponent],
  template: '<app-subscription-banner /><app-subscription-modal />',
})
class HostComponent {}

/** A dictionary-less I18nService answers keys; these specs read keys and the tc / tf choices. */
describe('Subscription notice (LIS-CR-012 C-16)', () => {
  const current = signal<AuthProfile | null>(null);

  beforeEach(async () => {
    current.set(null);
    await TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([{ path: 'account/wallet', children: [] }]),
        { provide: AuthStore, useValue: { profile: current.asReadonly(), isAuthenticated: () => current() !== null } },
      ],
    }).compileComponents();
  });

  async function render(): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    await settle();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('shows the banner to an administrator with the message, the amount and the wallet link, and hides it once dismissed', async () => {
    current.set(profile(notice({ showModal: false })));
    const host = await render();

    const banner = host.querySelector('[data-testid="subscription-banner"]');
    expect(banner?.textContent).toContain('account.subscription.trialEnds');
    expect(banner?.textContent).toContain('account.subscription.amount');
    expect(banner?.querySelector('a')?.getAttribute('href')).toBe(SUBSCRIPTION_WALLET_URL);

    host.querySelector<HTMLButtonElement>('[data-testid="subscription-banner-dismiss"]')!.click();
    await settle();
    TestBed.tick();
    expect(host.querySelector('[data-testid="subscription-banner"]')).toBeNull();
    expect(TestBed.inject(SubscriptionNoticeState).isBannerDismissed(subscriptionNoticeKey('u-1', notice({ showModal: false })))).toBe(true);
  });

  it('shows no banner to a member who does not administer the laboratory, nor without a notice', async () => {
    current.set(profile(notice({ canManage: false, showModal: false })));
    let host = await render();
    expect(host.querySelector('[data-testid="subscription-banner"]')).toBeNull();

    current.set(profile(null));
    host = await render();
    expect(host.querySelector('[data-testid="subscription-banner"]')).toBeNull();
  });

  it('an expired subscription keeps the banner: it cannot be dismissed', async () => {
    current.set(profile(notice({ level: 'Expired', daysLeft: 0, showModal: false })));
    const host = await render();
    expect(host.querySelector('[data-testid="subscription-banner"]')?.textContent).toContain('account.subscription.expired');
    expect(host.querySelector('[data-testid="subscription-banner-dismiss"]')).toBeNull();
  });

  it('opens the modal when the notice calls for it, once per session, and takes an administrator to the wallet', async () => {
    current.set(profile(notice({ level: 'Grace', daysLeft: 1 })));
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    await render();

    const message = document.querySelector('[data-testid="subscription-modal-message"]');
    expect(message?.textContent).toContain('account.subscription.grace');
    document.querySelector<HTMLButtonElement>('[data-testid="subscription-modal-wallet"]')!.click();
    await settle();
    expect(navigate).toHaveBeenCalledWith(SUBSCRIPTION_WALLET_URL);
    expect(TestBed.inject(SubscriptionNoticeState).isModalSeen(subscriptionNoticeKey('u-1', notice({ level: 'Grace', daysLeft: 1 })))).toBe(true);
  });

  it('keeps the modal closed when the notice does not call for it', async () => {
    current.set(profile(notice({ daysLeft: 5, showModal: false })));
    await render();
    expect(document.querySelector('[data-testid="subscription-modal-message"]')).toBeNull();
  });

  it('chooses the sentence by level, cause and days, and tells a member to ask the administrator', () => {
    const i18n = TestBed.inject(I18nService);
    expect(subscriptionMessage(i18n, notice({ daysLeft: 0 }))).toBe('account.subscription.trialEndsToday');
    expect(subscriptionMessage(i18n, notice({ reason: 'Wallet', daysLeft: 0 }))).toBe('account.subscription.walletLowToday');
    expect(subscriptionMessage(i18n, notice({ reason: 'WalletAtZero', daysLeft: 20 }))).toBe('account.subscription.walletZero');
    expect(subscriptionMessage(i18n, notice({ level: 'Grace', daysLeft: 0 }))).toBe('account.subscription.graceToday');
    expect(subscriptionMessage(i18n, notice({ level: 'Expired', canManage: false }))).toBe('account.subscription.expiredMember');
    expect(subscriptionAction(i18n, notice({ canManage: false }))).toBe('account.subscription.askAdministrator');
    expect(subscriptionAction(i18n, notice({ amountToTopUp: 0 }))).toBe('');
  });
});
