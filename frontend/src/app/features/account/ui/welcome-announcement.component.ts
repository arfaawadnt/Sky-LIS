import { ChangeDetectionStrategy, Component, computed, effect, inject, resource, signal, untracked } from '@angular/core';

import { ANNOUNCEMENT_API, type PublicAnnouncement, announcementText } from '../../../core/api/announcement.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { formatNumber } from '../data/account-format';
import { AccountAnnouncementsApi } from '../data/announcements.api';
import { AnnouncementDismissalsService } from '../state/announcement-dismissals.service';

/**
 * LIS-CR-013 (C-17, decision D-4): the dashboard's welcome card — the laboratory's live Welcome announcements as one card
 * (image, title, text, link in a new tab), paged when there are several, closed for the session with ✕ (nothing is stored).
 * Nothing shows when there is none, when the client opted out (the server answers none) or when the read fails. One impression
 * is reported per announcement shown and one click per link opened.
 */
@Component({
  selector: 'app-welcome-announcement',
  imports: [IconComponent],
  template: `
    @if (current(); as announcement) {
      <aside class="card" [attr.aria-label]="i18n.t('account.announcement.label')">
        @if (imageUrl(); as image) {
          <img [src]="image" alt="" (error)="onImageError(announcement.id)" />
        }
        <div class="text">
          <span class="eyebrow">{{ i18n.t('account.announcement.eyebrow') }}</span>
          <strong>{{ text().title }}</strong>
          @if (text().body; as body) {
            <p>{{ body }}</p>
          }
          <div class="actions">
            @if (announcement.linkUrl; as link) {
              <a class="btn sm primary" [href]="link" target="_blank" rel="noopener noreferrer" (click)="onLink(announcement)">{{ i18n.t('account.announcement.more') }}</a>
            }
            @if (visible().length > 1) {
              <button type="button" class="btn sm ghost" (click)="next()" [attr.aria-label]="i18n.t('account.announcement.next')">
                {{ i18n.tf('account.announcement.position', { index: number(position() + 1), total: number(visible().length) }) }} ›
              </button>
            }
          </div>
        </div>
        <button type="button" class="close" (click)="dismiss()" [attr.aria-label]="i18n.t('account.announcement.dismiss')" [title]="i18n.t('account.announcement.dismiss')">
          <app-icon name="x" [size]="16" />
        </button>
      </aside>
    }
  `,
  styles: `
    .card {
      position: relative;
      display: flex;
      flex-wrap: wrap;
      gap: var(--sp-4);
      align-items: center;
      margin-block-end: var(--sp-4);
      padding: var(--sp-4);
      padding-inline-end: calc(var(--sp-4) + 28px);
      border: 1px solid var(--line);
      border-radius: var(--r-lg);
      background: linear-gradient(135deg, var(--blue-soft), var(--surface));
    }

    img {
      flex: 0 1 220px;
      max-inline-size: 100%;
      max-block-size: 140px;
      object-fit: cover;
      border-radius: var(--r);
    }

    .text {
      flex: 1 1 260px;
      display: grid;
      gap: var(--sp-1);
      min-inline-size: 0;
    }

    .eyebrow {
      font-size: var(--fs-xs);
      font-weight: 600;
      color: var(--ink-2);
    }

    p {
      margin: 0;
      white-space: pre-line;
      color: var(--ink-2);
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--sp-2);
      margin-block-start: var(--sp-1);
    }

    .close {
      position: absolute;
      inset-block-start: var(--sp-2);
      inset-inline-end: var(--sp-2);
      border: 0;
      background: transparent;
      color: var(--ink-2);
      cursor: pointer;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WelcomeAnnouncementComponent {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(AccountAnnouncementsApi);
  private readonly dismissals = inject(AnnouncementDismissalsService);

  private readonly announcements = resource({
    loader: (): Promise<readonly PublicAnnouncement[]> => this.api.mine().catch(() => []),
  });

  /** The announcements not closed in this session. */
  protected readonly visible = computed<readonly PublicAnnouncement[]>(() => {
    const dismissed = this.dismissals.dismissed();
    return (this.announcements.hasValue() ? this.announcements.value() : []).filter((announcement) => !dismissed.has(announcement.id));
  });

  private readonly index = signal(0);

  protected readonly position = computed(() => (this.visible().length === 0 ? 0 : this.index() % this.visible().length));

  protected readonly current = computed<PublicAnnouncement | null>(() => this.visible()[this.position()] ?? null);

  protected readonly text = computed(() => {
    const current = this.current();
    return current === null ? { title: '', body: null } : announcementText(current, this.i18n.lang());
  });

  protected readonly failedImages = signal<readonly string[]>([]);

  protected readonly imageUrl = computed(() => {
    const current = this.current();
    return current !== null && current.imageVersion !== null && !this.failedImages().includes(current.id) ? ANNOUNCEMENT_API.publicImage(current.id, current.imageVersion) : null;
  });

  private readonly counted = new Set<string>();

  constructor() {
    effect(() => {
      const current = this.current();
      if (current !== null) {
        untracked(() => this.countImpression(current.id));
      }
    });
  }

  protected number(value: number): string {
    return formatNumber(value, this.i18n.lang());
  }

  protected onImageError(id: string): void {
    this.failedImages.update((ids) => [...ids, id]);
  }

  protected next(): void {
    this.index.update((value) => value + 1);
  }

  /** Closes the card for this session: every announcement it holds now stays closed until the next sign-in. */
  protected dismiss(): void {
    this.dismissals.dismiss(this.visible().map((announcement) => announcement.id));
  }

  protected onLink(announcement: PublicAnnouncement): void {
    this.api.click(announcement.id);
  }

  private countImpression(id: string): void {
    if (!this.counted.has(id)) {
      this.counted.add(id);
      this.api.impression(id);
    }
  }
}
