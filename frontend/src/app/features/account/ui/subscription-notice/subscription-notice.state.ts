import { Injectable, signal } from '@angular/core';

/**
 * LIS-CR-012: what the user dismissed in this application session (memory only: no web storage, rule of 01 §6). A reload or a
 * new sign-in shows the notice again, which is the intent ("dismissable for the session"); a notice that says something new
 * (another level, cause or day count) has another key and shows again too.
 */
@Injectable({ providedIn: 'root' })
export class SubscriptionNoticeState {
  private readonly bannerDismissed = signal<ReadonlySet<string>>(new Set());
  private readonly modalSeen = signal<ReadonlySet<string>>(new Set());

  isBannerDismissed(key: string): boolean {
    return this.bannerDismissed().has(key);
  }

  dismissBanner(key: string): void {
    this.bannerDismissed.update((keys) => new Set([...keys, key]));
  }

  isModalSeen(key: string): boolean {
    return this.modalSeen().has(key);
  }

  markModalSeen(key: string): void {
    this.modalSeen.update((keys) => new Set([...keys, key]));
  }
}
