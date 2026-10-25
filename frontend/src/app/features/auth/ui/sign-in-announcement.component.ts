import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';

import { ANNOUNCEMENT_API, type PublicAnnouncement, announcementText } from '../../../core/api/announcement.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PublicAnnouncementsApi } from '../data/public-announcements.api';

/**
 * LIS-CR-013 (C-17, decision D-4): the active SignIn announcement of a laboratory address in the sign-in hero, instead of the
 * slogan — image, title (the page's heading), text and link (new tab). One impression is reported per announcement shown and
 * one click per link opened; neither waits for the server.
 */
@Component({
  selector: 'app-sign-in-announcement',
  template: `
    @if (imageUrl(); as image) {
      <img class="art" [src]="image" alt="" (error)="imageFailed.set(true)" />
    }
    <h1>{{ text().title }}</h1>
    @if (text().body; as body) {
      <p class="lead">{{ body }}</p>
    }
    @if (announcement().linkUrl; as link) {
      <a class="more" [href]="link" target="_blank" rel="noopener noreferrer" (click)="onLink()">{{ i18n.t('auth.announcement.more') }} ›</a>
    }
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--sp-3);
      max-width: 520px;
    }

    .art {
      inline-size: 100%;
      max-block-size: 240px;
      object-fit: cover;
      border-radius: var(--r-lg);
    }

    h1 {
      margin: 0;
      font-size: clamp(30px, 3.6vw, 46px);
      line-height: 1.1;
      font-weight: 600;
      color: var(--navy);
    }

    .lead {
      margin: 0;
      font-size: 17px;
      line-height: 1.6;
      white-space: pre-line;
      color: var(--ink-2);
    }

    .more {
      justify-self: start;
      font-weight: 600;
      color: var(--blue);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SignInAnnouncementComponent {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(PublicAnnouncementsApi);

  readonly announcement = input.required<PublicAnnouncement>();

  protected readonly imageFailed = signal(false);

  protected readonly text = computed(() => announcementText(this.announcement(), this.i18n.lang()));

  protected readonly imageUrl = computed(() => {
    const announcement = this.announcement();
    return announcement.imageVersion !== null && !this.imageFailed() ? ANNOUNCEMENT_API.publicImage(announcement.id, announcement.imageVersion) : null;
  });

  private readonly counted = new Set<string>();

  constructor() {
    effect(() => {
      const id = this.announcement().id;
      untracked(() => {
        this.imageFailed.set(false);
        if (!this.counted.has(id)) {
          this.counted.add(id);
          this.api.impression(id);
        }
      });
    });
  }

  protected onLink(): void {
    this.api.click(this.announcement().id);
  }
}
