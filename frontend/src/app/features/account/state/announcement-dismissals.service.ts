import { Injectable, computed, signal } from '@angular/core';

/**
 * LIS-CR-013 (C-17): the welcome announcements the user closed, for this session only — kept in memory (no web storage), so a
 * sign-out (a full page load) or a reload shows them again. Root-provided, so leaving and reopening the dashboard keeps them closed.
 */
@Injectable({ providedIn: 'root' })
export class AnnouncementDismissalsService {
  private readonly ids = signal<ReadonlySet<string>>(new Set<string>());

  readonly dismissed = computed(() => this.ids());

  dismiss(ids: readonly string[]): void {
    this.ids.update((current) => new Set([...current, ...ids]));
  }
}
