import { ChangeDetectionStrategy, Component, input } from '@angular/core';

import type { AnnouncementPlacement } from '../../../core/api/announcement.contracts';

/**
 * SA-22 editor preview (LIS-CR-013): the announcement as a laboratory will see it — the sign-in hero (light brand ground,
 * large title) or the dashboard welcome card — in one language, with the image when there is one. Presentational only.
 */
@Component({
  selector: 'app-announcement-preview',
  template: `
    <figure class="frame" [class.hero]="placement() === 'SignIn'" [attr.dir]="dir()" [attr.lang]="lang()">
      @if (imageUrl(); as image) {
        <img [src]="image" alt="" />
      }
      <figcaption>
        <strong>{{ title() || '—' }}</strong>
        @if (body(); as text) {
          <span class="body">{{ text }}</span>
        }
        @if (link()) {
          <span class="link">{{ linkLabel() }} ›</span>
        }
      </figcaption>
    </figure>
  `,
  styles: `
    .frame {
      display: grid;
      gap: var(--sp-2);
      margin: 0;
      padding: var(--sp-3);
      border: 1px solid var(--line);
      border-radius: var(--r-lg);
      background: var(--surface);
    }

    .frame.hero {
      padding: var(--sp-4);
      background: linear-gradient(135deg, var(--blue-soft), var(--surface));
    }

    img {
      inline-size: 100%;
      max-block-size: 180px;
      object-fit: cover;
      border-radius: var(--r);
    }

    figcaption {
      display: grid;
      gap: var(--sp-1);
    }

    .hero strong {
      font-size: var(--fs-lg);
    }

    .body {
      white-space: pre-wrap;
      color: var(--ink-2);
      font-size: var(--fs-sm);
    }

    .link {
      color: var(--blue);
      font-weight: 600;
      font-size: var(--fs-sm);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AnnouncementPreviewComponent {
  readonly placement = input<AnnouncementPlacement>('SignIn');
  readonly title = input('');
  readonly body = input<string | null>(null);
  readonly link = input<string | null>(null);
  readonly linkLabel = input('');
  readonly imageUrl = input<string | null>(null);
  readonly lang = input<'en' | 'ar'>('en');
  readonly dir = input<'ltr' | 'rtl'>('ltr');
}
